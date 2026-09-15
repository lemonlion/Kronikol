using System.Linq;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.BDDfy;

public class BDDfyScenarioInfoExtensionsTests
{
    private static BDDfyScenarioInfo MakeScenario(
        string testId = "t1",
        string storyTitle = "Story",
        string scenarioTitle = "Scenario",
        string? storyDescription = null,
        string[]? tags = null,
        List<BDDfyStepInfo>? steps = null,
        TestStack.BDDfy.Result result = TestStack.BDDfy.Result.Passed,
        TimeSpan duration = default,
        object?[]? rawArguments = null,
        string[]? parameterNames = null,
        string? testMethodName = null)
    {
        return new BDDfyScenarioInfo
        {
            TestId = testId,
            StoryTitle = storyTitle,
            StoryDescription = storyDescription,
            ScenarioTitle = scenarioTitle,
            Tags = tags ?? [],
            Steps = steps ?? [],
            Result = result,
            Duration = duration,
            RawArguments = rawArguments,
            ParameterNames = parameterNames,
            TestMethodName = testMethodName,
        };
    }

    [Fact]
    public void ToFeatures_carries_when_the_scenario_ended()
    {
        var ended = new DateTimeOffset(2026, 1, 1, 10, 0, 3, 250, TimeSpan.Zero);
        var info = MakeScenario() with { EndedAt = ended };

        var features = new[] { info }.ToFeatures();

        Assert.Equal(ended, features[0].Scenarios[0].EndedAt);
    }

    [Fact]
    public void ToFeatures_maps_steps_to_scenario_steps()
    {
        var info = MakeScenario(steps:
        [
            new BDDfyStepInfo("Given", "a valid request"),
            new BDDfyStepInfo("When", "the request is sent"),
            new BDDfyStepInfo("Then", "the response is 200")
        ]);

        var features = new[] { info }.ToFeatures();
        var scenario = features[0].Scenarios[0];

        Assert.NotNull(scenario.Steps);
        Assert.Equal(3, scenario.Steps!.Length);
        Assert.Equal("Given", scenario.Steps[0].Keyword);
        Assert.Equal("a valid request", scenario.Steps[0].Text);
        Assert.Equal("When", scenario.Steps[1].Keyword);
        Assert.Equal("Then", scenario.Steps[2].Keyword);
    }

