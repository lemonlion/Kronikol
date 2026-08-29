using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Ingestion;

/// <summary>
/// Builds the <see cref="Feature"/>[] model the report generator needs from tests-file records
/// (<see cref="TestRunRecord"/>) and/or the ingested interaction logs. A scenario is created for every
/// distinct test id seen in either source; outcome, duration, structure (steps, background, doc-strings,
/// tables), classification (tags, rule, outline and example values) and artefacts (attachments) come
/// from the tests file when present.
/// </summary>
public static class FeatureSynthesizer
{
    /// <summary>Result of <see cref="Build"/>.</summary>
    /// <param name="Features">Features grouped by <see cref="TestRunRecord.Feature"/> (or the default feature name).</param>
    /// <param name="Start">Earliest timestamp observed (UTC) — the run start.</param>
    /// <param name="End">Latest timestamp observed (UTC) — the run end.</param>
    /// <param name="TestNames">Display name per test id, for normalising log <c>TestName</c>s.</param>
    public sealed record Result(Feature[] Features, DateTime Start, DateTime End, IReadOnlyDictionary<string, string> TestNames);

    /// <summary>
    /// Synthesises features. <paramref name="defaultFeatureName"/> groups scenarios that carry no feature.
    /// <paramref name="resultWhenUnknown"/> is the verdict for tests that have interactions but no <c>end</c> record.
    /// <paramref name="attachmentsBase"/> resolves relative <c>attachment</c> paths (default: the current directory).
    /// </summary>
    public static Result Build(
        IEnumerable<TestRunRecord>? testRecords,
        IEnumerable<RequestResponseLog>? logs,
        string defaultFeatureName = "Ingested",
        ExecutionResult resultWhenUnknown = ExecutionResult.Passed,
        string? attachmentsBase = null)
    {
        var records = (testRecords ?? []).ToList();
        var logList = (logs ?? []).ToList();

        var byTest = new Dictionary<string, TestAccumulator>(StringComparer.Ordinal);
        var order = new List<string>();

        TestAccumulator Get(string testId)
        {
            if (!byTest.TryGetValue(testId, out var acc))
            {
                acc = new TestAccumulator(testId);
                byTest[testId] = acc;
                order.Add(testId);
            }

            return acc;
        }

        foreach (var record in records.OrderBy(r => r.Timestamp ?? DateTimeOffset.MinValue))
        {
            if (string.IsNullOrWhiteSpace(record.TestId))
                continue;
            // Only the events this synthesiser understands create a scenario: a producer may add
            // run-level or future events (a Playwright reporter's `testrun`), and an unknown event
            // must not become a phantom scenario (one that, never ending, renders as Failed and
            // blanks the living documentation of an otherwise green run).
            if (!TestRunRecord.IsKnownEvent(record.Event))
                continue;
            var acc = Get(record.TestId);
            if (!string.IsNullOrWhiteSpace(record.TestName)) acc.Name = record.TestName;
            if (!string.IsNullOrWhiteSpace(record.Feature)) acc.Feature = record.Feature;

            switch (record.Event?.ToLowerInvariant())
            {
                case TestRunRecord.Events.Start:
                    acc.Start ??= record.Timestamp;
                    acc.Description ??= record.Description;
                    acc.FeatureDescription ??= record.FeatureDescription;
                    acc.Rule ??= record.Rule;
                    acc.OutlineId ??= record.OutlineId;
                    acc.ExamplesBlockName ??= record.ExamplesBlockName;
                    acc.ExamplesBlockDescription ??= record.ExamplesBlockDescription;
                    acc.ExamplesBlockIndex ??= record.ExamplesBlockIndex;
                    if (record.Tags is { Length: > 0 })
                        acc.Tags.AddRange(record.Tags);
                    if (record.ExampleValues is { Count: > 0 })
                        acc.ExampleValues ??= new Dictionary<string, string>(record.ExampleValues, StringComparer.Ordinal);
                    break;
                case TestRunRecord.Events.Step:
                    if (record.Background == true)
                        acc.BackgroundSteps.Add(record);
                    else
                        acc.Steps.Add(record);
                    break;
                case TestRunRecord.Events.Assertion:
                    acc.Steps.Add(record);
                    break;
                case TestRunRecord.Events.Attachment:
                    acc.Attachments.Add(record);
                    break;
                case TestRunRecord.Events.End:
                    acc.End = record.Timestamp ?? acc.End;
                    acc.Status = record.Status ?? acc.Status;
                    acc.DurationMs = record.DurationMs ?? acc.DurationMs;
                    acc.Error = record.Error ?? acc.Error;
                    acc.StackTrace = record.StackTrace ?? acc.StackTrace;
                    acc.HasEnd = true;
                    break;
            }
        }

        foreach (var log in logList)
        {
            if (string.IsNullOrWhiteSpace(log.TestId))
                continue;
            var acc = Get(log.TestId);
            acc.Name ??= string.IsNullOrWhiteSpace(log.TestName) || log.TestName == TestIdentityScope.UnknownTestName ? null : log.TestName;
            acc.HasLogs = true;
            if (log.Timestamp is { } ts)
            {
                acc.FirstLog = acc.FirstLog is null || ts < acc.FirstLog ? ts : acc.FirstLog;
                acc.LastLog = acc.LastLog is null || ts > acc.LastLog ? ts : acc.LastLog;
            }
        }

        var featureGroups = new Dictionary<string, List<Scenario>>(StringComparer.Ordinal);
        var featureDescriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var featureEndpoints = new Dictionary<string, string>(StringComparer.Ordinal);
        var featureTags = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        var featureOrder = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        DateTimeOffset? runStart = null, runEnd = null;

        foreach (var testId in order)
        {
            var acc = byTest[testId];
            var name = acc.Name ?? testId;
            names[testId] = name;

            var tags = ScenarioTags.Classify(acc.Tags);
            var steps = BuildStepTree(acc.Steps);
            var backgroundSteps = BuildStepTree(acc.BackgroundSteps);

            var scenario = new Scenario
            {
                Id = testId,
                DisplayName = name,
                Description = acc.Description,
                Result = acc.HasEnd ? MapStatus(acc.Status) : resultWhenUnknown,
                ErrorMessage = acc.Error,
                ErrorStackTrace = acc.StackTrace,
                Duration = acc.DurationMs is { } ms ? TimeSpan.FromMilliseconds(ms)
                    : acc.Start is { } s && acc.End is { } e && e >= s ? e - s
                    : acc.FirstLog is { } f && acc.LastLog is { } l && l >= f ? l - f
                    : null,
                Steps = steps,
                BackgroundSteps = backgroundSteps,
                Rule = acc.Rule,
                OutlineId = acc.OutlineId,
                ExamplesBlockName = acc.ExamplesBlockName,
                ExamplesBlockDescription = acc.ExamplesBlockDescription,
                ExamplesBlockIndex = acc.ExamplesBlockIndex,
                ExampleValues = acc.ExampleValues,
                ExampleFlatValues = acc.ExampleValues is null ? null : new Dictionary<string, string>(acc.ExampleValues, StringComparer.Ordinal),
                ExampleRawValues = acc.ExampleValues?.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value, StringComparer.Ordinal),
                IsHappyPath = tags.IsHappyPath,
                Labels = tags.Labels.Length > 0 ? tags.Labels : null,
                Categories = tags.Categories.Length > 0 ? tags.Categories : null,
            };

            ApplyAttachments(scenario, acc.Attachments, attachmentsBase);

            var feature = acc.Feature ?? defaultFeatureName;
            if (!featureGroups.TryGetValue(feature, out var list))
            {
                list = [];
                featureGroups[feature] = list;
                featureTags[feature] = [];
                featureOrder.Add(feature);
            }

            list.Add(scenario);
            featureTags[feature].Add(tags.Labels);
            if (acc.FeatureDescription is { Length: > 0 } description)
                featureDescriptions.TryAdd(feature, description);
            if (tags.Endpoint is { Length: > 0 } endpoint)
                featureEndpoints.TryAdd(feature, endpoint);

            foreach (var candidate in new[] { acc.Start, acc.End, acc.FirstLog, acc.LastLog })
            {
                if (candidate is not { } c) continue;
                runStart = runStart is null || c < runStart ? c : runStart;
                runEnd = runEnd is null || c > runEnd ? c : runEnd;
            }
        }

