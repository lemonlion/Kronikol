using Reqnroll;
using Kronikol.ReqNRoll;
using Kronikol.Reports;

namespace Kronikol.Tests.ReqNRoll;

public class ReqNRollTUnitScenarioInfoExtensionsTests
{
    private static ReqNRollScenarioInfo MakeScenario(
        string scenarioId = "s1",
        string scenarioTitle = "Scenario",
        string? scenarioDescription = null,
        string featureTitle = "Feature",
        string? featureDescription = null,
        string[]? scenarioTags = null,
        string[]? combinedTags = null,
        List<ReqNRollStepInfo>? steps = null,
        ScenarioExecutionStatus status = ScenarioExecutionStatus.OK)
    {
        var tags = scenarioTags ?? [];
        return new ReqNRollScenarioInfo
        {
            ScenarioId = scenarioId,
            ScenarioTitle = scenarioTitle,
            ScenarioDescription = scenarioDescription,
            FeatureTitle = featureTitle,
            FeatureDescription = featureDescription,
            ScenarioTags = tags,
            CombinedTags = combinedTags ?? tags,
            Steps = steps ?? [],
            ExecutionStatus = status
        };
    }

    [Fact]
    public void ToFeatures_maps_steps_to_scenario_steps()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "a valid request"),
            new ReqNRollStepInfo("When", "the request is sent"),
            new ReqNRollStepInfo("Then", "the response is 200")
        ]);

        var features = new[] { info }.ToFeatures();
        var scenario = features[0].Scenarios[0];

        Assert.NotNull(scenario.Steps);
        Assert.Equal(3, scenario.Steps!.Length);
        Assert.Equal("Given", scenario.Steps[0].Keyword);
        Assert.Equal("a valid request", scenario.Steps[0].Text);
    }

    [Fact]
    public void ToFeatures_leaves_steps_null_when_no_steps()
    {
        var info = MakeScenario(steps: []);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Steps);
    }

    [Fact]
    public void ToFeatures_maps_feature_description()
    {
        var info = MakeScenario(featureDescription: "As a user I want to register");
        var features = new[] { info }.ToFeatures();
        Assert.Equal("As a user I want to register", features[0].Description);
    }

    [Fact]
    public void ToFeatures_maps_scenario_description()
    {
        var info = MakeScenario(scenarioDescription: "The batch must come out fresh");
        var features = new[] { info }.ToFeatures();
        Assert.Equal("The batch must come out fresh", features[0].Scenarios[0].Description);
    }

    [Fact]
    public void ToFeatures_dedents_scenario_description_indentation()
    {
        var info = MakeScenario(scenarioDescription: "        Bakers rely on this guarantee\r\n        every single morning");
        var features = new[] { info }.ToFeatures();
        Assert.Equal("Bakers rely on this guarantee\nevery single morning", features[0].Scenarios[0].Description);
    }

    [Fact]
    public void ToFeatures_maps_tags_to_scenario_labels_excluding_special_tags()
    {
        var info = MakeScenario(
            scenarioTags: ["smoke", "happy-path", "regression"],
            combinedTags: ["smoke", "happy-path", "endpoint:GET /api", "regression"]);

        var features = new[] { info }.ToFeatures();
        var scenario = features[0].Scenarios[0];

        Assert.NotNull(scenario.Labels);
        Assert.Contains("smoke", scenario.Labels!);
        Assert.Contains("regression", scenario.Labels!);
        Assert.DoesNotContain("happy-path", scenario.Labels!);
    }

    // ─── Step status mapping ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_step_status_ok_to_passed()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "something", ScenarioExecutionStatus.OK)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Passed, features[0].Scenarios[0].Steps![0].Status);
    }

    [Fact]
    public void ToFeatures_maps_step_status_test_error_to_failed()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "something", ScenarioExecutionStatus.TestError)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Failed, features[0].Scenarios[0].Steps![0].Status);
    }

    [Fact]
    public void ToFeatures_maps_step_status_skipped_after_failure_to_skipped_after_failure()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "this fails", ScenarioExecutionStatus.TestError),
            new ReqNRollStepInfo("Then", "this was skipped", ScenarioExecutionStatus.Skipped)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Failed, features[0].Scenarios[0].Steps![0].Status);
        Assert.Equal(ExecutionResult.SkippedAfterFailure, features[0].Scenarios[0].Steps![1].Status);
    }

    [Fact]
    public void ToFeatures_maps_step_status_skipped_without_prior_failure_to_skipped()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "this passed", ScenarioExecutionStatus.OK),
            new ReqNRollStepInfo("Then", "this was skipped", ScenarioExecutionStatus.Skipped)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Skipped, features[0].Scenarios[0].Steps![1].Status);
    }

    // ─── Scenario-level status mapping ──────────────────────

    [Fact]
    public void ExecutionStatus_OK_maps_to_Passed()
    {
        Assert.Equal(ExecutionResult.Passed, ScenarioExecutionStatus.OK.ToExecutionResult());
    }

    [Fact]
    public void ExecutionStatus_TestError_maps_to_Failed()
    {
        Assert.Equal(ExecutionResult.Failed, ScenarioExecutionStatus.TestError.ToExecutionResult());
    }

    [Fact]
    public void ExecutionStatus_UndefinedStep_maps_to_Skipped()
    {
        Assert.Equal(ExecutionResult.Skipped, ScenarioExecutionStatus.UndefinedStep.ToExecutionResult());
    }

    // ─── Test context ───────────────────────────────────────

    [Fact]
    public void TestContext_stores_and_retrieves_current_test_info()
    {
        ReqNRollTestContext.CurrentTestInfo = ("test name", "test-id-123");
        Assert.Equal(("test name", "test-id-123"), ReqNRollTestContext.CurrentTestInfo);
        ReqNRollTestContext.CurrentTestInfo = null;
    }

    [Fact]
    public void TestContext_stores_and_retrieves_step_type()
    {
        ReqNRollTestContext.CurrentStepType = "Given";
        Assert.Equal("Given", ReqNRollTestContext.CurrentStepType);
        ReqNRollTestContext.CurrentStepType = null;
    }

    [Fact]
    public void Options_has_current_test_info_fetcher()
    {
        ReqNRollTestContext.CurrentTestInfo = ("my test", "id-456");
        try
        {
            var options = new ReqNRollTestTrackingMessageHandlerOptions();
            var info = options.CurrentTestInfoFetcher!();
            Assert.Equal("my test", info.Name);
            Assert.Equal("id-456", info.Id);
        }
        finally
        {
            ReqNRollTestContext.CurrentTestInfo = null;
        }
    }

    [Fact]
    public void Options_has_step_type_fetcher()
    {
        ReqNRollTestContext.CurrentStepType = "When";
        try
        {
            var options = new ReqNRollTestTrackingMessageHandlerOptions();
            var stepType = options.CurrentStepTypeFetcher!();
            Assert.Equal("When", stepType);
        }
        finally
        {
            ReqNRollTestContext.CurrentStepType = null;
        }
    }

    [Fact]
    public void Constants_match_expected_values()
    {
        Assert.Equal("Kronikol.RuntimeId", ReqNRollConstants.ScenarioRuntimeIdKey);
        Assert.Equal("Kronikol.Steps", ReqNRollConstants.StepsCollectionKey);
        Assert.Equal("happy-path", ReqNRollConstants.HappyPathTag);
        Assert.Equal("endpoint:", ReqNRollConstants.EndpointTagPrefix);
    }

    // ─── Feature name titleization ────────────────────────────────

    [Theory]
    [InlineData("AlternativeEvidenceScenarios", "Alternative Evidence Scenarios")]
    [InlineData("OrderProcessing", "Order Processing")]
    [InlineData("Alternative Evidence Scenarios", "Alternative Evidence Scenarios")]
    [InlineData("my_feature_name", "My Feature Name")]
    public void ToFeatures_titleizes_feature_display_name(string featureTitle, string expected)
    {
        var info = MakeScenario(featureTitle: featureTitle);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(expected, features[0].DisplayName);
    }
}
