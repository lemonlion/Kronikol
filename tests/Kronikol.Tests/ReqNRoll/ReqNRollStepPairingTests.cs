using Reqnroll;
using Kronikol.ReqNRoll;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.ReqNRoll;

/// <summary>The Reqnroll adapter's half of the pairing: a Gherkin step on the frame its hooks opened, a hand-built one by position.</summary>
[Collection("DiagramsFetcher")]
public class ReqNRollStepPairingTests
{
    private static ScenarioStep[]? Rows(string id, params ReqNRollStepInfo[] steps) =>
        new[]
        {
            new ReqNRollScenarioInfo
            {
                ScenarioId = id,
                ScenarioTitle = "Scenario",
                FeatureTitle = "Feature",
                ScenarioTags = [],
                CombinedTags = [],
                Steps = [..steps],
                ExecutionStatus = ScenarioExecutionStatus.OK,
            }
        }.ToFeatures()[0].Scenarios[0].Steps;

    private static string NewTest() => "pairing-" + Guid.NewGuid();

    [Fact]
    public void A_Gherkin_step_takes_the_frame_its_hooks_opened_and_its_row_carries_the_id()
    {
        var test = NewTest();
        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.AddAssertionSubStep(test, "an item", passed: true);
        StepCollector.CompleteFrame(test, frame, passed: true);

        var row = Assert.Single(Rows(test, new ReqNRollStepInfo("Given", "a basket") { FrameId = frame })!);

        Assert.Equal(frame, row.CollectedId);
        Assert.Equal("Given", row.Keyword);
        Assert.Equal("an item", Assert.Single(row.SubSteps!).Text);
        Assert.False(row.OutsideFrameworkStep);
        Assert.False(row.DidNotRun);
    }

    [Fact]
    public void A_step_method_no_Gherkin_step_opened_stands_as_itself_with_its_mark()
    {
        var test = NewTest();
        StepCollector.StartStep(test, null, "Reference data is seeded", null, null);
        StepCollector.CompleteStep(test, passed: true);
        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.CompleteFrame(test, frame, passed: true);

        var rows = Rows(test, new ReqNRollStepInfo("Given", "a basket") { FrameId = frame })!;

        Assert.Equal(["Reference data is seeded", "a basket"], rows.Select(r => r.Text));
        Assert.True(rows[0].OutsideFrameworkStep);
        Assert.NotNull(rows[0].CollectedId);
        Assert.Null(rows[0].Keyword);
        Assert.False(rows[1].OutsideFrameworkStep);
    }

    [Fact]
    public void Steps_built_by_hand_pair_by_position_and_their_rows_carry_no_id()
    {
        // A position is a guess, and an id would make a stamped call certain of it (H1).
        var test = NewTest();
        StepCollector.StartStep(test, "Given", "the first", null, null);
        StepCollector.AddAssertionSubStep(test, "under the first", passed: true);
        StepCollector.CompleteStep(test, passed: true);
        StepCollector.StartStep(test, "When", "the second", null, null);
        StepCollector.AddAssertionSubStep(test, "under the second", passed: true);
        StepCollector.CompleteStep(test, passed: true);

        var rows = Rows(test, new ReqNRollStepInfo("Given", "a hand-built one"), new ReqNRollStepInfo("When", "and another"))!;

        Assert.Equal(["a hand-built one", "and another"], rows.Select(r => r.Text));
        Assert.Equal("under the second", Assert.Single(rows[1].SubSteps!).Text);
        Assert.All(rows, row => Assert.Null(row.CollectedId));
        Assert.All(rows, row => Assert.False(row.OutsideFrameworkStep));
    }

    [Fact]
    public void A_scenario_with_no_step_of_its_own_lists_what_was_collected()
    {
        // A scenario whose only step is a hook's, and T05: a failing hook step leaves no Gherkin step run (H2).
        var test = NewTest();
        StepCollector.StartStep(test, null, "Seeding fails", null, null);
        StepCollector.CompleteStep(test, passed: false, "seeding failed");

        var row = Assert.Single(Rows(test)!);

        Assert.Equal("Seeding fails", row.Text);
        Assert.Equal(ExecutionResult.Failed, row.Status);
        Assert.True(row.OutsideFrameworkStep);
    }

    [Fact]
    public void A_scenario_where_nothing_ran_lists_no_steps() => Assert.Null(Rows(NewTest()));

