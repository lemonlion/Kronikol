using Kronikol.Reports;
using Kronikol.Tracking;
using TUnit.Core;

namespace Kronikol.TUnit;

internal static class TestContextEnumerableExtensions
{
    public static Feature[] ToFeatures(this IEnumerable<TestContext> contexts)
    {
        return contexts
            .Where(x => x.Metadata.TestDetails.ClassType is not null)
            .OrderBy(x => x.Metadata.TestDetails.ClassType.Name)
            .GroupBy(x => x.Metadata.TestDetails.ClassType.Name)
            .Select(scenariosForFeature =>
            {
                var featureClass = scenariosForFeature.First();
                var featureTags = TestTags.OfFeature(scenariosForFeature.Select(x => x.Metadata.TestDetails.ClassType));

                string? endpoint = null;
                if (featureClass.Metadata.TestDetails.CustomProperties.TryGetValue(EndpointAttribute.EndpointPropertyKey, out var endpointValue))
                    endpoint = endpointValue.FirstOrDefault();

                return new Feature
                {
                    DisplayName = ScenarioTitleResolver.FormatFeatureName(scenariosForFeature.Key),
                    NameGenerated = true,
                    Endpoint = endpoint,
                    Tags = featureTags.Length > 0 ? featureTags : null,
                    Scenarios = scenariosForFeature
                        .DistinctBy(x => x.Id)
                        .OrderByDescending(x => TestTags.OfScenario(x.Metadata.TestDetails.Categories, featureTags).IsHappyPath)
                        .ThenBy(x => x.Metadata.DisplayName)
                        .Select(x =>
                        {
                            var name = ScenarioTitleResolver.FormatLaneScenarioName(x.Metadata.DisplayName, x.Metadata.TestDetails.MethodName);

                            var structuredResult = TryExtractStructuredParametersWithRaw(x);
                            var structuredParams = structuredResult?.StringValues;
                            Dictionary<string, object?>? rawValues = structuredResult?.RawValues;
                            // a name the test wrote whole holds no arguments, whatever its brackets say
                            var parsed = structuredParams ?? (name.ReadArguments ? ParameterParser.Parse(name.Shown) : null);
                            var outlineId = parsed is { Count: > 0 } ? (structuredParams is not null ? GetStructuredOutlineId(x) : ParameterParser.ExtractBaseName(name.Shown)) : null;

                            // 5.16.4's reading of the same test, which the id keys on where this one differs (plans/TITLES_AS_WRITTEN_PLAN.md 4.4)
                            var frozenName = ScenarioTitleResolver.FormatScenarioDisplayNameAsOf5164(x.Metadata.DisplayName);
                            var frozenParsed = structuredParams ?? ParameterParser.Parse(frozenName);
                            var frozenKey = new ScenarioIdKey(frozenName,
                                frozenParsed is { Count: > 0 } ? (structuredParams is not null ? GetStructuredOutlineId(x) : ParameterParser.ExtractBaseName(frozenName)) : null,
                                frozenParsed is { Count: > 0 } ? frozenParsed : null);

                            var result = x.Execution.Result?.State.ToExecutionResult() ?? ExecutionResult.Skipped;
                            var own = TestTags.OfScenario(x.Metadata.TestDetails.Categories, featureTags);

                            return new Scenario
                            {
                                Id = x.Id,
                                Result = result,
                                DisplayName = name.Shown,
                                NameAsWritten = name.Written,
                                NameGenerated = !name.Written,
                                StableIdKey = ScenarioStableId.KeyWhereChanged(frozenKey, name.Shown, outlineId, parsed is { Count: > 0 } ? parsed : null),
                                IsHappyPath = own.IsHappyPath,
                                Tags = own.Tags.Length > 0 ? own.Tags : null,
                                ErrorMessage = ScenarioOutcome.Message(result, x.Execution.Result?.Exception),
                                ErrorStackTrace = ScenarioOutcome.StackTrace(result, x.Execution.Result?.Exception),
                                Duration = ScenarioOutcome.Duration(x.Execution.Result?.Duration, x.Execution.Result?.Start),
                                EndedAt = x.Execution.Result?.End,
                                OutlineId = outlineId,
                                OutlineTitle = outlineId is null ? null : ScenarioTitleResolver.FormatScenarioDisplayName(x.Metadata.TestDetails.MethodName),
                                OutlineTitleGenerated = outlineId is not null,
                                ExampleValues = parsed is { Count: > 0 } ? parsed : null,
                                ExampleRawValues = rawValues,
                                Steps = StepCollector.GetSteps(x.Id) is { Length: > 0 } steps ? steps : null,
                                Attachments = StepCollector.GetScenarioAttachments(x.Id)
                            };
                        }).ToArray()
                };
            }).ToArray();
    }

    private static (Dictionary<string, string> StringValues, Dictionary<string, object?> RawValues)? TryExtractStructuredParametersWithRaw(TestContext context)
    {
        try
        {
            var args = context.Metadata.TestDetails.TestMethodArguments;
            var parameterMetadata = context.Metadata.TestDetails.MethodMetadata?.Parameters;
            if (args is not { Length: > 0 } || parameterMetadata is not { Length: > 0 })
                return null;

            var paramNames = parameterMetadata.Select(p => p.Name).ToArray();
            return ParameterParser.ExtractStructuredParametersWithRaw(args, paramNames);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStructuredOutlineId(TestContext context)
    {
        return context.Metadata.TestDetails.MethodName;
    }
}
