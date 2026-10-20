using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.MSTest;

internal static class TestContextEnumerableExtensions
{
    public static Feature[] ToFeatures(this IEnumerable<MSTestScenarioInfo> scenarioInfos)
    {
        return scenarioInfos
            .OrderBy(x => x.TestClassSimpleName)
            .GroupBy(x => x.TestClassSimpleName)
            .Select(scenariosForFeature =>
            {
                var firstScenario = scenariosForFeature.First();
                var featureTags = ScenarioTags.Classify(firstScenario.FeatureTags).Tags;

                return new Feature
                {
                    DisplayName = firstScenario.TestClassSimpleName.Titleize(),
                    Endpoint = firstScenario.Endpoint,
                    Tags = featureTags.Length > 0 ? featureTags : null,
                    Scenarios = scenariosForFeature
                        .DistinctBy(x => x.TestId)
                        .OrderByDescending(x => x.IsHappyPath || ScenarioTags.Classify(x.Tags).IsHappyPath)
                        .ThenBy(x => x.TestMethodName)
                        .Select(x =>
                        {
                            var displayName = ScenarioTitleResolver.FormatScenarioDisplayName(x.TestDisplayName ?? x.TestMethodName);
                            var parsed = ParameterParser.Parse(displayName);
                            parsed = RebindParameterNames(parsed, x.ParameterNames);
                            var classified = ScenarioTags.Classify(x.Tags);
                            var tags = classified.Tags.Except(featureTags, ScenarioTags.Comparer).ToArray();
                            return new Scenario
                            {
                                Id = x.TestId,
                                Result = x.Outcome.ToExecutionResult(),
                                DisplayName = displayName,
                                IsHappyPath = x.IsHappyPath || classified.IsHappyPath,
                                Tags = tags.Length > 0 ? tags : null,
                                ErrorMessage = x.ErrorMessage,
                                ErrorStackTrace = x.ErrorStackTrace,
                                FailureCause = x.FailureCause,
                                Duration = x.Duration,
                                EndedAt = x.EndedAt,
                                Steps = StepCollector.GetSteps(x.TestId) is { Length: > 0 } steps ? steps : null,
                                Attachments = StepCollector.GetScenarioAttachments(x.TestId),
                                OutlineId = parsed is { Count: > 0 } ? ParameterParser.ExtractBaseName(displayName) : null,
                                ExampleValues = parsed is { Count: > 0 } ? parsed : null
                            };
                        }).ToArray()
                };
            }).ToArray();
    }

    internal static Dictionary<string, string>? RebindParameterNames(
        Dictionary<string, string>? parsed, string?[]? parameterNames)
    {
        if (parsed is not { Count: > 0 } || parameterNames is not { Length: > 0 })
            return parsed;

        if (parsed.Count != parameterNames.Length)
            return parsed;

        // Only rebind if all keys are positional (arg0, arg1, ...)
        var allPositional = parsed.Keys.All(k => k.StartsWith("arg") && int.TryParse(k.AsSpan(3), out _));
        if (!allPositional)
            return parsed;

        var result = new Dictionary<string, string>();
        var i = 0;
        foreach (var value in parsed.Values)
        {
            result[parameterNames[i] ?? $"param{i}"] = value;
            i++;
        }

        return result;
    }
}
