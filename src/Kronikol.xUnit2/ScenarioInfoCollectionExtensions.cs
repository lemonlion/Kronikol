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
                // The feature's are the categories all its tests have at its level, not the first test's (5.2.7): two
                // classes of one name are one feature. A test keeps the rest of its class's on its own.
                var featureTags = ScenarioTags.Shared(scenariosForFeature.Select(x => ScenarioTags.Classify(x.FeatureTags).Tags));
                return new Feature
                {
                    DisplayName = first.FeatureName,
                    Endpoint = first.Endpoint,
                    Tags = featureTags.Length > 0 ? featureTags : null,
                    Scenarios = scenariosForFeature
                        .OrderByDescending(x => x.IsHappyPath || ScenarioTags.Classify(x.Tags).IsHappyPath)
                        .ThenBy(x => x.ScenarioName)
                        .Select(x =>
                        {
                            var parsed = ParameterParser.Parse(x.ScenarioName);
                            var classified = ScenarioTags.Classify(x.Tags);
                            var tags = ScenarioTags.Merge(ScenarioTags.Classify(x.FeatureTags).Tags, classified.Tags)
                                .Except(featureTags, ScenarioTags.Comparer).ToArray();
                            return new Scenario
                            {
                                Id = x.Id,
                                Result = x.Result,
                                DisplayName = x.ScenarioName,
                                IsHappyPath = x.IsHappyPath || classified.IsHappyPath,
                                Tags = tags.Length > 0 ? tags : null,
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
