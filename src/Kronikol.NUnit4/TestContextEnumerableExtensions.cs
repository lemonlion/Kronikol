using Kronikol.Reports;
using Kronikol.Tracking;
using NUnit.Framework;

namespace Kronikol.NUnit4;

internal static class TestContextEnumerableExtensions
{
    public static Feature[] ToFeatures(this IEnumerable<TestContext> contexts)
    {
        return contexts
            .GroupBy(x => x.Test.DisplayName)
            .OrderBy(x => x.Key)
            .Select(scenariosForFeature =>
            {
                var firstScenarioForFeature = scenariosForFeature.First();

                string? endpoint = null;
                if (firstScenarioForFeature.Test.Parent!.Properties.ContainsKey(EndpointAttribute.EndpointPropertyKey))
                    endpoint = firstScenarioForFeature.Test.Parent.Properties[EndpointAttribute.EndpointPropertyKey][0]?.ToString();

                return new Feature
                {
                    DisplayName = firstScenarioForFeature.Test.DisplayName!.Titleize(),
                    Endpoint = endpoint,
                    Scenarios = scenariosForFeature
                        .DistinctBy(x => x.Test.ID)
                        .OrderByDescending(x => x.Test.Properties.ContainsKey(HappyPathAttribute.HappyPathPropertyKey))
                        .ThenBy(x => x.Test.MethodName)
                        .Select(x =>
                        {
                            var displayName = ScenarioTitleResolver.FormatScenarioDisplayName(x.Test.Name!);

                            var structuredResult = TryExtractStructuredParametersWithRaw(x);
                            var structuredParams = structuredResult?.StringValues;
                            Dictionary<string, object?>? rawValues = structuredResult?.RawValues;
                            var parsed = structuredParams ?? ParameterParser.Parse(displayName);

                            return new Scenario
                            {
                                Id = x.Test.ID,
                                Result = x.Result.Outcome.Status.ToExecutionResult(),
                                DisplayName = displayName,
                                IsHappyPath = x.Test.Properties.ContainsKey(HappyPathAttribute.HappyPathPropertyKey),
                                ErrorMessage = x.Result.Message,
                                ErrorStackTrace = x.Result.StackTrace,
                                Duration = DiagrammedTestRun.TestDurations.TryGetValue(x.Test.ID, out var dur) ? dur : null,
                                EndedAt = DiagrammedTestRun.TestEnds.TryGetValue(x.Test.ID, out var endedAt) ? endedAt : null,
                                OutlineId = parsed is { Count: > 0 } ? (structuredParams is not null ? GetStructuredOutlineId(x) : ParameterParser.ExtractBaseName(displayName)) : null,
                                ExampleValues = parsed is { Count: > 0 } ? parsed : null,
                                ExampleRawValues = rawValues,
                                Steps = StepCollector.GetSteps(x.Test.ID) is { Length: > 0 } steps ? steps : null,
                                Attachments = StepCollector.GetScenarioAttachments(x.Test.ID)
                            };
                        }).ToArray()
                };
            }).ToArray();
    }

    private static (Dictionary<string, string> StringValues, Dictionary<string, object?> RawValues)? TryExtractStructuredParametersWithRaw(TestContext context)
    {
        try
        {
            var args = context.Test.Arguments;
            var methodParams = context.Test.MethodInfo?.GetParameters()?.ToArray();
            if (args is not { Length: > 0 } || methodParams is not { Length: > 0 })
                return null;

            var paramNames = methodParams.Select(p => p.Name).ToArray();
            return ParameterParser.ExtractStructuredParametersWithRaw(args, paramNames);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStructuredOutlineId(TestContext context)
    {
        return context.Test.MethodName;
    }
}