    [Fact]
    public void A_nested_Gherkin_step_carries_what_the_framework_knows_about_it()
    {
        var test = NewTest();
        var outer = StepCollector.StartFrame(test, "Given", "a step that runs another");
        var inner = StepCollector.StartFrame(test, "Given", "the basket is listed");
        StepCollector.CompleteFrame(test, inner, passed: false);
        StepCollector.CompleteFrame(test, outer, passed: false);

        var rows = Rows(test,
            new ReqNRollStepInfo("Given", "the basket is listed", ScenarioExecutionStatus.TestError, DocString: "a doc string") { FrameId = inner },
            new ReqNRollStepInfo("Given", "a step that runs another", ScenarioExecutionStatus.TestError) { FrameId = outer })!;

        var row = Assert.Single(rows);
        Assert.Equal("a step that runs another", row.Text);
        Assert.Equal(ExecutionResult.Failed, row.Status);
        var nested = Assert.Single(row.SubSteps!);
        Assert.Equal("the basket is listed", nested.Text);
        Assert.Equal("a doc string", nested.DocString);
        Assert.Equal(ExecutionResult.Failed, nested.Status);
        Assert.Equal(inner, nested.CollectedId);
    }

    [Fact]
    public void A_hook_steps_failure_does_not_take_the_row_of_a_Gherkin_step_that_never_ran()
    {
        var test = NewTest();
        StepCollector.StartStep(test, null, "Seeding fails", null, null);
        StepCollector.CompleteStep(test, passed: false, "seeding failed");
        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.CompleteFrame(test, frame, passed: true);

        var rows = Rows(test, new ReqNRollStepInfo("Given", "a basket") { FrameId = frame })!;

        Assert.Equal(ExecutionResult.Failed, rows[0].Status);
        Assert.Equal(ExecutionResult.Passed, rows[1].Status);
    }

    [Fact]
    public void A_step_the_hooks_listed_with_no_frame_takes_no_step_methods_row()
    {
        // A suite's BeforeStep hook sorted ahead of ours and threw, so the Gherkin step has no frame, and a hook's step method ran before it.
        var id = "rq-no-frame-" + Guid.NewGuid();
        StepCollector.StartStep(id, null, "Reference data is seeded", null, null);
        StepCollector.CompleteStep(id, true);
        var scenario = new ReqNRollScenarioInfo
        {
            ScenarioId = id,
            ScenarioTitle = "A",
            FeatureTitle = "Orders",
            ScenarioTags = [],
            CombinedTags = [],
            Steps = [new ReqNRollStepInfo("Given", "a basket", ScenarioExecutionStatus.TestError) { NotRunAfter = StepCollector.LastStepIdOf(id) }],
            WrittenBackground = [],
            ExecutionStatus = ScenarioExecutionStatus.TestError,
        };

        var steps = new[] { scenario }.ToFeatures()[0].Scenarios[0].Steps!;

        Assert.Equal(["Reference data is seeded", "a basket"], steps.Select(s => s.Text));
        Assert.True(steps[0].OutsideFrameworkStep);
        Assert.Equal(ExecutionResult.Passed, steps[0].Status);
        Assert.True(steps[1].DidNotRun);
        Assert.Equal(ExecutionResult.Failed, steps[1].Status);
    }

    private static ScenarioStep[]? MappedFrom(string id, params ReqNRollStepInfo[] steps) =>
        new[]
        {
            new ReqNRollScenarioInfo
            {
                ScenarioId = id,
                ScenarioTitle = "A",
                FeatureTitle = "Orders",
                ScenarioTags = [],
                CombinedTags = [],
                Steps = [.. steps],
                WrittenBackground = [],
                ExecutionStatus = ScenarioExecutionStatus.OK,
            },
        }.ToFeatures()[0].Scenarios[0].Steps;

    private static (ReqNRollStepInfo First, ReqNRollStepInfo Second) TwoStepsThatRan(string id)
    {
        var first = StepCollector.StartFrame(id, "Given", "a basket");
        StepCollector.AddAssertionSubStep(id, "under the first", passed: true);
        StepCollector.CompleteFrame(id, first, true);
        var second = StepCollector.StartFrame(id, "When", "the customer pays");
        StepCollector.AddAssertionSubStep(id, "under the second", passed: true);
        StepCollector.CompleteFrame(id, second, true);
        return (new ReqNRollStepInfo("Given", "a basket") { FrameId = first }, new ReqNRollStepInfo("When", "the customer pays") { FrameId = second });
    }

    [Fact]
    public void A_row_rebuilt_by_hand_among_the_hooks_rows_leaves_the_list_paired_by_position()
    {
        // A consumer masks a step's text through the public record: nothing comes back, and nothing doubles.
        var id = "rq-rebuilt-" + Guid.NewGuid();
        var (first, _) = TwoStepsThatRan(id);

        var steps = MappedFrom(id, first, new ReqNRollStepInfo("When", "the customer pays ***"))!;

        Assert.Equal(["a basket", "the customer pays ***"], steps.Select(s => s.Text));
        Assert.Equal("under the second", Assert.Single(steps[1].SubSteps!).Text);
        Assert.All(steps, step => Assert.Null(step.CollectedId));
    }

