using Kronikol.Tracking;
using Kronikol.xUnit2;

namespace Kronikol.Tests.xUnit2;

public class ScenarioInfoCollectionExtensionsTests
{
    private static ScenarioInfo MakeScenario(
        string id = "t1",
        string featureName = "Feature",
        string scenarioName = "Scenario",
        string methodMatchKey = "key")
    {
        return new ScenarioInfo
        {
            Id = id,
            FeatureName = featureName,
            ScenarioName = scenarioName,
            MethodMatchKey = methodMatchKey,
        };
    }

    [Fact]
    public void ToFeatures_carries_when_the_scenario_ended()
    {
        var ended = new DateTimeOffset(2026, 1, 1, 10, 0, 3, 250, TimeSpan.Zero);
        var info = MakeScenario();
        info.EndedAt = ended;

        var features = new[] { info }.ToFeatures();

        Assert.Equal(ended, features[0].Scenarios[0].EndedAt);
    }

    [Fact]
    public void ToFeatures_populates_steps_from_StepCollector()
    {
        var testId = "xunit2-steps-" + Guid.NewGuid();

        StepCollector.StartStep(testId, "Given", "a valid request", null, null);
        StepCollector.CompleteStep(testId, true);
        StepCollector.StartStep(testId, "When", "the request is sent", null, null);
        StepCollector.CompleteStep(testId, true);

        var info = MakeScenario(id: testId);

        try
        {
            var features = new[] { info }.ToFeatures();
            var scenario = features[0].Scenarios[0];

            Assert.NotNull(scenario.Steps);
            Assert.Equal(2, scenario.Steps!.Length);
            Assert.Equal("Given", scenario.Steps[0].Keyword);
            Assert.Equal("a valid request", scenario.Steps[0].Text);
            Assert.Equal("When", scenario.Steps[1].Keyword);
        }
        finally
        {
            StepCollector.ClearSteps(testId);
        }
    }

    [Fact]
    public void ToFeatures_leaves_steps_null_when_StepCollector_empty()
    {
        var info = MakeScenario();
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Steps);
    }
}
