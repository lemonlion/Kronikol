using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.BDDfy.xUnit3;

internal static class ScenarioInfoExtensions
{
    public static Feature[] ToFeatures(this IEnumerable<BDDfyScenarioInfo> scenarios)
    {
        var all = scenarios.ToArray();
        var ofEachTest = all.GroupBy(x => x.TestId).ToDictionary(test => test.Key, test => test.ToArray());

        return all
            .GroupBy(x => x.StoryTitle)
            .OrderBy(x => x.Key)
            .Select(featureGroup =>
            {
                var firstScenario = featureGroup.First();
                var endpoint = featureGroup.Select(s => ScenarioTags.Classify(s.Tags).Endpoint).FirstOrDefault(e => e is not null);

                var scenarios = DeduplicateScenarioTitles(featureGroup
                        .DistinctBy(x => x.TestId)
                        .OrderByDescending(x => ScenarioTags.Classify(x.Tags).IsHappyPath)
                        // by the title 5.16.4 sorted by, so the order, the sN addresses and the ` [n]` of two titles
                        // that read alike there stay as they were
                        .ThenBy(x => x.StableIdTitle ?? x.ScenarioTitle)
                        .Select(x =>
                        {
                            // a story has no tags, so they are all the scenario's; category:x is the tag x (F1)
                            var classified = ScenarioTags.Classify(x.Tags);

                            // Prefer structured extraction from raw arguments (same mechanism as the
                            // non-BDDfy adapters) for rich sub-table rendering of complex objects.
                            // Fall back to string-based parsing of the scenario title.
                            Dictionary<string, string>? exampleValues = null;
                            Dictionary<string, object?>? exampleRawValues = null;
                            string? outlineId = null;

                            var structured = ParameterParser.ExtractStructuredParametersWithRaw(
                                x.RawArguments, x.ParameterNames);

                            if (structured is not null)
                            {
                                exampleValues = structured.Value.StringValues;
                                exampleRawValues = structured.Value.RawValues;
                                outlineId = x.TestMethodName ?? ParameterParser.ExtractBaseName(x.ScenarioTitle);
                            }
                            else
                            {
                                var parsed = ParameterParser.Parse(x.ScenarioTitle);
                                if (parsed is { Count: > 0 })
                                {
                                    exampleValues = parsed;
                                    outlineId = x.TestMethodName ?? ParameterParser.ExtractBaseName(x.ScenarioTitle);
                                }
                            }

                            // 5.16.4 read the row's values from its own title the same way; only the name can differ
                            var frozenTitle = x.StableIdTitle ?? x.ScenarioTitle;
                            var frozenValues = structured is not null ? exampleValues : ParameterParser.Parse(frozenTitle);
                            var frozenKey = new ScenarioIdKey(frozenTitle,
                                frozenValues is { Count: > 0 } ? x.TestMethodName ?? ParameterParser.ExtractBaseName(frozenTitle) : null,
                                frozenValues is { Count: > 0 } ? frozenValues : null);

                            BDDfyStepTrackingExecutor.CloseLeftRunning(x.TestId);

                            var ofThisTest = ofEachTest[x.TestId];
                            var deciding = Deciding(ofThisTest);
                            var skipped = StepExtensions.SkipReasonIn(deciding.ErrorMessage) is not null;

                            return new Scenario
                            {
                                Id = x.TestId,
                                DisplayName = x.ScenarioTitle,
                                NameGenerated = x.TitleGenerated,
                                StableIdKey = ScenarioStableId.KeyWhereChanged(frozenKey, x.ScenarioTitle, outlineId,
                                    exampleValues is { Count: > 0 } ? exampleValues : null),
                                IsHappyPath = classified.IsHappyPath,
                                Result = skipped
                                    ? ExecutionResult.Skipped
                                    : deciding.Steps.Count == 0 && deciding.Result == TestStack.BDDfy.Result.NotExecuted
                                        ? ExecutionResult.Passed
                                        : deciding.Result.ToExecutionResult(),
                                ErrorMessage = skipped ? null : deciding.ErrorMessage,
                                ErrorStackTrace = skipped ? null : deciding.ErrorStackTrace,
                                Duration = DurationOf(ofThisTest),
                                EndedAt = ofThisTest.Max(row => row.EndedAt),
                                Steps = x.Steps.Count > 0
                                    ? MapSteps(x.Steps, StepCollector.GetSteps(x.TestId), x.TestId)
                                    : StepCollector.GetSteps(x.TestId) is { Length: > 0 } collectedSteps ? collectedSteps : null,
                                Tags = classified.Tags.Length > 0 ? classified.Tags : null,
                                OutlineId = outlineId,
                                OutlineTitle = outlineId is null ? null : x.TestMethodName is { } method ? ScenarioTitleResolver.FormatScenarioDisplayName(method) : outlineId,
                                OutlineTitleGenerated = outlineId is not null && (x.TestMethodName is not null || x.TitleGenerated),
                                ExampleValues = exampleValues is { Count: > 0 } ? exampleValues : null,
                                ExampleRawValues = exampleRawValues,
                                Attachments = ScenarioAttachments(x),
                            };
                        }).ToArray());

                return new Feature
                {
                    DisplayName = featureGroup.Key.Titleize(),
                    NameGenerated = featureGroup.All(x => x.StoryTitleGenerated),
                    // the ids key on 5.9.0's name, which Titleize can now differ from (plans/REPORT_UI_FIXES_PLAN.md N3)
                    StableIdName = StringCasing.TitleizeAsOf590(featureGroup.Key),
                    Endpoint = endpoint,
                    Description = firstScenario.StoryDescription,
                    Scenarios = scenarios
                };
            }).ToArray();
    }

