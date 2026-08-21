using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Ingestion;

/// <summary>What to ingest and how to render it. See <see cref="IngestPipeline.Run"/>.</summary>
public sealed class IngestRequest
{
    /// <summary>NDJSON files of <see cref="InteractionRecord"/> lines (in addition to <see cref="Interactions"/>).</summary>
    public IReadOnlyList<string> InteractionFiles { get; init; } = [];

    /// <summary>In-memory interaction records (in addition to <see cref="InteractionFiles"/>).</summary>
    public IEnumerable<InteractionRecord>? Interactions { get; init; }

    /// <summary>Optional tests NDJSON file (<see cref="TestRunRecord"/> lines) supplying outcomes, durations and steps.</summary>
    public string? TestsFile { get; init; }

    /// <summary>In-memory test-run records (in addition to <see cref="TestsFile"/>).</summary>
    public IEnumerable<TestRunRecord>? TestRecords { get; init; }

    /// <summary>Report options. <c>ReportsFolderPath</c> decides the output directory. Defaults to <see cref="IngestPipeline.DefaultOptions"/>.</summary>
    public ReportConfigurationOptions? Options { get; init; }

    /// <summary>Clear the in-process store (<see cref="RequestResponseLogger.Clear"/>) before replaying. Default <c>true</c> — ingest is a whole-run replay.</summary>
    public bool ClearExistingLogs { get; init; } = true;

    /// <summary>Feature name for scenarios that carry none.</summary>
    public string DefaultFeatureName { get; init; } = "Ingested";

    /// <summary>Verdict for tests that have interactions but no <c>end</c> record in the tests file.</summary>
    public ExecutionResult ResultWhenUnknown { get; init; } = ExecutionResult.Passed;

    /// <summary>When true, the report is generated even if no scenario could be synthesised (it will be empty). Default <c>false</c>.</summary>
    public bool AllowEmpty { get; init; }

    /// <summary>
    /// Order each test's interactions as a call tree instead of strictly by timestamp: every response sits
    /// directly after its request, calls a service made while handling a request (their interval lies inside
    /// the parent's and their caller is the parent's service) sit between that request and its response,
    /// and siblings keep request-time order. Default <c>true</c>: externally captured traffic is usually
    /// concurrent, and a chronologically interleaved diagram makes it impossible to tell which reply answers
    /// which call (and defeats <c>CollapseConsecutiveIdenticalCalls</c>, which works on adjacent pairs).
    /// Set <c>false</c> for a strict timeline.
    /// </summary>
    public bool CallTreeOrdering { get; init; } = true;

    /// <summary>
    /// When set, interactions whose <c>testId</c> is not a test in the tests records — or every
    /// interaction, when there are no tests records — are re-attributed to this one scenario (typically
    /// "Traffic outside any test": warm-ups, health probes, manual browsing, background jobs). Leave
    /// null to render each unknown test id as its own scenario.
    /// </summary>
    public UnknownTestFold? FoldUnknownTestsInto { get; init; }
}

/// <summary>The single scenario that collects interactions of unknown tests (<see cref="IngestRequest.FoldUnknownTestsInto"/>).</summary>
/// <param name="ScenarioName">Display name, e.g. "Traffic outside any test".</param>
/// <param name="ScenarioId">Scenario id (also the <c>testId</c> the folded interactions take).</param>
public sealed record UnknownTestFold(string ScenarioName, string ScenarioId = "outside-any-test");

/// <summary>Outcome of <see cref="IngestPipeline.Run"/>.</summary>
/// <param name="InteractionCount">Interaction lines replayed into the store.</param>
/// <param name="ScenarioCount">Scenarios in the synthesised model.</param>
/// <param name="Features">The synthesised model.</param>
/// <param name="ReportsDirectory">Where the report files were written.</param>
/// <param name="Start">Run start (UTC).</param>
/// <param name="End">Run end (UTC).</param>
/// <param name="Generated">False when nothing was ingested and the report was skipped.</param>
public sealed record IngestResult(
    int InteractionCount,
    int ScenarioCount,
    Feature[] Features,
    string ReportsDirectory,
    DateTime Start,
    DateTime End,
    bool Generated)
{
    /// <summary>Path of the main HTML report (may not exist when <see cref="Generated"/> is false).</summary>
    public string TestRunReportHtml => Path.Combine(ReportsDirectory, "TestRunReport.html");
}

