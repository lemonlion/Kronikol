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
/// therefore dropped rather than duplicated.</item>
/// <item>The tests file still contributes what only it knows: <c>assertion</c> events (nested under the
/// Gherkin step whose time window contains them — the ✓/✗ rows), attachments the reporter recorded on the
/// scenario or its steps, and the failure text when Gherkin carried none.</item>
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
    /// Merges a Cucumber synthesis into the model <see cref="FeatureSynthesizer.Build"/> produced from the
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
        foreach (var feature in fromTestsFile.Features)
        {
            foreach (var scenario in feature.Scenarios)
            {
                if (replaced.TryGetValue(scenario.Id, out var target))
                    CarryOver(scenario, target);
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
        return new FeatureSynthesizer.Result(features, start, end, names);
    }

    /// <summary>
    /// True for a tests-file <c>step</c> record that the messages replace: the reporter's own step events
    /// for a BDD scenario would otherwise draw a second set of delimiter bars next to the Gherkin ones.
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
    /// The reporter's attachments win over the messages' copies of the SAME artefact (playwright-bdd inlines
    /// every attachment as BASE64 into the messages file, so a screenshot the reporter already materialised
    /// would otherwise appear twice); anything only the messages carry is kept.
    /// </summary>
    internal static FileAttachment[] MergeAttachments(FileAttachment[]? fromMessages, FileAttachment[] fromTests)
    {
        if (fromMessages is not { Length: > 0 })
            return fromTests;
        var names = new HashSet<string>(fromTests.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        return fromTests.Concat(fromMessages.Where(a => !names.Contains(a.Name))).ToArray();
    }

    /// <summary>
    /// Keeps the scenario-level facts only the reporter has: attachments it recorded, and the failure text
    /// when the messages carried none (a Playwright timeout fails the test without failing a Gherkin step).
    /// </summary>
    private static void CarryOver(Scenario fromTests, Scenario target)
    {
        if (fromTests.Attachments is { Length: > 0 } attachments)
            target.Attachments = MergeAttachments(target.Attachments, attachments);

        target.ErrorMessage ??= fromTests.ErrorMessage;
        target.ErrorStackTrace ??= fromTests.ErrorStackTrace;
        if (target.Duration is null)
            target.Duration = fromTests.Duration;
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
            Text = $"{(passed ? Track.PassSymbol : Track.FailSymbol)} {record.Text ?? "assertion"}",
            Status = passed ? ExecutionResult.Passed : ExecutionResult.Failed,
            Comments = passed || string.IsNullOrWhiteSpace(record.Error) ? null : [record.Error!],
            Duration = record.DurationMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
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
