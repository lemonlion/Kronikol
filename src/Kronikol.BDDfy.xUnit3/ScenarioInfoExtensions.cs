using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.BDDfy.xUnit3;

internal static class ScenarioInfoExtensions
{
    public static Feature[] ToFeatures(this IEnumerable<BDDfyScenarioInfo> scenarios)
    {
        return scenarios
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

                            return new Scenario
                            {
                                Id = x.TestId,
                                DisplayName = x.ScenarioTitle,
                                NameGenerated = x.TitleGenerated,
                                StableIdKey = ScenarioStableId.KeyWhereChanged(frozenKey, x.ScenarioTitle, outlineId,
                                    exampleValues is { Count: > 0 } ? exampleValues : null),
                                IsHappyPath = classified.IsHappyPath,
                                Result = x.Steps.Count == 0 && x.Result == TestStack.BDDfy.Result.NotExecuted
                                    ? ExecutionResult.Passed
                                    : x.Result.ToExecutionResult(),
                                ErrorMessage = x.ErrorMessage,
                                ErrorStackTrace = x.ErrorStackTrace,
                                Duration = x.Duration != TimeSpan.Zero ? x.Duration : null,
                                EndedAt = x.EndedAt,
                                Steps = x.Steps.Count > 0
                                    ? MapSteps(x.Steps, StepCollector.GetSteps(x.TestId))
                                    : StepCollector.GetSteps(x.TestId) is { Length: > 0 } collectedSteps ? collectedSteps : null,
                                Tags = classified.Tags.Length > 0 ? classified.Tags : null,
                                OutlineId = outlineId,
                                OutlineTitle = outlineId is null ? null : x.TestMethodName is { } method ? ScenarioTitleResolver.FormatScenarioDisplayName(method) : outlineId,
                                OutlineTitleGenerated = outlineId is not null && (x.TestMethodName is not null || x.TitleGenerated),
                                ExampleValues = exampleValues is { Count: > 0 } ? exampleValues : null,
                                ExampleRawValues = exampleRawValues,
                                Attachments = StepCollector.GetScenarioAttachments(x.TestId),
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

    private static ScenarioStep[] MapSteps(List<BDDfyStepInfo> steps, ScenarioStep[]? collectedSteps = null)
    {
        var mapped = new ScenarioStep[steps.Count];
        var priorFailure = false;
        for (var i = 0; i < steps.Count; i++)
        {
            mapped[i] = new ScenarioStep
            {
                Keyword = steps[i].Keyword,
                Text = steps[i].Text,
                Status = steps[i].Result.ToStepResult(priorFailure),
                Duration = steps[i].Duration,
                SubSteps = collectedSteps is not null && i < collectedSteps.Length
                    ? collectedSteps[i].SubSteps
                    : null,
                Attachments = collectedSteps is not null && i < collectedSteps.Length
                    ? collectedSteps[i].Attachments
                    : null,
            };
            if (steps[i].Result == TestStack.BDDfy.Result.Failed)
                priorFailure = true;
        }
        return mapped;
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
