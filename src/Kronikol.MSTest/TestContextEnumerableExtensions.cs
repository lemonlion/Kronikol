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
                // The feature's are the categories all its tests have at its level, not the first test's (5.2.7): two
                // classes of one name are one feature. A test keeps the rest of its class's on its own.
                var featureTags = ScenarioTags.Shared(scenariosForFeature.Select(x => ScenarioTags.Classify(x.FeatureTags).Tags));

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
                            var name = ScenarioTitleResolver.FormatLaneScenarioName(x.TestDisplayName ?? x.TestMethodName, x.TestMethodName,
                                x.MethodDisplayName);
                            // a name the test wrote whole holds no arguments, whatever its brackets say
                            var parsed = name.ReadArguments ? RebindParameterNames(ParameterParser.Parse(name.Shown), x.ParameterNames) : null;
                            var outlineId = parsed is { Count: > 0 } ? ParameterParser.ExtractBaseName(name.Shown) : null;

                            // 5.16.4's reading of the same test, which the id keys on where this one differs (plans/TITLES_AS_WRITTEN_PLAN.md 4.4)
                            var frozenName = ScenarioTitleResolver.FormatScenarioDisplayNameAsOf5164(x.TestDisplayName ?? x.TestMethodName);
                            var frozenParsed = RebindParameterNames(ParameterParser.Parse(frozenName), x.ParameterNames);
                            var frozenKey = new ScenarioIdKey(frozenName,
                                frozenParsed is { Count: > 0 } ? ParameterParser.ExtractBaseName(frozenName) : null,
                                frozenParsed is { Count: > 0 } ? frozenParsed : null);
                            var classified = ScenarioTags.Classify(x.Tags);
                            var tags = ScenarioTags.Merge(ScenarioTags.Classify(x.FeatureTags).Tags, classified.Tags)
                                .Except(featureTags, ScenarioTags.Comparer).ToArray();
                            return new Scenario
                            {
                                Id = x.TestId,
                                Result = x.Outcome.ToExecutionResult(),
                                DisplayName = name.Shown,
                                NameAsWritten = name.Written,
                                StableIdKey = ScenarioStableId.KeyWhereChanged(frozenKey, name.Shown, outlineId, parsed is { Count: > 0 } ? parsed : null),
                                IsHappyPath = x.IsHappyPath || classified.IsHappyPath,
                                Tags = tags.Length > 0 ? tags : null,
                                ErrorMessage = x.ErrorMessage,
                                ErrorStackTrace = x.ErrorStackTrace,
                                FailureCause = x.FailureCause,
                                Duration = x.Duration,
                                EndedAt = x.EndedAt,
                                Steps = StepCollector.GetSteps(x.TestId) is { Length: > 0 } steps ? steps : null,
                                Attachments = StepCollector.GetScenarioAttachments(x.TestId),
                                OutlineId = outlineId,
                                OutlineTitle = outlineId is null ? null : x.MethodDisplayName ?? ScenarioTitleResolver.FormatScenarioDisplayName(x.TestMethodName),
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
