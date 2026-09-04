using Reqnroll;
using Kronikol.ReqNRoll;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.ReqNRoll;

public class ReqNRollScenarioInfoExtensionsTests
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
    public void ToFeatures_leaves_description_null_when_absent()
    {
        var info = MakeScenario(featureDescription: null);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Description);
    }

    [Fact]
    public void ToFeatures_dedents_feature_description_indentation()
    {
        // Reqnroll's generated code passes descriptions with the feature-file
        // indentation intact; the Cucumber ingest path dedents, so the live path must too.
        var info = MakeScenario(featureDescription: "    As a user\r\n    I want to register");
        var features = new[] { info }.ToFeatures();
        Assert.Equal("As a user\nI want to register", features[0].Description);
    }

    [Fact]
    public void ToFeatures_leaves_feature_description_null_when_blank()
    {
        var info = MakeScenario(featureDescription: "   ");
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Description);
    }

    // ─── Scenario description mapping ────────────────────────────────

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
    public void ToFeatures_leaves_scenario_description_null_when_absent()
    {
        var info = MakeScenario(scenarioDescription: null);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Description);
    }

    [Fact]
    public void ToFeatures_leaves_scenario_description_null_when_blank()
    {
        var info = MakeScenario(scenarioDescription: "   ");
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Description);
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

    [Fact]
    public void ToFeatures_leaves_labels_null_when_only_special_tags()
    {
        var info = MakeScenario(scenarioTags: ["happy-path"]);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Labels);
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
    public void ToFeatures_maps_step_status_binding_error_to_failed()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "something", ScenarioExecutionStatus.BindingError)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Failed, features[0].Scenarios[0].Steps![0].Status);
    }

    [Fact]
    public void ToFeatures_maps_step_status_undefined_step_to_skipped()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "something", ScenarioExecutionStatus.UndefinedStep)
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(ExecutionResult.Skipped, features[0].Scenarios[0].Steps![0].Status);
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

    // ─── Step duration mapping ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_step_duration()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "something", Duration: TimeSpan.FromMilliseconds(250))
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal(TimeSpan.FromMilliseconds(250), features[0].Scenarios[0].Steps![0].Duration);
    }

    [Fact]
    public void ToFeatures_leaves_step_duration_null_when_not_provided()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "something")
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Steps![0].Duration);
    }

    // ─── DocString mapping ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_step_doc_string()
    {
        var info = MakeScenario(steps:
        [
            new ReqNRollStepInfo("Given", "a request body", DocString: """{"name":"test"}""")
        ]);
        var features = new[] { info }.ToFeatures();
        Assert.Equal("""{"name":"test"}""", features[0].Scenarios[0].Steps![0].DocString);
    }

    // ─── Categories mapping ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_category_tags_to_scenario_categories()
    {
        var info = MakeScenario(
            scenarioTags: ["smoke", "category:Integration"],
            combinedTags: ["smoke", "category:Integration"]);
        var features = new[] { info }.ToFeatures();
        var scenario = features[0].Scenarios[0];

        Assert.NotNull(scenario.Categories);
        Assert.Equal(["Integration"], scenario.Categories!);
        Assert.DoesNotContain("category:Integration", scenario.Labels!);
    }

    [Fact]
    public void ToFeatures_leaves_categories_null_when_no_category_tags()
    {
        var info = MakeScenario(scenarioTags: ["smoke"]);
        var features = new[] { info }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Categories);
    }

    // ─── Rule mapping ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_rule()
    {
        var info = new ReqNRollScenarioInfo
        {
            ScenarioId = "s1",
            ScenarioTitle = "Test",
            FeatureTitle = "Feature",
            ScenarioTags = [],
            CombinedTags = [],
            Steps = [],
            Rule = "Business Rule 1"
        };
        var features = new[] { info }.ToFeatures();
        Assert.Equal("Business Rule 1", features[0].Scenarios[0].Rule);
    }

    // ─── ExampleValues mapping ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_example_values()
    {
        var info = new ReqNRollScenarioInfo
        {
            ScenarioId = "s1",
            ScenarioTitle = "Test with <amount>",
            FeatureTitle = "Feature",
            ScenarioTags = [],
            CombinedTags = [],
            Steps = [],
            ExampleValues = new Dictionary<string, string> { ["amount"] = "100", ["currency"] = "GBP" }
        };
        var features = new[] { info }.ToFeatures();
        Assert.NotNull(features[0].Scenarios[0].ExampleValues);
        Assert.Equal("100", features[0].Scenarios[0].ExampleValues!["amount"]);
        Assert.Equal("GBP", features[0].Scenarios[0].ExampleValues!["currency"]);
    }

    // ─── Feature labels mapping ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_feature_level_labels_from_combined_tags()
    {
        var info = MakeScenario(
            scenarioTags: ["scenario-tag"],
            combinedTags: ["feature-api", "scenario-tag", "endpoint:GET /api"]);
        var features = new[] { info }.ToFeatures();

        Assert.NotNull(features[0].Labels);
        Assert.Contains("feature-api", features[0].Labels!);
        Assert.DoesNotContain("scenario-tag", features[0].Labels!);
        Assert.DoesNotContain("endpoint:GET /api", features[0].Labels!);
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

    // ─── Assertion sub-steps mapping ────────────────────────────────

    [Fact]
    public void ToFeatures_maps_assertion_sub_steps_from_StepCollector()
    {
        var scenarioId = "assertion-sub-step-test-" + Guid.NewGuid();

        // Simulate what the hooks will do: push a step, add assertion sub-steps, complete
        StepCollector.StartStep(scenarioId, "Then", "the response should be valid", null, null);
        StepCollector.AddAssertionSubStep(scenarioId, "Response status code should be 200", true);
        StepCollector.AddAssertionSubStep(scenarioId, "Response body should contain items", true);
        StepCollector.CompleteStep(scenarioId, true);

        var info = MakeScenario(scenarioId: scenarioId, steps:
        [
            new ReqNRollStepInfo("Then", "the response should be valid", ScenarioExecutionStatus.OK)
        ]);

        try
        {
            var features = new[] { info }.ToFeatures();
            var step = features[0].Scenarios[0].Steps![0];

            Assert.NotNull(step.SubSteps);
            Assert.Equal(2, step.SubSteps!.Length);
            Assert.Equal("Response status code should be 200", step.SubSteps[0].Text);
            Assert.Equal("Response body should contain items", step.SubSteps[1].Text);
        }
        finally
        {
            StepCollector.ClearSteps(scenarioId);
        }
    }

    [Fact]
    public void ToFeatures_maps_assertion_sub_steps_only_to_matching_step_index()
    {
        var scenarioId = "assertion-index-test-" + Guid.NewGuid();

        // Step 0 (Given): no assertions
        StepCollector.StartStep(scenarioId, "Given", "a request", null, null);
        StepCollector.CompleteStep(scenarioId, true);

        // Step 1 (When): no assertions
        StepCollector.StartStep(scenarioId, "When", "the request is sent", null, null);
        StepCollector.CompleteStep(scenarioId, true);

        // Step 2 (Then): has assertions
        StepCollector.StartStep(scenarioId, "Then", "the response is valid", null, null);
        StepCollector.AddAssertionSubStep(scenarioId, "Status should be OK", true);
        StepCollector.CompleteStep(scenarioId, true);

        var info = MakeScenario(scenarioId: scenarioId, steps:
        [
            new ReqNRollStepInfo("Given", "a request", ScenarioExecutionStatus.OK),
            new ReqNRollStepInfo("When", "the request is sent", ScenarioExecutionStatus.OK),
            new ReqNRollStepInfo("Then", "the response is valid", ScenarioExecutionStatus.OK)
        ]);

        try
        {
            var features = new[] { info }.ToFeatures();
            var steps = features[0].Scenarios[0].Steps!;

            Assert.Null(steps[0].SubSteps);
            Assert.Null(steps[1].SubSteps);
            Assert.NotNull(steps[2].SubSteps);
            Assert.Single(steps[2].SubSteps!);
            Assert.Equal("Status should be OK", steps[2].SubSteps![0].Text);
        }
        finally
        {
            StepCollector.ClearSteps(scenarioId);
        }
    }

    // ─── Step-level attachments via StepCollector ──────────

    [Fact]
    public void ToFeatures_maps_step_level_attachments_from_StepCollector()
    {
        var scenarioId = "attachment-step-test-" + Guid.NewGuid();

        StepCollector.StartStep(scenarioId, "Given", "a request", null, null);
        StepCollector.CompleteStep(scenarioId, true);

        StepCollector.StartStep(scenarioId, "When", "the spec is written to disk", null, null);
        StepCollector.AddAttachment(scenarioId, "/tmp/openapi.json", null);
        StepCollector.CompleteStep(scenarioId, true);

        var info = MakeScenario(scenarioId: scenarioId, steps:
        [
            new ReqNRollStepInfo("Given", "a request", ScenarioExecutionStatus.OK),
            new ReqNRollStepInfo("When", "the spec is written to disk", ScenarioExecutionStatus.OK)
        ]);

        try
        {
            var features = new[] { info }.ToFeatures();
            var steps = features[0].Scenarios[0].Steps!;

            Assert.Null(steps[0].Attachments);
            Assert.NotNull(steps[1].Attachments);
            Assert.Single(steps[1].Attachments!);
            Assert.Equal("openapi.json", steps[1].Attachments![0].Name);
        }
        finally
        {
            StepCollector.ClearSteps(scenarioId);
        }
    }

    [Fact]
    public void ToFeatures_maps_scenario_level_attachments_from_StepCollector()
    {
        var scenarioId = "attachment-scenario-test-" + Guid.NewGuid();

        // Add attachment when no step is active → scenario-level
        StepCollector.AddAttachment(scenarioId, "/tmp/report.pdf", null);

        var info = MakeScenario(scenarioId: scenarioId, steps:
        [
            new ReqNRollStepInfo("Given", "a request", ScenarioExecutionStatus.OK)
        ]);

        try
        {
            var features = new[] { info }.ToFeatures();
            var scenario = features[0].Scenarios[0];

            Assert.NotNull(scenario.Attachments);
            Assert.Single(scenario.Attachments!);
            Assert.Equal("report.pdf", scenario.Attachments![0].Name);
        }
        finally
        {
            StepCollector.ClearSteps(scenarioId);
        }
    }

    private static ReqNRollScenarioInfo MakeOutlineRow(
        string id, string period, int? blockIndex,
        string? blockName = null, string? blockDescription = null)
    {
        return new ReqNRollScenarioInfo
        {
            ScenarioId = id,
            ScenarioTitle = "Movement",
            FeatureTitle = "Market share",
            ScenarioTags = [],
            CombinedTags = [],
            Steps = [],
            ExecutionStatus = ScenarioExecutionStatus.OK,
            OutlineId = "Movement",
            ExampleValues = new Dictionary<string, string> { ["Period"] = period },
            ExamplesBlockName = blockName,
            ExamplesBlockDescription = blockDescription,
            ExamplesBlockIndex = blockIndex
        };
    }

    [Fact]
    public void ToFeatures_copies_examples_block_fields_onto_scenarios()
    {
        var features = new[] { MakeOutlineRow("s1", "OneWeek", 0, "gained share", "a description") }.ToFeatures();
        var scenario = features[0].Scenarios[0];

        Assert.Equal("gained share", scenario.ExamplesBlockName);
        Assert.Equal("a description", scenario.ExamplesBlockDescription);
        Assert.Equal(0, scenario.ExamplesBlockIndex);
    }

    [Fact]
    public void ToFeatures_orders_outline_rows_by_block_index_so_blocks_do_not_interleave()
    {
        // Alphabetical-by-values ordering would interleave: AFourWeeks < BOneWeek < COneYear,
        // but FourWeeks belongs to block 1.
        var infos = new[]
        {
            MakeOutlineRow("s1", "AFourWeeks", 1, "lost share"),
            MakeOutlineRow("s2", "COneYear", 0, "gained share"),
            MakeOutlineRow("s3", "BOneWeek", 0, "gained share")
        };

        var scenarios = infos.ToFeatures()[0].Scenarios;

        Assert.Equal(["s3", "s2", "s1"], scenarios.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void ToFeatures_keeps_alphabetical_value_ordering_for_blockless_rows()
    {
        var infos = new[]
        {
            MakeOutlineRow("s1", "b", null),
            MakeOutlineRow("s2", "a", null)
        };

        var scenarios = infos.ToFeatures()[0].Scenarios;

        Assert.Equal(["s2", "s1"], scenarios.Select(s => s.Id).ToArray());
    }
}