/// <summary>
/// Replays NDJSON captures into the in-process store and generates the standard Kronikol reports from
/// them — the programmatic form of <c>kronikol ingest</c>. Use it when a capturer (an out-of-process
/// proxy tap, a Java/Node service, any tool that can write <see cref="InteractionRecord"/> lines) runs
/// outside the test process, or when a host wants to regenerate a report from files after the fact.
/// </summary>
/// <remarks>
/// Order of operations: read everything → (optionally) clear the store → replay records in timestamp
/// order (the sequence diagram follows enqueue order, not timestamps) → normalise each log's
/// <c>TestName</c> from the tests file → reset the diagram cache → synthesise <see cref="Feature"/>s →
/// <see cref="ReportGenerator.CreateStandardReportsWithDiagrams"/>. Capture-time redaction
/// (<see cref="RequestResponseLogger.Redaction"/>) applies during replay, so secrets present in a raw
/// capture file can still be kept out of the report data files.
/// </remarks>
public static class IngestPipeline
{
    /// <summary>
    /// Defaults suited to externally captured traffic: browser-side PlantUML rendering, no internal-flow
    /// tracking (there are no in-process spans to show), component diagram on, consecutive identical
    /// calls collapsed.
    /// </summary>
    public static ReportConfigurationOptions DefaultOptions() => new()
    {
        PlantUmlRendering = PlantUmlRendering.BrowserJs,
        InternalFlowTracking = false,
        GenerateComponentDiagram = true,
        CollapseConsecutiveIdenticalCalls = true,
        ShowNoInteractionsMarker = true,
    };

    /// <summary>Runs the pipeline. Throws <see cref="FormatException"/> for malformed input lines and <see cref="FileNotFoundException"/> for missing files.</summary>
    public static IngestResult Run(IngestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.Options ?? DefaultOptions();

        var records = new List<InteractionRecord>();
        foreach (var file in request.InteractionFiles)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException("Interaction file not found.", file);
            records.AddRange(NdjsonInteractionReader.ReadFile(file));
        }

        if (request.Interactions is not null)
            records.AddRange(request.Interactions);

        var testRecords = new List<TestRunRecord>();
        if (!string.IsNullOrWhiteSpace(request.TestsFile))
        {
            if (!File.Exists(request.TestsFile))
                throw new FileNotFoundException("Tests file not found.", request.TestsFile);
            testRecords.AddRange(NdjsonTestRunReader.ReadFile(request.TestsFile));
        }

        if (request.TestRecords is not null)
            testRecords.AddRange(request.TestRecords);

        var reportsDirectory = ReportGenerator.ResolveReportsDirectory(options);

        if (request.ClearExistingLogs)
            RequestResponseLogger.Clear();

        // Stable sort by timestamp: entries without a timestamp keep their file order relative to
        // each other and sort first.
        var ordered = records
            .Select((record, index) => (record, index))
            .OrderBy(x => x.record.Timestamp ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.index)
            .Select(x => x.record)
            .ToList();

        if (request.FoldUnknownTestsInto is { } fold)
        {
            var known = new HashSet<string>(
                testRecords.Where(t => !string.IsNullOrWhiteSpace(t.TestId)).Select(t => t.TestId!),
                StringComparer.Ordinal);
            ordered = ordered
                .Select(r => known.Contains(r.TestId) || r.TestId == fold.ScenarioId
                    ? r
                    : r with { TestId = fold.ScenarioId, TestName = fold.ScenarioName })
                .ToList();
        }

        if (request.CallTreeOrdering)
            ordered = OrderAsCallTree(ordered);

        // Names from the tests file win over whatever each hop knew.
        var namesFromTests = testRecords
            .Where(t => !string.IsNullOrWhiteSpace(t.TestId) && !string.IsNullOrWhiteSpace(t.TestName))
            .GroupBy(t => t.TestId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().TestName!, StringComparer.Ordinal);