    [Fact]
    public void ToFeatures_leaves_steps_null_when_no_steps()
    {
        var info = MakeScenario(steps: []);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Steps);
    }

    [Fact]
    public void ToFeatures_maps_story_description_to_feature_description()
    {
        var info = MakeScenario(storyDescription: "As a user I want to order");
        var features = new[] { info }.ToFeatures();
        Assert.Equal("As a user I want to order", features[0].Description);
    }

    [Fact]
    public void ToFeatures_leaves_description_null_when_no_story_description()
    {
        var info = MakeScenario(storyDescription: null);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Description);
    }

    [Fact]
    public void ToFeatures_maps_tags_to_scenario_labels_excluding_special_tags()
    {
        var info = MakeScenario(tags: ["smoke", "happy-path", "endpoint:GET /api", "regression"]);
        var features = new[] { info }.ToFeatures();
        var scenario = features[0].Scenarios[0];

        Assert.NotNull(scenario.Labels);
        Assert.Contains("smoke", scenario.Labels!);
        Assert.Contains("regression", scenario.Labels!);
        Assert.DoesNotContain("happy-path", scenario.Labels!);
        Assert.DoesNotContain("endpoint:GET /api", scenario.Labels!);
    }

    [Fact]
    public void ToFeatures_leaves_labels_null_when_no_non_special_tags()
    {
        var info = MakeScenario(tags: ["happy-path"]);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Labels);
    }

    [Fact]
    public void ToFeatures_maps_duration_to_scenario_duration()
    {
        var info = MakeScenario(duration: TimeSpan.FromMilliseconds(1234));
        var features = new[] { info }.ToFeatures();
        Assert.Equal(TimeSpan.FromMilliseconds(1234), features[0].Scenarios[0].Duration);
    }

    // ─── Step status mapping ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_step_result_passed()
    {
        var info = MakeScenario(steps:
        [
            new BDDfyStepInfo("Given", "something", TestStack.BDDfy.Result.Passed)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Passed, features[0].Scenarios[0].Steps![0].Status);
    }

    [Fact]
    public void ToFeatures_maps_step_result_failed()
    {
        var info = MakeScenario(steps:
        [
            new BDDfyStepInfo("Given", "something", TestStack.BDDfy.Result.Failed)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Failed, features[0].Scenarios[0].Steps![0].Status);
    }

    [Fact]
    public void ToFeatures_maps_step_result_inconclusive_to_skipped()
    {
        var info = MakeScenario(steps:
        [
            new BDDfyStepInfo("Given", "something", TestStack.BDDfy.Result.Inconclusive)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Skipped, features[0].Scenarios[0].Steps![0].Status);
    }

    [Fact]
    public void ToFeatures_maps_step_result_not_implemented_to_skipped()
    {
        var info = MakeScenario(steps:
        [
            new BDDfyStepInfo("Given", "something", TestStack.BDDfy.Result.NotImplemented)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Skipped, features[0].Scenarios[0].Steps![0].Status);
    }

    [Fact]
    public void ToFeatures_maps_not_executed_after_failure_to_skipped_after_failure()
    {
        var info = MakeScenario(steps:
        [
            new BDDfyStepInfo("Given", "this fails", TestStack.BDDfy.Result.Failed),
            new BDDfyStepInfo("Then", "this was not executed", TestStack.BDDfy.Result.NotExecuted)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Failed, features[0].Scenarios[0].Steps![0].Status);
        Assert.Equal(ExecutionResult.SkippedAfterFailure, features[0].Scenarios[0].Steps![1].Status);
    }

    [Fact]
    public void ToFeatures_maps_not_executed_without_prior_failure_to_skipped()
    {
        var info = MakeScenario(steps:
        [
            new BDDfyStepInfo("Given", "this passed", TestStack.BDDfy.Result.Passed),
            new BDDfyStepInfo("Then", "this was not executed", TestStack.BDDfy.Result.NotExecuted)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Skipped, features[0].Scenarios[0].Steps![1].Status);
    }

    // ─── Error extraction ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_error_message_and_stack_trace()
    {
        var info = new BDDfyScenarioInfo
        {
            TestId = "t1",
            StoryTitle = "Story",
            ScenarioTitle = "Scenario",
            Tags = [],
            Steps = [],
            Result = TestStack.BDDfy.Result.Failed,
            ErrorMessage = "Expected 200 but got 500",
            ErrorStackTrace = "at MyTests.Test() in Test.cs:line 42"
        };
        var features = new[] { info }.ToFeatures();
        Assert.Equal("Expected 200 but got 500", features[0].Scenarios[0].ErrorMessage);
        Assert.Equal("at MyTests.Test() in Test.cs:line 42", features[0].Scenarios[0].ErrorStackTrace);
    }

    [Fact]
    public void ToFeatures_leaves_error_fields_null_when_passed()
    {
        var info = MakeScenario(result: TestStack.BDDfy.Result.Passed);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].ErrorMessage);
        Assert.Null(features[0].Scenarios[0].ErrorStackTrace);
    }

    // ─── Step duration ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_step_duration()
    {
        var info = MakeScenario(steps:
        [
            new BDDfyStepInfo("Given", "something", Duration: TimeSpan.FromMilliseconds(150))
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(TimeSpan.FromMilliseconds(150), features[0].Scenarios[0].Steps![0].Duration);
    }

    [Fact]
    public void ToFeatures_leaves_step_duration_null_when_not_provided()
    {
        var info = MakeScenario(steps:
        [
            new BDDfyStepInfo("Given", "something")
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Steps![0].Duration);
    }

    // ─── Feature name titleization ────────────────────────────────

    [Theory]
    [InlineData("AlternativeEvidenceScenarios", "Alternative Evidence Scenarios")]
    [InlineData("OrderProcessing", "Order Processing")]
    [InlineData("Alternative Evidence Scenarios", "Alternative Evidence Scenarios")]
    [InlineData("my_feature_name", "My Feature Name")]
    public void ToFeatures_titleizes_feature_display_name(string storyTitle, string expected)
    {
        var info = MakeScenario(storyTitle: storyTitle);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(expected, features[0].DisplayName);
    }

    // ─── Deduplication uses bracket format ────────────────────────────────

    [Fact]
    public void ToFeatures_deduplicates_scenario_titles_with_bracket_suffix()
    {
        var infos = new[]
        {
            MakeScenario(testId: "t1", scenarioTitle: "Validate something"),
            MakeScenario(testId: "t2", scenarioTitle: "Validate something"),
            MakeScenario(testId: "t3", scenarioTitle: "Validate something")
        };

        var features = infos.ToFeatures();
        var names = features[0].Scenarios.Select(s => s.DisplayName).ToArray();

        Assert.Equal("Validate something [1]", names[0]);
        Assert.Equal("Validate something [2]", names[1]);
        Assert.Equal("Validate something [3]", names[2]);
    }

    [Fact]
    public void ToFeatures_does_not_deduplicate_unique_titles()
    {
        var infos = new[]
        {
            MakeScenario(testId: "t1", scenarioTitle: "First scenario"),
            MakeScenario(testId: "t2", scenarioTitle: "Second scenario")
        };

        var features = infos.ToFeatures();
        var names = features[0].Scenarios.Select(s => s.DisplayName).ToArray();

        Assert.Equal("First scenario", names[0]);
        Assert.Equal("Second scenario", names[1]);
    }

    // ─── Inline scenario result mapping (Issue #45) ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_not_executed_with_empty_steps_to_passed()
    {
        // When BDDfy scenarios use inline code (no step methods), Steps is empty
        // and Result is NotExecuted. Since the test completed (reached the Report processor),
        // this should map to Passed, not Skipped.
        var info = MakeScenario(steps: [], result: TestStack.BDDfy.Result.NotExecuted);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Passed, features[0].Scenarios[0].Result);
    }

    [Fact]
    public void ToFeatures_maps_not_executed_with_steps_to_skipped()
    {
        // When steps exist but result is NotExecuted (shouldn't normally happen with
        // non-empty steps, but verify we don't break existing behavior)
        var info = MakeScenario(
            steps: [new BDDfyStepInfo("Given", "something", TestStack.BDDfy.Result.NotExecuted)],
            result: TestStack.BDDfy.Result.NotExecuted);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Skipped, features[0].Scenarios[0].Result);
    }

    [Fact]
    public void ToFeatures_maps_passed_with_empty_steps_to_passed()
    {
        var info = MakeScenario(steps: [], result: TestStack.BDDfy.Result.Passed);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Passed, features[0].Scenarios[0].Result);
    }

    [Fact]
    public void ToFeatures_maps_failed_with_empty_steps_to_failed()
    {
        // Even with empty steps, if BDDfy marks it Failed, respect that
        var info = MakeScenario(steps: [], result: TestStack.BDDfy.Result.Failed);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Failed, features[0].Scenarios[0].Result);
    }

    // ── OutlineId from TestMethodName ──

    [Fact]
    public void ToFeatures_uses_TestMethodName_for_OutlineId_when_structured_params_available()
    {
        // When raw arguments and TestMethodName are provided, OutlineId should be the raw
        // method name — not parsed from the scenario title (which may contain truncated
        // generic type notation that breaks ExtractBaseName)
        var scenario1 = MakeScenario(
            testId: "t1",
            scenarioTitle: "Different muffin recipes should produce the expected batch [recipeName: \"Classic\", recipe: MuffinRecipeTestData { Toppings = System.Collections.Generic.List`1[ToppingData] …]",
            rawArguments: ["Classic", 180],
            parameterNames: ["recipeName", "temperature"],
            testMethodName: "Different_muffin_recipes_should_produce_the_expected_batch");

        var scenario2 = MakeScenario(
            testId: "t2",
            scenarioTitle: "Different muffin recipes should produce the expected batch [recipeName: \"Rustic\", recipe: MuffinRecipeTestData { Toppings = System.Collections.Generic.List`1[ToppingData] …]",
            rawArguments: ["Rustic", 175],
            parameterNames: ["recipeName", "temperature"],
            testMethodName: "Different_muffin_recipes_should_produce_the_expected_batch");

        var features = new[] { scenario1, scenario2 }.ToFeatures();
        var scenarios = features[0].Scenarios;

        // Both should share the same OutlineId based on the raw method name
        Assert.Equal("Different_muffin_recipes_should_produce_the_expected_batch", scenarios[0].OutlineId);
        Assert.Equal(scenarios[0].OutlineId, scenarios[1].OutlineId);
    }

    [Fact]
    public void ToFeatures_falls_back_to_ExtractBaseName_when_TestMethodName_is_null()
    {
        var scenario = MakeScenario(
            scenarioTitle: "Some scenario [param: value]",
            rawArguments: ["value"],
            parameterNames: ["param"],
            testMethodName: null);

        var features = new[] { scenario }.ToFeatures();

        Assert.Equal("Some scenario", features[0].Scenarios[0].OutlineId);
    }

    [Fact]
    public void ToFeatures_uses_TestMethodName_for_OutlineId_on_string_parsed_path()
    {
        // Even when no raw arguments exist (fallback to title parsing), TestMethodName
        // should be used for OutlineId when available
        var scenario = MakeScenario(
            scenarioTitle: "Some scenario [param: value]",
            testMethodName: "Some_test_method");

        var features = new[] { scenario }.ToFeatures();

        Assert.Equal("Some_test_method", features[0].Scenarios[0].OutlineId);
    }

    // ─── Assertion sub-step merging from StepCollector ──────────

    [Fact]
    public void ToFeatures_merges_assertion_substeps_from_StepCollector_into_native_steps()
    {
        var testId = "bddfy-assert-merge-" + Guid.NewGuid();

        // Simulate BDDfyStepTrackingExecutor: push step, add assertions, complete
        StepCollector.StartStep(testId, "Then", "the response should be valid", null, null);
        StepCollector.AddAssertionSubStep(testId, "response.StatusCode.Should().Be(200)", passed: true);
        StepCollector.AddAssertionSubStep(testId, "response.Body.Should().NotBeNull()", passed: true);
        StepCollector.CompleteStep(testId, true);

        var info = MakeScenario(
            testId: testId,
            steps:
            [
                new BDDfyStepInfo("Then", "the response should be valid")
            ]);

        try
        {
            var features = new[] { info }.ToFeatures();
            var step = features[0].Scenarios[0].Steps![0];

            Assert.NotNull(step.SubSteps);
            Assert.Equal(2, step.SubSteps!.Length);
            Assert.Equal("response.StatusCode.Should().Be(200)", step.SubSteps[0].Text);
            Assert.Equal("response.Body.Should().NotBeNull()", step.SubSteps[1].Text);
        }
        finally
        {
            StepCollector.ClearSteps(testId);
        }
    }

    [Fact]
    public void ToFeatures_merges_assertion_substeps_only_to_matching_step_index()
    {
        var testId = "bddfy-assert-index-" + Guid.NewGuid();

        // Step 0: Given — no assertions
        StepCollector.StartStep(testId, "Given", "a valid request", null, null);
        StepCollector.CompleteStep(testId, true);

        // Step 1: When — no assertions
        StepCollector.StartStep(testId, "When", "the request is sent", null, null);
        StepCollector.CompleteStep(testId, true);

        // Step 2: Then — has assertions
        StepCollector.StartStep(testId, "Then", "the response is 200", null, null);
        StepCollector.AddAssertionSubStep(testId, "status.Should().Be(200)", passed: true);
        StepCollector.CompleteStep(testId, true);

        var info = MakeScenario(
            testId: testId,
            steps:
            [
                new BDDfyStepInfo("Given", "a valid request"),
                new BDDfyStepInfo("When", "the request is sent"),
                new BDDfyStepInfo("Then", "the response is 200")
            ]);

        try
        {
            var features = new[] { info }.ToFeatures();
            var steps = features[0].Scenarios[0].Steps!;

            Assert.Null(steps[0].SubSteps);
            Assert.Null(steps[1].SubSteps);
            Assert.NotNull(steps[2].SubSteps);
            Assert.Single(steps[2].SubSteps!);
            Assert.Equal("status.Should().Be(200)", steps[2].SubSteps![0].Text);
        }
        finally
        {
            StepCollector.ClearSteps(testId);
        }
    }
}
