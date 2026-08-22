using Kronikol.Tracking;

namespace Kronikol.Ingestion;

/// <summary>
/// Attribution and phase assignment for captures that arrive without a test identity of their own —
/// a database tee, an OTLP span exporter, a shared sidecar. Both passes work purely from the tests
/// NDJSON's timeline, so any capturer benefits without having to propagate headers.
/// </summary>
public static class IngestAttribution
{
    /// <summary>The interval one test occupied, from its <c>start</c> record to its <c>end</c> record.</summary>
    /// <param name="TestId">The scenario id.</param>
    /// <param name="Start">When the test began.</param>
    /// <param name="End">When the test ended — the last timestamp seen for it when no <c>end</c> record arrived.</param>
    public sealed record TestWindow(string TestId, DateTimeOffset Start, DateTimeOffset End);

    /// <summary>
    /// Derives one window per test from the tests records. A test with no <c>start</c> is skipped
    /// (there is nothing to bound it); a test whose run was killed before its <c>end</c> record is
    /// bounded by the last timestamp seen for it, so a crash cannot swallow the rest of the timeline.
    /// </summary>
    public static List<TestWindow> BuildWindows(IEnumerable<TestRunRecord>? testRecords)
    {
        var starts = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var ends = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var last = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var record in testRecords ?? [])
        {
            if (string.IsNullOrWhiteSpace(record.TestId) || record.Timestamp is not { } timestamp)
                continue;

            if (!last.ContainsKey(record.TestId))
                order.Add(record.TestId);
            if (!last.TryGetValue(record.TestId, out var seen) || timestamp > seen)
                last[record.TestId] = timestamp;

            if (record.Is(TestRunRecord.Events.Start))
            {
                if (!starts.TryGetValue(record.TestId, out var existing) || timestamp < existing)
                    starts[record.TestId] = timestamp;
            }
            else if (record.Is(TestRunRecord.Events.End))
            {
                if (!ends.TryGetValue(record.TestId, out var existing) || timestamp > existing)
                    ends[record.TestId] = timestamp;
            }
        }

        var windows = new List<TestWindow>();
        foreach (var testId in order)
        {
            if (!starts.TryGetValue(testId, out var start))
                continue;
            var end = ends.TryGetValue(testId, out var e) ? e : last.GetValueOrDefault(testId, start);
            if (end < start)
                end = start;
            windows.Add(new TestWindow(testId, start, end));
        }

