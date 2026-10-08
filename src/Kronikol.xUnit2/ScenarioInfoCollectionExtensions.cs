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
                                // Null when there is nothing to report, the rule every failure field follows
                                // (FailureText.OrNull): an empty string here gave every passing xUnit v2
                                // scenario an errorMessage of "".
                                ErrorMessage = FailureText.OrNull(x.ErrorMessage),
                                ErrorStackTrace = FailureText.OrNull(x.ErrorStackTrace),
                                Duration = x.Duration,
                                EndedAt = x.EndedAt,
                                ResultDefaulted = x.ResultDefaulted,
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
