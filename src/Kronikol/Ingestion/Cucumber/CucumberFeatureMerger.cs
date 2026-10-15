using System.Security.Cryptography;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Ingestion.Cucumber;

/// <summary>
/// Joins the two sources an ingested BDD run has: the Cucumber Messages file (the complete Gherkin
/// structure) and the tests NDJSON written by the test runner's own reporter (assertions, UI actions,
/// attachments, and the identity every captured interaction carries).
/// </summary>
/// <remarks>
/// <para>The rule is <em>messages win for structure</em>:</para>
/// <list type="bullet">
/// <item>For a scenario the messages own, the Cucumber-built <see cref="Scenario"/> replaces the one the
/// tests file produced: feature, description, rule, background, keywords, tables, doc strings, example
/// values and outcomes all come from Gherkin. The reporter's own <c>step</c> events for that scenario are
/// therefore dropped rather than duplicated, all but what a <c>bypassed</c> one says: its status and reason are
/// carried onto the Gherkin step with its text before the drop (<see cref="IngestPipeline"/>).</item>
/// <item>The tests file still contributes what only it knows: <c>assertion</c> events (nested under the
/// Gherkin step whose time window contains them — the ✓/✗ rows), the failure text when Gherkin carried none,
/// and the attachments the reporter recorded. Those are joined with the messages' copies of the same artefacts
/// wherever the messages put them, so each artefact is listed once (see <see cref="MergeAttachments"/>).</item>
/// <item>Scenarios the messages do <em>not</em> own — non-BDD tests, the "traffic outside any test" fold
/// bucket — are carried through from the tests-file model untouched.</item>
/// </list>
/// <para>
/// Both sources are keyed by the same scenario id because the fixture writes it as the
/// <c>kronikol-test-id</c> attachment; see <see cref="CucumberSynthesisResult.JoinedTestIds"/> for the
/// scenarios where that attachment was missing and the join could not be made.
/// </para>
/// </remarks>
public static class CucumberFeatureMerger
{
    /// <summary>
    /// Merges a Cucumber synthesis into the model <see cref="FeatureSynthesizer.Build(IEnumerable{TestRunRecord}, IEnumerable{RequestResponseLog}, string, ExecutionResult, string)"/> produced from the
    /// tests file and the interaction logs.
    /// </summary>
    /// <param name="cucumber">What the messages file yielded.</param>
    /// <param name="fromTestsFile">What the tests file and the interaction logs yielded.</param>
    /// <param name="testRecords">
    /// The raw tests-file records, used to place <c>assertion</c> events inside the Gherkin steps. Pass
    /// null to skip assertion grafting.
    /// </param>
    public static FeatureSynthesizer.Result Merge(
        CucumberSynthesisResult cucumber,
        FeatureSynthesizer.Result fromTestsFile,
        IEnumerable<TestRunRecord>? testRecords = null)
    {
        ArgumentNullException.ThrowIfNull(cucumber);
        ArgumentNullException.ThrowIfNull(fromTestsFile);

        var owned = new HashSet<string>(cucumber.TestNames.Keys, StringComparer.Ordinal);
        if (owned.Count == 0)
            return fromTestsFile;

        var replaced = new Dictionary<string, Scenario>(StringComparer.Ordinal);
        foreach (var scenario in cucumber.Features.SelectMany(f => f.Scenarios))
            replaced[scenario.Id] = scenario;

        // What the tests file knew about the same scenario, so nothing it contributed is lost.
        var keptTwice = 0;
        foreach (var feature in fromTestsFile.Features)
        {
            foreach (var scenario in feature.Scenarios)
            {
                if (replaced.TryGetValue(scenario.Id, out var target))
                    keptTwice += CarryOver(scenario, target, cucumber);
            }
        }

        GraftAssertions(cucumber, testRecords);

        // Scenarios the messages do not own keep their place in the tests-file model.
        var leftovers = new List<Feature>();
        foreach (var feature in fromTestsFile.Features)
        {
            var keep = feature.Scenarios.Where(s => !owned.Contains(s.Id)).ToArray();
            if (keep.Length > 0)
                leftovers.Add(feature with { Scenarios = keep });
        }

        var features = MergeFeatures(cucumber.Features, leftovers);

        var names = new Dictionary<string, string>(fromTestsFile.TestNames, StringComparer.Ordinal);
        foreach (var (id, name) in cucumber.TestNames)
            names[id] = name;

        var start = fromTestsFile.Start == default ? cucumber.Start : Min(fromTestsFile.Start, cucumber.Start);
        var end = Max(fromTestsFile.End, cucumber.End);
        return new FeatureSynthesizer.Result(features, start, end, names)
        {
            // The tests file's scenarios the messages do not own keep the verdict they defaulted to, and the count of them.
            DefaultedResultCount = features.SelectMany(f => f.Scenarios).Count(s => s.ResultDefaulted),
            AttachmentsKeptTwice = keptTwice,
        };
    }

