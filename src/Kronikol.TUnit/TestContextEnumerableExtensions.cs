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
                var categories = featureClass.Metadata.TestDetails.Categories;

                string? endpoint = null;
                if (featureClass.Metadata.TestDetails.CustomProperties.TryGetValue(EndpointAttribute.EndpointPropertyKey, out var endpointValue))
                    endpoint = endpointValue.FirstOrDefault();

                return new Feature
                {
                    DisplayName = ScenarioTitleResolver.FormatFeatureName(scenariosForFeature.Key),
                    Endpoint = endpoint,
                    Scenarios = scenariosForFeature
                        .DistinctBy(x => x.Id)
                        .OrderByDescending(x => x.Metadata.TestDetails.Categories.Contains(HappyPathAttribute.HappyPathCategoryKey))
                        .ThenBy(x => x.Metadata.DisplayName)
                        .Select(x =>
                        {
                            var displayName = ScenarioTitleResolver.FormatScenarioDisplayName(x.Metadata.DisplayName);

                            var structuredResult = TryExtractStructuredParametersWithRaw(x);
                            var structuredParams = structuredResult?.StringValues;
                            Dictionary<string, object?>? rawValues = structuredResult?.RawValues;
                            var parsed = structuredParams ?? ParameterParser.Parse(displayName);

                            return new Scenario
                            {
                                Id = x.Id,
                                Result = x.Execution.Result?.State.ToExecutionResult() ?? ExecutionResult.Skipped,
                                DisplayName = displayName,
                                IsHappyPath = x.Metadata.TestDetails.Categories.Contains(HappyPathAttribute.HappyPathCategoryKey),
                                ErrorMessage = x.Execution.Result?.Exception?.Message,
                                ErrorStackTrace = x.Execution.Result?.Exception?.StackTrace,
                                Duration = x.Execution.Result?.Duration,
                                EndedAt = x.Execution.Result?.End,
                                OutlineId = parsed is { Count: > 0 } ? (structuredParams is not null ? GetStructuredOutlineId(x) : ParameterParser.ExtractBaseName(displayName)) : null,
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
