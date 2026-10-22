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
    /// <remarks>
    /// The tests file's attachments of a scenario the messages own, its steps' included, are joined with the messages' copies
    /// of the same artefacts (<see cref="MergeAttachments"/>). A decoded copy that the reporter's own file replaced is
    /// deleted from <see cref="CucumberSynthesisOptions.AttachmentsDirectory"/> once no entry links it.
    /// </remarks>
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
    /// <para>A tests attachment pairs with the first copy not yet taken that has its name, the producer's own (<c>trace</c>)
    /// or the one the synthesis displays (<c>trace.zip</c>), compared ignoring case. The reporter's files that can be read
    /// pair first, in their order, each with a copy of the same bytes, or else with one whose bytes cannot be read (a link to
    /// a file that is gone). Two artefacts may share a name, and screenshots of an unchanged page share their bytes whatever
    /// they are called, so the bytes check a pair and never make one. Then a reporter's file that cannot be read (gone, or
    /// held by another process) pairs by its name alone with a copy that can. A link is never a copy of a file. A copy that
    /// took its pair is never offered again.</para>
    /// <para>A pair is one entry under the reporter's name, where the messages put it. It links the reporter's file when that
    /// can be read, and the decoded copy is deleted unless another entry still links it; else it links the messages' copy. It
    /// takes the media type of the file it links. The scenario's own list is the tests attachments no step's copy took, in
    /// their order, then the messages' entries no tests attachment took. The test id and bypass attachments are signals,
    /// never listed. A copy that pairs with nothing is kept where it was.</para>
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
        var pairs = new Dictionary<int, (int Holder, int Index, FileAttachment Merged)>();
        var replaced = new List<string>();

        // The reporter's files that can be read pair first, by their bytes. In one pass in the reporter's order, a file that
        // is gone took the first copy of its name, which could be the one a later file's bytes match.
        var unreadable = new List<int>();
        for (var i = 0; i < fromTests.Length; i++)
        {
            var fromTest = fromTests[i];
            // A reporter's file is read only to tell copies of its name apart, so one with no namesake is never hashed.
            if (IsSignal(fromTest) || fromTest.IsUrl || First(Namesakes(fromTest)) is null)
                continue;
            if (Describe(fromTest.RelativePath) is not { } mine)
            {
                unreadable.Add(i);
                continue;
            }

            // The copy with the same bytes; else one whose bytes cannot be read (the messages name a file that is gone), as
            // the reporter's file is then the only one there.
            var found = First(Namesakes(fromTest).Where(n => BytesOf(n.Candidate) is { } copy && copy.Length == mine.Length && copy.Hash.AsSpan().SequenceEqual(mine.Hash)))
                        ?? First(Namesakes(fromTest).Where(n => BytesOf(n.Candidate) is null));
            if (found is not { } slot)
                continue;
            Take(i, slot, new FileAttachment(fromTest.Name, fromTest.RelativePath, fromTest.MediaType ?? slot.Candidate.MediaType));
            if (cucumber.AttachmentOrigins.GetValueOrDefault(slot.Candidate)?.WrittenPath is { } written)
                replaced.Add(written);
        }

        // The reporter's file is gone (Playwright empties test-results/ on its next run) or cannot be read: the name pairs it
        // with a copy that can be, and that copy is kept, with its own media type.
        foreach (var i in unreadable)
        {
            var fromTest = fromTests[i];
            if (First(Namesakes(fromTest).Where(n => BytesOf(n.Candidate) is not null)) is { } slot)
                Take(i, slot, new FileAttachment(fromTest.Name, slot.Candidate.RelativePath, slot.Candidate.MediaType ?? fromTest.MediaType));
        }

        var scenarioList = new List<FileAttachment>();
        var keptTwice = 0;
        for (var i = 0; i < fromTests.Length; i++)
        {
            var fromTest = fromTests[i];
            if (IsSignal(fromTest))
                continue;
            if (!pairs.TryGetValue(i, out var pair))
            {
                scenarioList.Add(fromTest);
                if (KeptBesideItsNamesake(fromTest))
                    keptTwice++;
            }
            else if (holders[pair.Holder].IsScenario)
            {
                holders[pair.Holder].Items[pair.Index] = null;
                scenarioList.Add(pair.Merged);
            }
            else
            {
                holders[pair.Holder].Items[pair.Index] = pair.Merged;
            }
        }

        foreach (var (set, items, isScenario) in holders)
        {
            FileAttachment[] kept = isScenario ? [.. scenarioList, .. items.OfType<FileAttachment>()] : [.. items.OfType<FileAttachment>()];
            set(kept.Length > 0 ? kept : null);
        }

        // A decoded copy a reporter's file replaced goes once no entry links it: a plan that repeats a test step id puts one
        // copy in two lists, and the entry left would otherwise be a dead link.
        if (replaced.Count > 0)
        {
            var linked = AllAttachments(target).Select(a => a.RelativePath).ToHashSet(StringComparer.Ordinal);
            foreach (var written in replaced.Where(w => !linked.Contains(w)))
                TryDelete(written);
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

        void Take(int fromTest, (int Holder, int Index, FileAttachment Candidate) slot, FileAttachment merged)
        {
            taken.Add((slot.Holder, slot.Index));
            pairs[fromTest] = (slot.Holder, slot.Index, merged);
        }

        bool IsSignal(FileAttachment fromTest) => cucumber.SignalAttachmentNames.Contains(fromTest.Name);

        // The copies not yet taken that have the tests attachment's name, in reading order. A link is never a copy of a file.
        IEnumerable<(int Holder, int Index, FileAttachment Candidate)> Namesakes(FileAttachment fromTest)
        {
            for (var holder = 0; holder < holders.Count; holder++)
            {
                var items = holders[holder].Items;
                for (var index = 0; index < items.Count; index++)
                {
                    if (items[index] is { IsUrl: false } candidate && !taken.Contains((holder, index)) && SameName(fromTest, candidate))
                        yield return (holder, index, candidate);
                }
            }
        }

        static (int Holder, int Index, FileAttachment Candidate)? First(IEnumerable<(int Holder, int Index, FileAttachment Candidate)> slots)
        {
            foreach (var slot in slots)
                return slot;
            return null;
        }

        // A reporter that names a record by its file writes what the synthesis displays (Playwright's trace.zip), and
        // playwright-bdd passes Playwright's own name through as the attachment's fileName (trace).
        bool SameName(FileAttachment fromTest, FileAttachment candidate) =>
            string.Equals(cucumber.AttachmentOrigins.GetValueOrDefault(candidate)?.OriginalName ?? candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase);

        // A copy's bytes: the body the synthesis decoded, or the file the messages point at; null when that cannot be read.
        (long Length, byte[] Hash)? BytesOf(FileAttachment candidate) =>
            cucumber.AttachmentOrigins.GetValueOrDefault(candidate) is { Length: { } length, Sha256: { } hash } ? (length, hash) : Describe(candidate.RelativePath);

        // An unpaired tests attachment whose reporter's file was read, beside a copy no one took that has its name and bytes
        // that were read: two entries with one name, whose bytes differ (bytes that matched would have paired them).
        bool KeptBesideItsNamesake(FileAttachment fromTest) =>
            !fromTest.IsUrl && First(Namesakes(fromTest).Where(n => BytesOf(n.Candidate) is not null)) is not null
                            && Describe(fromTest.RelativePath) is not null;

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

    /// <summary>
    /// A scenario's attachments: its own, then each step's in tree order (the background steps, then the steps, each followed
    /// by its sub-steps).
    /// </summary>
    internal static IEnumerable<FileAttachment> AllAttachments(Scenario scenario)
    {
        return (scenario.Attachments ?? []).Concat(Steps(scenario.BackgroundSteps)).Concat(Steps(scenario.Steps));

        static IEnumerable<FileAttachment> Steps(ScenarioStep[]? steps) =>
            (steps ?? []).SelectMany(step => (step.Attachments ?? []).Concat(Steps(step.SubSteps)));
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
        // The scenario's own and each step's: the messages' step markers give the scenario its steps in the tests file's model
        // too, so a record's step index puts an attachment on one of them, and the scenario the messages build replaces them.
        FileAttachment[] attachments = [.. AllAttachments(fromTests)];
        var keptTwice = attachments.Length > 0 ? MergeAttachments(target, attachments, cucumber) : 0;

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

    /// <summary>
    /// Cucumber features first (in Gherkin order), then whatever the tests file had left over. A leftover under a Cucumber
    /// feature's name joins it with the tags both give the feature, and each side's others move onto its own scenarios, as
    /// a merge of two shards does (<see cref="Reports.Merge.MergeableReportMerger"/>): until 5.2.7 the Cucumber feature's
    /// tags reached the tests file's scenarios, and the tests file's feature tags were dropped.
    /// </summary>
    private static Feature[] MergeFeatures(Feature[] cucumber, List<Feature> leftovers)
    {
        var result = new List<Feature>(cucumber);
        var byName = result.ToDictionary(f => f.DisplayName, StringComparer.Ordinal);
        foreach (var feature in leftovers)
        {
            if (byName.TryGetValue(feature.DisplayName, out var existing))
            {
                var shared = ScenarioTags.Shared([existing.Tags, feature.Tags]);
                existing.Scenarios =
                [
                    .. Reports.Merge.MergeableReportMerger.WithFeatureTags(existing.Scenarios, existing.Tags, shared),
                    .. Reports.Merge.MergeableReportMerger.WithFeatureTags(feature.Scenarios, feature.Tags, shared)
                ];
                existing.Tags = shared.Length > 0 ? shared : null;
            }
            else
                result.Add(feature);
        }

        return result.ToArray();
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