    /// <summary>
    /// True for a tests-file <c>step</c> record that the messages replace: the reporter's own step events
    /// for a BDD scenario would otherwise draw a second set of delimiter bars next to the Gherkin ones. A
    /// <c>bypassed</c> one is replaced too, once the synthesis has applied its status and reason to its Gherkin step.
    /// </summary>
    public static bool IsReplacedStep(TestRunRecord record, CucumberSynthesisResult cucumber)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(cucumber);
        return string.Equals(record.Event, "step", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(record.TestId)
               && cucumber.TestNames.ContainsKey(record.TestId);
    }

    /// <summary>
    /// Joins the tests file's attachments of a scenario the messages own with the messages' copies of the same artefacts,
    /// wherever the messages put them: the scenario's own list (which holds the attachments of the hooks dropped), a
    /// background step, a Gherkin step, a hook step kept, a sub-step. playwright-bdd embeds every attachment in the messages
    /// as BASE64, so each artefact a reporter also wrote to a file is described twice (#175).
    /// </summary>
    /// <remarks>
    /// <para>Each tests attachment, in its order, takes the first copy not yet taken that has its name, the producer's own
    /// (<c>trace</c>) or the one the synthesis displays (<c>trace.zip</c>), compared ignoring case; and, when the reporter's
    /// file can be read, the same bytes. Two artefacts may share a name, and screenshots of an unchanged page share their
    /// bytes whatever they are called, so the bytes check a pair and never make one. A link is never a copy of a file. A
    /// copy that took its pair is never offered again.</para>
    /// <para>A pair is one entry under the reporter's name, where the messages put it. It links the reporter's file when that
    /// exists, and the decoded copy is deleted; else it links the messages' copy. It takes the media type of the file it
    /// links. The scenario's own list is the tests entries in their order, then the messages' entries no tests entry took.
    /// The test id and bypass attachments are signals, never listed. What pairs with nothing is kept where it was.</para>
    /// </remarks>
    /// <returns>How many tests attachments are kept beside a messages copy of the same name whose bytes differ.</returns>
    internal static int MergeAttachments(Scenario target, FileAttachment[] fromTests, CucumberSynthesisResult cucumber)
    {
        // Every list on the messages' side, in reading order: the scenario's own first.
        var holders = new List<Holder> { new(a => target.Attachments = a, [.. target.Attachments ?? []], IsScenario: true) };
        Walk(target.BackgroundSteps);
        Walk(target.Steps);

        var descriptions = new Dictionary<string, (long Length, byte[] Hash)?>(StringComparer.Ordinal);
        // A slot that took its pair is never a candidate again: per-step screenshots of an unchanged page share a name and
        // their bytes, and a merged entry left in its step would take the next one's pair.
        var taken = new HashSet<(int Holder, int Index)>();
        var scenarioList = new List<FileAttachment>();
        var unpaired = new List<FileAttachment>();
        foreach (var fromTest in fromTests)
        {
            if (cucumber.SignalAttachmentNames.Contains(fromTest.Name))
                continue;
            if (!TryPair(fromTest, out var holder, out var index, out var merged))
            {
                scenarioList.Add(fromTest);
                unpaired.Add(fromTest);
                continue;
            }

            taken.Add((holder, index));
            if (holders[holder].IsScenario)
            {
                holders[holder].Items[index] = null;
                scenarioList.Add(merged);
            }
            else
            {
                holders[holder].Items[index] = merged;
            }
        }

        var keptTwice = unpaired.Count(KeptBesideItsNamesake);

        foreach (var (set, items, isScenario) in holders)
        {
            FileAttachment[] kept = isScenario ? [.. scenarioList, .. items.OfType<FileAttachment>()] : [.. items.OfType<FileAttachment>()];
            set(kept.Length > 0 ? kept : null);
        }

        return keptTwice;

        void Walk(ScenarioStep[]? steps)
        {
            foreach (var step in steps ?? [])
            {
                if (step.Attachments is { Length: > 0 } own)
                    holders.Add(new Holder(a => step.Attachments = a, [.. own], IsScenario: false));
                Walk(step.SubSteps);
            }
        }

        bool TryPair(FileAttachment fromTest, out int holder, out int index, out FileAttachment merged)
        {
            if (!fromTest.IsUrl)
            {
                var reportersFileExists = File.Exists(fromTest.RelativePath);
                for (holder = 0; holder < holders.Count; holder++)
                {
                    var items = holders[holder].Items;
                    for (index = 0; index < items.Count; index++)
                    {
                        if (items[index] is not { IsUrl: false } candidate || taken.Contains((holder, index)) || !SameName(fromTest, candidate))
                            continue;
                        var origin = cucumber.AttachmentOrigins.GetValueOrDefault(candidate);
                        if (!reportersFileExists)
                        {
                            // The reporter's file is gone (Playwright empties test-results/ on its next run): the name pairs
                            // them, and the copy that exists is kept, with its own media type.
                            merged = new FileAttachment(fromTest.Name, candidate.RelativePath, candidate.MediaType ?? fromTest.MediaType);
                            return true;
                        }

                        if (!SameBytes(fromTest.RelativePath, candidate, origin))
                            continue;
                        if (origin?.WrittenPath is { } written)
                            TryDelete(written);
                        merged = new FileAttachment(fromTest.Name, fromTest.RelativePath, fromTest.MediaType ?? candidate.MediaType);
                        return true;
                    }
                }
            }

            holder = -1;
            index = -1;
            merged = fromTest;
            return false;
        }

        // A reporter that names a record by its file writes what the synthesis displays (Playwright's trace.zip), and
        // playwright-bdd passes Playwright's own name through as the attachment's fileName (trace).
        bool SameName(FileAttachment fromTest, FileAttachment candidate) =>
            string.Equals(cucumber.AttachmentOrigins.GetValueOrDefault(candidate)?.OriginalName ?? candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase);

        bool SameBytes(string reportersFile, FileAttachment candidate, CucumberAttachmentOrigin? origin)
        {
            (long Length, byte[] Hash)? theirs = origin is { Length: { } length, Sha256: { } hash } ? (length, hash) : Describe(candidate.RelativePath);
            return Describe(reportersFile) is { } mine && theirs is { } copy
                   && mine.Length == copy.Length && mine.Hash.AsSpan().SequenceEqual(copy.Hash);
        }

        // An unpaired tests attachment whose reporter's file was read, beside a copy no one took that has its name: two
        // entries with one name, whose bytes differ (bytes that matched would have paired them).
        bool KeptBesideItsNamesake(FileAttachment fromTest)
        {
            if (fromTest.IsUrl || Describe(fromTest.RelativePath) is null)
                return false;
            for (var holder = 0; holder < holders.Count; holder++)
            {
                var items = holders[holder].Items;
                for (var index = 0; index < items.Count; index++)
                {
                    if (items[index] is { IsUrl: false } candidate && !taken.Contains((holder, index)) && SameName(fromTest, candidate))
                        return true;
                }
            }

            return false;
        }

        (long Length, byte[] Hash)? Describe(string path)
        {
            if (descriptions.TryGetValue(path, out var known))
                return known;
            try
            {
                using var stream = File.OpenRead(path);
                return descriptions[path] = (stream.Length, SHA256.HashData(stream));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return descriptions[path] = null;
            }
        }
    }