        return windows;
    }

    /// <summary>
    /// Assigns a test id to every record that has none — its <c>testId</c> is empty or equals
    /// <paramref name="fallbackTestId"/> — by asking which test was running at its
    /// <see cref="InteractionRecord.Timestamp"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is deliberately boring so a reader can predict it: a record belongs to the test whose
    /// <c>[start, end]</c> interval contains its timestamp; when several do (a suite that nests or
    /// overlaps tests), the one that <em>started latest</em> wins — the innermost test in flight. A
    /// record that falls in no window is left exactly as it was, so
    /// <see cref="IngestRequest.FoldUnknownTestsInto"/> still collects it.
    /// </para>
    /// <para>
    /// A response is never attributed on its own: it follows the request it answers (matched by
    /// <c>requestResponseId</c>), because a call that starts inside a test can easily reply after the
    /// test's <c>end</c> record was written.
    /// </para>
    /// </remarks>
    /// <returns>The records with attribution applied, in the same order, and how many were attributed.</returns>
    public static (List<InteractionRecord> Records, int Attributed) AttributeByWindow(
        IReadOnlyList<InteractionRecord> records,
        IReadOnlyList<TestWindow> windows,
        string? fallbackTestId = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(windows);

        var result = new List<InteractionRecord>(records.Count);
        if (windows.Count == 0)
            return (records.ToList(), 0);

        // Requests decide; their responses inherit. Both passes run over the same list, so the map is
        // filled by the time a response is reached only when the response follows its request in the
        // file — which it does for every capturer that writes as it goes. A response that arrives first
        // is attributed on its own timestamp.
        var byRequestResponseId = new Dictionary<string, string>(StringComparer.Ordinal);
        var attributed = 0;

        foreach (var record in records)
        {
            if (!NeedsAttribution(record, fallbackTestId))
            {
                if (record.RequestResponseId is { Length: > 0 } known && !IsResponse(record))
                    byRequestResponseId[known] = record.TestId;
                result.Add(record);
                continue;
            }

            string? testId = null;
            if (IsResponse(record) && record.RequestResponseId is { Length: > 0 } id)
                byRequestResponseId.TryGetValue(id, out testId);

            testId ??= FindInnermost(windows, record.Timestamp);

            if (testId is null)
            {
                result.Add(record);
                continue;
            }

            if (record.RequestResponseId is { Length: > 0 } pairId && !IsResponse(record))
                byRequestResponseId[pairId] = testId;

            attributed++;
            result.Add(record with { TestId = testId });
        }

        return (result, attributed);
    }

    /// <summary>Whether a record still has to be attributed: no test id at all, or the capturer's fallback marker.</summary>
    public static bool NeedsAttribution(InteractionRecord record, string? fallbackTestId) =>
        string.IsNullOrWhiteSpace(record.TestId)
        || (!string.IsNullOrEmpty(fallbackTestId) && string.Equals(record.TestId, fallbackTestId, StringComparison.Ordinal));

    private static bool IsResponse(InteractionRecord record) =>
        string.Equals(record.Type, "Response", StringComparison.OrdinalIgnoreCase);

    /// <summary>The test in flight at <paramref name="timestamp"/> — the latest-started window containing it.</summary>
    private static string? FindInnermost(IReadOnlyList<TestWindow> windows, DateTimeOffset? timestamp)
    {
        if (timestamp is not { } when)
            return null;

        TestWindow? best = null;
        foreach (var window in windows)
        {
            if (when < window.Start || when > window.End)
                continue;
            if (best is null || window.Start > best.Start)
                best = window;
        }

        return best?.TestId;
    }

    /// <summary>
    /// The phase a top-level step puts the run in, derived from its keyword or
    /// <see cref="TestRunRecord.KeywordType"/>: <c>Given</c>/<c>Context</c> is setup, <c>When</c>/<c>Then</c>
    /// (<c>Action</c>/<c>Outcome</c>) is the action, and <c>And</c>/<c>But</c>/<c>Conjunction</c> inherit
    /// whatever came before.
    /// </summary>
    /// <param name="keyword">The literal keyword the producer wrote (<c>Given</c>, <c>And</c>, …).</param>
    /// <param name="keywordType">The Cucumber keyword type, when the producer supplied one; it wins over the literal keyword.</param>
    /// <param name="previous">The phase the preceding top-level step established.</param>
    public static TestPhase PhaseForStep(string? keyword, string? keywordType, TestPhase previous)
    {
        var word = (keywordType ?? keyword ?? string.Empty).Trim().ToLowerInvariant();
        return word switch
        {
            "given" or "context" => TestPhase.Setup,
            "when" or "then" or "action" or "outcome" or "butwhen" => TestPhase.Action,
            "and" or "but" or "conjunction" or "*" => previous,
            _ => previous,
        };
    }

    /// <summary>The interval one top-level step occupied, and the phase it puts the run in.</summary>
    /// <param name="TestId">The scenario the step belongs to.</param>
    /// <param name="Start">The step's timestamp.</param>
    /// <param name="End">The step's timestamp plus its duration (the timestamp itself when it has none).</param>
    /// <param name="Phase">The phase interactions inside the window get.</param>
    public sealed record StepWindow(string TestId, DateTimeOffset Start, DateTimeOffset End, TestPhase Phase);

    /// <summary>
    /// Derives one window per top-level, non-background <c>step</c> record that carries a timestamp,
    /// resolving <c>And</c>/<c>But</c> against the preceding step of the same test.
    /// </summary>
    public static List<StepWindow> BuildStepWindows(IEnumerable<TestRunRecord>? testRecords)
    {
        var windows = new List<StepWindow>();
        var previousPhase = new Dictionary<string, TestPhase>(StringComparer.Ordinal);

        foreach (var record in (testRecords ?? []).Where(r => r.Is(TestRunRecord.Events.Step))
                     .OrderBy(r => r.Timestamp ?? DateTimeOffset.MinValue))
        {
            if ((record.Level ?? 0) != 0 || record.Background == true || record.Timestamp is not { } start
                || string.IsNullOrWhiteSpace(record.TestId))
                continue;

            var phase = PhaseForStep(record.Keyword, record.KeywordType, previousPhase.GetValueOrDefault(record.TestId, TestPhase.Unknown));
            previousPhase[record.TestId] = phase;
            if (phase == TestPhase.Unknown)
                continue;

            var end = record.DurationMs is { } ms and > 0 ? start.AddMilliseconds(ms) : start;
            windows.Add(new StepWindow(record.TestId, start, end, phase));
        }

        return windows;
    }

    /// <summary>
    /// Tags interactions with the phase of the step they happened during, so <c>SeparateSetup</c> and
    /// <c>HighlightSetup</c> — until now reachable only from an in-process run, where
    /// <c>TestPhaseContext</c> is ambient — work for ingested runs too. Only records whose own phase is
    /// unset or <see cref="TestPhase.Unknown"/> are touched, so a capturer that knows better still wins.
    /// </summary>
    /// <returns>The records with phases applied, in the same order, and how many were tagged.</returns>
    public static (List<InteractionRecord> Records, int Tagged) ApplyPhaseFromSteps(
        IReadOnlyList<InteractionRecord> records,
        IReadOnlyList<StepWindow> stepWindows)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(stepWindows);

        if (stepWindows.Count == 0)
            return (records.ToList(), 0);

        var byTest = stepWindows.ToLookup(w => w.TestId, StringComparer.Ordinal);
        var result = new List<InteractionRecord>(records.Count);
        var tagged = 0;

        foreach (var record in records)
        {
            if (record.IsMarker || !string.IsNullOrWhiteSpace(record.Phase) && !string.Equals(record.Phase, nameof(TestPhase.Unknown), StringComparison.OrdinalIgnoreCase))
            {
                result.Add(record);
                continue;
            }

            var phase = FindPhase(byTest[record.TestId], record.Timestamp);
            if (phase is null)
            {
                result.Add(record);
                continue;
            }

            tagged++;
            result.Add(record with { Phase = phase.Value.ToString() });
        }

        return (result, tagged);
    }

    private static TestPhase? FindPhase(IEnumerable<StepWindow> windows, DateTimeOffset? timestamp)
    {
        if (timestamp is not { } when)
            return null;

        StepWindow? best = null;
        foreach (var window in windows)
        {
            if (when < window.Start || when > window.End)
                continue;
            if (best is null || window.Start > best.Start)
                best = window;
        }

        return best?.Phase;
    }
}