        // First non-empty name per test id among the records themselves, so every hop of one test
        // carries the same label even without a tests file.
        var namesFromRecords = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in ordered)
        {
            if (!string.IsNullOrWhiteSpace(record.TestName) && record.TestName != TestIdentityScope.UnknownTestName
                && !namesFromRecords.ContainsKey(record.TestId))
                namesFromRecords[record.TestId] = record.TestName;
        }

        var logs = new List<RequestResponseLog>(ordered.Count);
        foreach (var record in ordered)
        {
            var name = namesFromTests.TryGetValue(record.TestId, out var fromTests) ? fromTests
                : namesFromRecords.TryGetValue(record.TestId, out var fromRecords) ? fromRecords
                : null;
            var log = record.ToLog(name);
            RequestResponseLogger.Log(log);
            logs.Add(log);
        }

        var synthesised = FeatureSynthesizer.Build(testRecords, logs, request.DefaultFeatureName, request.ResultWhenUnknown);
        var scenarioCount = synthesised.Features.Sum(f => f.Scenarios.Length);

        if (scenarioCount == 0 && !request.AllowEmpty)
            return new IngestResult(logs.Count, 0, synthesised.Features, reportsDirectory, synthesised.Start, synthesised.End, Generated: false);

        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagrams(synthesised.Features, synthesised.Start, synthesised.End, options);
        DefaultDiagramsFetcher.Reset();

        return new IngestResult(logs.Count, scenarioCount, synthesised.Features, reportsDirectory, synthesised.Start, synthesised.End, Generated: true);
    }

    /// <summary>Above this many units in one test, nesting is skipped (pairs only) to keep ingest linear.</summary>
    private const int NestingLimit = 5000;

    /// <summary>
    /// Orders a timestamp-sorted list as a call tree. A unit is a request with its response (matched by
    /// <c>requestResponseId</c>) or a lone record. Each response sits directly after its request; a call a
    /// service made while handling a request — its interval lies inside the parent's and its caller is the
    /// parent's service — sits between that request and its response; siblings keep request-time order.
    /// Responses with no earlier request and records without an id keep their place. Records are grouped
    /// by <c>testId</c> first, so nesting never crosses tests.
    /// </summary>
    internal static List<InteractionRecord> OrderAsCallTree(IReadOnlyList<InteractionRecord> ordered)
    {
        var open = new Dictionary<string, CallUnit>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<CallUnit>>(StringComparer.Ordinal);
        var groupOrder = new List<string>();
        foreach (var record in ordered)
        {
            var isResponse = string.Equals(record.Type, "Response", StringComparison.OrdinalIgnoreCase);
            if (isResponse && record.RequestResponseId is { Length: > 0 } id && open.Remove(id, out var unit))
            {
                unit.Response = record;
                continue;
            }

            var fresh = new CallUnit(record, isRequest: !isResponse);
            if (!isResponse && record.RequestResponseId is { Length: > 0 } requestId)
                open[requestId] = fresh;
            if (!groups.TryGetValue(record.TestId, out var group))
            {
                group = [];
                groups[record.TestId] = group;
                groupOrder.Add(record.TestId);
            }

            group.Add(fresh);
        }

        var result = new List<InteractionRecord>(ordered.Count);
        foreach (var testId in groupOrder)
        {
            var group = groups[testId]; // in start order: the input is timestamp-sorted
            if (group.Count <= NestingLimit)
            {
                for (var i = 0; i < group.Count; i++)
                {
                    var unit = group[i];
                    if (unit.Start is null)
                        continue;
                    // Parent: the latest-started earlier request whose interval contains this one and
                    // whose service is this unit's caller — the innermost call that can have caused it.
                    for (var j = i - 1; j >= 0; j--)
                    {
                        var candidate = group[j];
                        if (!candidate.IsRequest || candidate.Start is null || candidate.End is null)
                            continue;
                        if (candidate.Start <= unit.Start && candidate.End >= unit.End
                            && string.Equals(candidate.Request.ServiceName, unit.Request.CallerName, StringComparison.Ordinal))
                        {
                            unit.Parent = candidate;
                            candidate.Children.Add(unit);
                            break;
                        }
                    }
                }
            }

            foreach (var root in group)
            {
                if (root.Parent is null)
                    Emit(root, result);
            }
        }

        return result;

        static void Emit(CallUnit unit, List<InteractionRecord> into)
        {
            into.Add(unit.Request);
            foreach (var child in unit.Children)
                Emit(child, into);
            if (unit.Response is not null)
                into.Add(unit.Response);
        }
    }

    private sealed class CallUnit(InteractionRecord request, bool isRequest)
    {
        public InteractionRecord Request { get; } = request;
        public bool IsRequest { get; } = isRequest;
        public InteractionRecord? Response { get; set; }
        public DateTimeOffset? Start => Request.Timestamp;
        public DateTimeOffset? End => Response?.Timestamp ?? Request.Timestamp;
        public CallUnit? Parent { get; set; }
        public List<CallUnit> Children { get; } = [];
    }
}