    /// <summary>One list of attachments on the messages' side of a scenario: its own, or one step's.</summary>
    private sealed record Holder(Action<FileAttachment[]?> Set, List<FileAttachment?> Items, bool IsScenario);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* left for the folder's own deletion */ }
    }

    /// <summary>
    /// Keeps the scenario-level facts only the reporter has: attachments it recorded, and the failure text
    /// when the messages carried none (a Playwright timeout fails the test without failing a Gherkin step).
    /// </summary>
    /// <returns>How many of its attachments are kept beside a messages copy of the same name with other bytes.</returns>
    private static int CarryOver(Scenario fromTests, Scenario target, CucumberSynthesisResult cucumber)
    {
        var keptTwice = fromTests.Attachments is { Length: > 0 } attachments ? MergeAttachments(target, attachments, cucumber) : 0;

        target.ErrorMessage ??= fromTests.ErrorMessage;
        target.ErrorStackTrace ??= fromTests.ErrorStackTrace;
        if (target.Duration is null)
            target.Duration = fromTests.Duration;
        return keptTwice;
    }

    /// <summary>
    /// Nests every <c>assertion</c> record under the Gherkin step whose window contains its timestamp —
    /// the same placement the sequence diagram gives the ✓/✗ note. Assertions outside every window join
    /// the nearest preceding step; assertions for a scenario with no steps are appended as top-level rows.
    /// </summary>
    private static void GraftAssertions(CucumberSynthesisResult cucumber, IEnumerable<TestRunRecord>? testRecords)
    {
        if (testRecords is null || !StepCollector.Options.IncludeTrackedAssertionsInStepList)
            return;

        var byTest = new Dictionary<string, List<TestRunRecord>>(StringComparer.Ordinal);
        foreach (var record in testRecords)
        {
            if (!string.Equals(record.Event, "assertion", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(record.TestId)
                || !cucumber.StepWindows.ContainsKey(record.TestId))
                continue;
            if (!byTest.TryGetValue(record.TestId, out var list))
                byTest[record.TestId] = list = [];
            list.Add(record);
        }

        if (byTest.Count == 0)
            return;

        var scenarios = cucumber.Features.SelectMany(f => f.Scenarios).ToDictionary(s => s.Id, StringComparer.Ordinal);

        foreach (var (testId, records) in byTest)
        {
            var windows = cucumber.StepWindows[testId];
            var extra = new List<ScenarioStep>();
            var children = new Dictionary<ScenarioStep, List<ScenarioStep>>();

            foreach (var record in records.OrderBy(r => r.Timestamp ?? DateTimeOffset.MinValue))
            {
                var step = BuildAssertionStep(record);
                var host = FindHost(windows, record.Timestamp);
                if (host is null)
                    extra.Add(step);
                else
                {
                    if (!children.TryGetValue(host, out var list))
                        children[host] = list = [.. host.SubSteps ?? []];
                    list.Add(step);
                }
            }

            foreach (var (parent, list) in children)
                parent.SubSteps = list.ToArray();

            if (extra.Count > 0 && scenarios.TryGetValue(testId, out var scenario))
                scenario.Steps = (scenario.Steps ?? []).Concat(extra).ToArray();
        }
    }

    /// <summary>
    /// The step that was running at <paramref name="timestamp"/>: the last one whose delimiter bar had
    /// already been drawn. Two steps can share a boundary instant (a producer that reports a step as
    /// instantaneous), so the scan runs backwards and the later step wins — the same rule the sequence
    /// diagram follows when it places a note between two bars.
    /// </summary>
    private static ScenarioStep? FindHost(IReadOnlyList<CucumberStepWindow> windows, DateTimeOffset? timestamp)
    {
        if (timestamp is not { } at)
            return null;

        for (var i = windows.Count - 1; i >= 0; i--)
        {
            if (windows[i].Start <= at)
                return windows[i].Step;
        }

        return null;
    }

    private static ScenarioStep BuildAssertionStep(TestRunRecord record)
    {
        var passed = !string.Equals(record.Status, "failed", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(record.Status, "fail", StringComparison.OrdinalIgnoreCase);
        return new ScenarioStep
        {
            IsAssertion = true,
            Text = $"{(passed ? Track.PassSymbol : Track.FailSymbol)} {record.Text ?? "assertion"}",
            Status = passed ? ExecutionResult.Passed : ExecutionResult.Failed,
            Comments = passed || string.IsNullOrWhiteSpace(record.Error) ? null : [record.Error!],
            Duration = record.DurationMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
            // As the tests lane builds an assertion: what Failures.md prints, and where it was written.
            FailureMessage = passed || string.IsNullOrWhiteSpace(record.Error) ? null : record.Error,
            SourceFile = SourcePaths.FileName(record.SourceFile),
            SourceLine = record.SourceLine,
        };
    }

    /// <summary>Cucumber features first (in Gherkin order), then whatever the tests file had left over.</summary>
    private static Feature[] MergeFeatures(Feature[] cucumber, List<Feature> leftovers)
    {
        var result = new List<Feature>(cucumber);
        var byName = result.ToDictionary(f => f.DisplayName, StringComparer.Ordinal);
        foreach (var feature in leftovers)
        {
            if (byName.TryGetValue(feature.DisplayName, out var existing))
                existing.Scenarios = existing.Scenarios.Concat(feature.Scenarios).ToArray();
            else
                result.Add(feature);
        }

        return result.ToArray();
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