    [Fact]
    public void A_row_rebuilt_with_a_with_expression_keeps_its_frame()
    {
        var id = "rq-with-" + Guid.NewGuid();
        var (first, second) = TwoStepsThatRan(id);

        var steps = MappedFrom(id, first, second with { Text = "the customer pays ***" })!;

        Assert.Equal(["a basket", "the customer pays ***"], steps.Select(s => s.Text));
        Assert.All(steps, step => Assert.NotNull(step.CollectedId));
    }

    [Fact]
    public void A_row_a_consumer_removed_does_not_come_back()
    {
        var id = "rq-removed-" + Guid.NewGuid();
        var (first, _) = TwoStepsThatRan(id);

        var step = Assert.Single(MappedFrom(id, first)!);

        Assert.Equal("a basket", step.Text);
    }

    [Fact]
    public void A_row_that_ran_is_not_read_as_a_row_that_did_not_when_its_steps_were_cleared()
    {
        // The scenario goes back to the bars, as a row with no id closes the gate, and its calls keep their steps.
        var id = "rq-cleared-" + Guid.NewGuid();
        var (first, second) = TwoStepsThatRan(id);
        StepCollector.ClearSteps(id);

        var steps = MappedFrom(id, first, second)!;

        Assert.All(steps, step => Assert.False(step.DidNotRun));
        Assert.All(steps, step => Assert.Null(step.CollectedId));
    }

    // What a Gherkin step ran through ITestRunner is under it in what was collected, so a rebuilt row beside it leaves it no place of its own (Q3).
    [Fact]
    public void A_row_rebuilt_beside_a_step_that_ran_another_pairs_the_top_level_rows_by_position()
    {
        var test = NewTest();
        var outer = StepCollector.StartFrame(test, "Given", "a step that runs another");
        var inner = StepCollector.StartFrame(test, "Given", "a registered customer");
        StepCollector.AddAssertionSubStep(test, "a customer", passed: true);
        StepCollector.CompleteFrame(test, inner, passed: true);
        StepCollector.CompleteFrame(test, outer, passed: true);
        var when = StepCollector.StartFrame(test, "When", "the order is placed");
        StepCollector.AddAssertionSubStep(test, "an order", passed: true);
        StepCollector.CompleteFrame(test, when, passed: true);
        var then = StepCollector.StartFrame(test, "Then", "it is confirmed");
        StepCollector.AddAssertionSubStep(test, "a confirmation", passed: true);
        StepCollector.CompleteFrame(test, then, passed: true);

        var rows = Rows(test,
            new ReqNRollStepInfo("Given", "a step that runs another") { FrameId = outer },
            new ReqNRollStepInfo("Given", "a registered customer", TableText: "| sku |\n| APPLE |\n") { FrameId = inner },
            new ReqNRollStepInfo("When", "the order is placed") { FrameId = when },
            new ReqNRollStepInfo("Then", "it is ***"))!;

        Assert.Equal(["a step that runs another", "the order is placed", "it is ***"], rows.Select(r => r.Text));
        var nested = Assert.Single(rows[0].SubSteps!);
        Assert.Equal("a registered customer", nested.Text);
        Assert.Equal("table", Assert.Single(nested.Parameters!).Name);
        Assert.Equal("an order", Assert.Single(rows[1].SubSteps!).Text);
        Assert.Equal("a confirmation", Assert.Single(rows[2].SubSteps!).Text);
        Assert.All(rows, r => Assert.Null(r.CollectedId));
    }

    [Fact]
    public void Rows_that_read_alike_are_each_listed_by_position()
    {
        var rows = Rows(NewTest(),
            new ReqNRollStepInfo("When", "I click next"),
            new ReqNRollStepInfo("When", "I click next"),
            new ReqNRollStepInfo("Then", "I am on the last page"))!;

        Assert.Equal(["I click next", "I click next", "I am on the last page"], rows.Select(r => r.Text));
    }

    [Fact]
    public void Rows_that_read_alike_keep_their_own_sub_steps_by_position()
    {
        var test = NewTest();
        foreach (var inner in new[] { "first refresh", "second refresh", "shown" })
        {
            var frame = StepCollector.StartFrame(test, "When", "a step");
            StepCollector.AddAssertionSubStep(test, inner, passed: true);
            StepCollector.CompleteFrame(test, frame, passed: true);
        }

        var rows = Rows(test,
            new ReqNRollStepInfo("When", "***"),
            new ReqNRollStepInfo("When", "***"),
            new ReqNRollStepInfo("Then", "it shows"))!;

        Assert.Equal(["first refresh", "second refresh", "shown"], rows.Select(r => Assert.Single(r.SubSteps!).Text));
    }
}