        var features = featureOrder
            .Select(f =>
            {
                var scenarios = featureGroups[f].ToArray();
                DetectBackgroundSteps(scenarios);
                return new Feature
                {
                    DisplayName = f,
                    Scenarios = scenarios,
                    Description = featureDescriptions.GetValueOrDefault(f),
                    Endpoint = featureEndpoints.GetValueOrDefault(f),
                    Labels = SharedLabels(featureTags[f]),
                };
            })
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        return new Result(
            features,
            (runStart ?? now).UtcDateTime,
            (runEnd ?? runStart ?? now).UtcDateTime,
            names);
    }

    /// <summary>
    /// Labels carried by every scenario of a feature — in Gherkin those are exactly the feature's own
    /// tags, since a scenario inherits them. Null when there are none.
    /// </summary>
    private static string[]? SharedLabels(List<string[]> perScenarioLabels)
    {
        if (perScenarioLabels.Count == 0 || perScenarioLabels.Any(l => l.Length == 0))
            return null;

        var shared = perScenarioLabels
            .Skip(1)
            .Aggregate(
                new HashSet<string>(perScenarioLabels[0], StringComparer.OrdinalIgnoreCase),
                (acc, labels) => { acc.IntersectWith(labels); return acc; });

        return shared.Count == 0
            ? null
            : perScenarioLabels[0].Where(shared.Contains).ToArray();
    }

    /// <summary>
    /// Runs the <see cref="BackgroundStepsDetector"/> heuristic, but only when it has something to add:
    /// no scenario supplied an explicit background (<c>background: true</c> step records), and the steps
    /// carry Gherkin keywords — a common prefix of keyword-less UI actions is a coincidence, not a Background.
    /// </summary>
    private static void DetectBackgroundSteps(Scenario[] scenarios)
    {
        if (scenarios.Any(s => s.BackgroundSteps is { Length: > 0 }))
            return;
        if (!scenarios.Any(s => (s.Steps ?? []).Any(step => !string.IsNullOrWhiteSpace(step.Keyword))))
            return;

        BackgroundStepsDetector.DetectAndExtract(scenarios);
    }

    /// <summary>
    /// Attaches <c>attachment</c> records to the scenario or, when they name a <c>step</c> index, to that
    /// 0-based top-level step. An index that no longer resolves (fewer steps than the producer thought)
    /// falls back to the scenario, so an artefact is never silently lost.
    /// </summary>
    private static void ApplyAttachments(Scenario scenario, List<TestRunRecord> attachments, string? attachmentsBase)
    {
        if (attachments.Count == 0)
            return;

        var scenarioLevel = new List<FileAttachment>();
        var perStep = new Dictionary<int, List<FileAttachment>>();

        foreach (var record in attachments)
        {
            var path = ResolveAttachmentPath(record.Path, attachmentsBase);
            if (path is null)
                continue;

            var attachment = new FileAttachment(AttachmentName(record, path), path, record.MediaType);

            var index = record.Step;
            if (index is { } i && i >= 0 && scenario.Steps is { Length: > 0 } steps && i < steps.Length)
            {
                if (!perStep.TryGetValue(i, out var list))
                    perStep[i] = list = [];
                list.Add(attachment);
            }
            else
            {
                scenarioLevel.Add(attachment);
            }
        }

        if (scenarioLevel.Count > 0)
            scenario.Attachments = [.. scenario.Attachments ?? [], .. scenarioLevel];

        foreach (var (index, list) in perStep)
        {
            var step = scenario.Steps![index];
            step.Attachments = [.. step.Attachments ?? [], .. list];
        }
    }

    /// <summary>The display name of an attachment: the record's <c>name</c>, else the file name, else the raw path.</summary>
    private static string AttachmentName(TestRunRecord record, string path)
    {
        if (!string.IsNullOrWhiteSpace(record.Name))
            return record.Name!;
        var fileName = FileAttachment.IsUrlPath(path) ? null : System.IO.Path.GetFileName(path);
        return string.IsNullOrEmpty(fileName) ? path : fileName;
    }

    /// <summary>
    /// Resolves an attachment path: URLs pass through untouched, absolute paths are kept, relative paths
    /// are resolved against <paramref name="attachmentsBase"/> (default: the current directory).
    /// </summary>
    private static string? ResolveAttachmentPath(string? path, string? attachmentsBase)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (FileAttachment.IsUrlPath(path) || System.IO.Path.IsPathRooted(path))
            return path;
        return System.IO.Path.GetFullPath(path, attachmentsBase ?? Directory.GetCurrentDirectory());
    }

    /// <summary>
    /// Builds the step list from <c>step</c> and <c>assertion</c> records (in timestamp order): top-level
    /// steps (<c>level</c> 0/absent) are the rows; nested steps (<c>level</c> &gt; 0) and assertions become
    /// sub-steps of the most recent top-level step — an assertion before any step is a top-level row.
    /// Assertions honour <see cref="StepTrackingOptions.IncludeTrackedAssertionsInStepList"/>, like
    /// Kronikol's own assertion tracking.
    /// </summary>
    public static ScenarioStep[]? BuildStepTree(IEnumerable<TestRunRecord> records)
    {
        var includeAssertions = StepCollector.Options.IncludeTrackedAssertionsInStepList;
        var roots = new List<ScenarioStep>();
        var children = new Dictionary<ScenarioStep, List<ScenarioStep>>();
        ScenarioStep? currentTop = null;

        foreach (var record in records.OrderBy(r => r.Timestamp ?? DateTimeOffset.MinValue))
        {
            var isAssertion = string.Equals(record.Event, TestRunRecord.Events.Assertion, StringComparison.OrdinalIgnoreCase);
            if (isAssertion && !includeAssertions)
                continue;

            var step = isAssertion ? BuildAssertionStep(record) : BuildStep(record);

            var nested = isAssertion || (record.Level ?? 0) > 0;
            if (nested && currentTop is not null)
            {
                if (!children.TryGetValue(currentTop, out var list))
                    children[currentTop] = list = [];
                list.Add(step);
            }
            else
            {
                roots.Add(step);
                if (!isAssertion)
                    currentTop = step;
            }
        }

        if (roots.Count == 0)
            return null;

        foreach (var (parent, list) in children)
            parent.SubSteps = list.ToArray();
        return roots.ToArray();
    }

    private static ScenarioStep BuildAssertionStep(TestRunRecord record)
    {
        var passed = !string.Equals(record.Status, "failed", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(record.Status, "fail", StringComparison.OrdinalIgnoreCase);
        return new ScenarioStep
        {
            Text = $"{(passed ? Track.PassSymbol : Track.FailSymbol)} {record.Text ?? "assertion"}",
            Status = passed ? ExecutionResult.Passed : ExecutionResult.Failed,
            Comments = BuildComments(passed ? null : record.Error, record.StackTrace),
            Duration = record.DurationMs is { } ad ? TimeSpan.FromMilliseconds(ad) : null,
        };
    }

    private static ScenarioStep BuildStep(TestRunRecord record)
    {
        var text = record.Text ?? "(step)";
        var parameters = BuildTableParameter(record.Table);
        return new ScenarioStep
        {
            Text = text,
            Keyword = record.Keyword,
            Status = record.Status is null ? null : MapStatus(record.Status),
            Duration = record.DurationMs is { } d ? TimeSpan.FromMilliseconds(d) : null,
            Comments = BuildComments(record.Error, record.StackTrace),
            BypassReason = record.BypassReason,
            DocString = record.DocString,
            DocStringMediaType = record.DocStringMediaType,
            Parameters = parameters,
            // A data table renders below the step; the reference segment gives the reader a toggle for it
            // in the step line itself, exactly like the in-process tabular-parameter rendering.
            TextSegments = parameters is null
                ? null
                : [StepTextSegment.Literal(text + " "), StepTextSegment.TableRef(TableParameterName)],
        };
    }

    private static string[]? BuildComments(string? error, string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(error) && string.IsNullOrWhiteSpace(stackTrace))
            return null;
        var comments = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(error)) comments.Add(error!);
        if (!string.IsNullOrWhiteSpace(stackTrace)) comments.Add(stackTrace!);
        return comments.ToArray();
    }

    /// <summary>The parameter name a Gherkin data table is filed under — the same one the ReqNRoll adapter uses.</summary>
    internal const string TableParameterName = "table";

    /// <summary>Maps a <c>table</c> (first row = header) to the step's tabular parameter.</summary>
    internal static StepParameter[]? BuildTableParameter(string[][]? table)
    {
        if (table is null || table.Length < 2)
            return null;

        var header = table[0];
        if (header.Length == 0)
            return null;

        var columns = header.Select(h => new TabularColumn(h ?? string.Empty, false)).ToArray();
        var rows = new List<TabularRow>(table.Length - 1);
        for (var i = 1; i < table.Length; i++)
        {
            var source = table[i] ?? [];
            var cells = new TabularCell[columns.Length];
            for (var c = 0; c < columns.Length; c++)
                cells[c] = new TabularCell(c < source.Length ? source[c] ?? string.Empty : string.Empty, null, VerificationStatus.NotApplicable);
            rows.Add(new TabularRow(TableRowType.Matching, cells));
        }

        return rows.Count == 0
            ? null
            : [new StepParameter
            {
                Name = TableParameterName,
                Kind = StepParameterKind.Tabular,
                TabularValue = new TabularParameterValue(columns, rows.ToArray()),
            }];
    }

    /// <summary>Maps a test-runner status word to <see cref="ExecutionResult"/> (Playwright, Jest, JUnit and xUnit vocabularies).</summary>
    public static ExecutionResult MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "passed" or "pass" or "ok" or "success" or "succeeded" => ExecutionResult.Passed,
        "failed" or "fail" or "error" or "timedout" or "timed-out" or "timeout" or "interrupted" or "broken" => ExecutionResult.Failed,
        "skipped" or "skip" or "pending" or "todo" or "ignored" => ExecutionResult.Skipped,
        "bypassed" => ExecutionResult.Bypassed,
        "skippedafterfailure" => ExecutionResult.SkippedAfterFailure,
        _ => ExecutionResult.Failed,
    };

    private sealed class TestAccumulator(string testId)
    {
        public string TestId { get; } = testId;
        public string? Name { get; set; }
        public string? Feature { get; set; }
        public string? FeatureDescription { get; set; }
        public string? Description { get; set; }
        public string? Rule { get; set; }
        public string? OutlineId { get; set; }
        public string? ExamplesBlockName { get; set; }
        public string? ExamplesBlockDescription { get; set; }
        public int? ExamplesBlockIndex { get; set; }
        public List<string> Tags { get; } = [];
        public Dictionary<string, string>? ExampleValues { get; set; }
        public DateTimeOffset? Start { get; set; }
        public DateTimeOffset? End { get; set; }
        public string? Status { get; set; }
        public double? DurationMs { get; set; }
        public string? Error { get; set; }
        public string? StackTrace { get; set; }
        public bool HasEnd { get; set; }
        public bool HasLogs { get; set; }
        public DateTimeOffset? FirstLog { get; set; }
        public DateTimeOffset? LastLog { get; set; }
        public List<TestRunRecord> Steps { get; } = [];
        public List<TestRunRecord> BackgroundSteps { get; } = [];
        public List<TestRunRecord> Attachments { get; } = [];
    }
}
