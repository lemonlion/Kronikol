using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.xUnit2;

internal static class ScenarioInfoCollectionExtensions
{
    public static Feature[] ToFeatures(this IEnumerable<ScenarioInfo> scenarios)
    {
        return scenarios
            .OrderBy(x => x.FeatureName)
            .GroupBy(x => x.FeatureName)
            .Select(scenariosForFeature =>
            {
                var first = scenariosForFeature.First();
                return new Feature
                {
                    DisplayName = first.FeatureName,
                    Endpoint = first.Endpoint,
                    Scenarios = scenariosForFeature
                        .OrderByDescending(x => x.IsHappyPath)
                        .ThenBy(x => x.ScenarioName)
                        .Select(x =>
                        {
                            var parsed = ParameterParser.Parse(x.ScenarioName);
                            return new Scenario
                            {
                                Id = x.Id,
                                Result = x.Result,
                                DisplayName = x.ScenarioName,
                                IsHappyPath = x.IsHappyPath,
                                ErrorMessage = x.ErrorMessage ?? string.Empty,
                                ErrorStackTrace = x.ErrorStackTrace ?? string.Empty,
                                Duration = x.Duration,
                                EndedAt = x.EndedAt,
                                Steps = StepCollector.GetSteps(x.Id) is { Length: > 0 } steps ? steps : null,
                                Attachments = StepCollector.GetScenarioAttachments(x.Id),
                                OutlineId = parsed is { Count: > 0 } ? ParameterParser.ExtractBaseName(x.ScenarioName) : null,
                                ExampleValues = parsed is { Count: > 0 } ? parsed : null
                            };
                        }).ToArray()
                };
            }).ToArray();
    }
}