    /// <summary>
    /// What decides a test's result when it ran more than one story or row. A BDDfy call throws at its end if a row failed, so a later call ran only if the
    /// ones before it passed or something caught their failure: the last call decides, and in it any row that failed.
    /// </summary>
    private static BDDfyScenarioInfo Deciding(BDDfyScenarioInfo[] ofOneTest)
    {
        var lastCall = ofOneTest.Max(row => row.Call);
        return ofOneTest.Where(row => row.Call == lastCall).MaxBy(row => row.Result)!;
    }

    // BDDfy's stopwatch stops when a step's task comes back, at its first await, so a test takes as long as its steps did when they say longer (#122).
    private static TimeSpan? DurationOf(BDDfyScenarioInfo[] ofOneTest)
    {
        var bdDfys = TimeSpan.FromTicks(ofOneTest.Sum(row => row.Duration.Ticks));
        var steps = TimeSpan.FromTicks(StepCollector.GetSteps(ofOneTest[0].TestId).Sum(step => step.Duration?.Ticks ?? 0));
        var longest = steps > bdDfys ? steps : bdDfys;
        return longest != TimeSpan.Zero ? longest : null;
    }

    private static TimeSpan? Longest(TimeSpan? bdDfys, TimeSpan? collected) =>
        collected is { } steps && (bdDfys is not { } own || steps > own) ? steps : bdDfys;

    // The scenario's own files, and those of a step the report does not list (a tear down, a later row of an example table).
    private static FileAttachment[]? ScenarioAttachments(BDDfyScenarioInfo info)
    {
        var own = StepCollector.GetScenarioAttachments(info.TestId);
        if (info.Steps.Count == 0)
            return own;

        // By position, a frame past the last row has no row to read it.
        var collected = StepCollector.GetSteps(info.TestId);
        var notListed = (PairsByPosition(info.Steps, info.TestId)
            ? collected.Skip(info.Steps.Count).SelectMany(AllFilesOf)
            : BDDfyStepTrackingExecutor.FilesNotListed(collected, info.TestId, info.Steps.Select(s => s.StepId).ToArray())).ToArray();
        return notListed.Length > 0 ? [.. own ?? [], .. notListed] : own;
    }

    private static IEnumerable<FileAttachment> AllFilesOf(ScenarioStep step) =>
        (step.Attachments ?? []).Concat((step.SubSteps ?? []).SelectMany(AllFilesOf));

    // Steps built or rebuilt by hand have no step id, and steps no executor ran no frames: a list holding one pairs by position.
    private static bool PairsByPosition(List<BDDfyStepInfo> steps, string testId) =>
        steps.Any(s => s.StepId is null) || steps.All(s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId) is null);

    private static ScenarioStep[] MapSteps(List<BDDfyStepInfo> steps, ScenarioStep[] collectedSteps, string testId)
    {
        var priorFailure = false;
        if (PairsByPosition(steps, testId))
        {
            return steps.Select((step, i) =>
            {
                var collected = i < collectedSteps.Length ? collectedSteps[i] : null;
                var mapped = new ScenarioStep
                {
                    Keyword = step.Keyword,
                    Text = step.Text,
                    Status = step.Result.ToStepResult(priorFailure),
                    Duration = step.Duration,
                    SubSteps = collected?.SubSteps,
                    Attachments = collected?.Attachments,
                };
                if (step.Result == TestStack.BDDfy.Result.Failed)
                    priorFailure = true;
                return mapped;
            }).ToArray();
        }

        return StepPairing.InOrderTheyRan(steps, collectedSteps, s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId), (step, collected) =>
        {
            var mapped = new ScenarioStep
            {
                CollectedId = collected?.CollectedId,
                DidNotRun = BDDfyStepTrackingExecutor.FrameOf(testId, step.StepId) is null,
                Keyword = step.Keyword,
                Text = step.Text,
                Status = step.SkipReason is not null ? ExecutionResult.Skipped : step.Result.ToStepResult(priorFailure),
                BypassReason = step.SkipReason,
                Duration = Longest(step.Duration, collected?.Duration),
                SubSteps = collected?.SubSteps,
                Attachments = collected?.Attachments,
            };
            if (step.Result == TestStack.BDDfy.Result.Failed && step.SkipReason is null)
                priorFailure = true;
            return mapped;
        }).ToArray();
    }

    /// <summary>
    /// Numbers the titles a story repeats, ` [1]`, ` [2]`, in the story's order. The suffix is in the id, so the ids
    /// number the titles 5.16.4 showed alike (each scenario's recorded key), as it did; a title shown takes its number
    /// only where the titles shown still repeat, so two that read apart now need none, and two the rule made alike show
    /// alike, their ids apart (plans/TITLES_AS_WRITTEN_PLAN.md 4.4).
    /// </summary>
    private static Scenario[] DeduplicateScenarioTitles(Scenario[] scenarios)
    {
        var repeatedKeys = scenarios.GroupBy(ScenarioStableId.KeyName).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        if (repeatedKeys.Count == 0) return scenarios;
        var repeatedTitles = scenarios.GroupBy(s => s.DisplayName).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        var titleIndex = new Dictionary<string, int>();
        foreach (var scenario in scenarios)
        {
            var key = scenario.StableIdKey ?? new ScenarioIdKey(scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues);
            if (!repeatedKeys.Contains(key.Name)) continue;

            if (!titleIndex.TryGetValue(key.Name, out var index))
                index = 0;
            titleIndex[key.Name] = index + 1;

            if (repeatedTitles.Contains(scenario.DisplayName))
                scenario.DisplayName = $"{scenario.DisplayName} [{index + 1}]";
            scenario.StableIdKey = ScenarioStableId.KeyWhereChanged(key with { Name = $"{key.Name} [{index + 1}]" },
                scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues);
        }

        return scenarios;
    }
}
