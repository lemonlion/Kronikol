using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using TestStack.BDDfy;

namespace Kronikol.Tests.BDDfy;

/// <summary>
/// #224 on BDDfy (plans/STEP_PHASE_PLAN.md 5.5): the executor over a fake inner executor, on this test's own id. A step at the
/// top of a story says its keyword's phase and ends in <c>Unknown</c>, whatever the flow held before the story. Under a step
/// that said a phase, a story is a label, and the step gives back what the flow held.
/// </summary>
[Collection("DiagramsFetcher")]
public class BDDfyStepPhaseTests : IDisposable
{
    private readonly string _id = Xunit.TestContext.Current.Test!.UniqueID;

    public BDDfyStepPhaseTests() => StepCollector.ClearSteps(_id);

    public void Dispose() => StepCollector.ClearSteps(_id);

    private sealed class Inner(Func<object?> body) : IStepExecutor
    {
        public object Execute(Step step, object testObject) => body()!;
    }

    private static Step StepOf(ExecutionOrder order) => new(_ => null!, new StepTitle("a step"), false, order, true, []);

    /// <summary>Runs one BDDfy step: what its body read, with the step open.</summary>
    private static TestPhase Run(ExecutionOrder order)
    {
        var read = TestPhase.Unknown;
        new BDDfyStepTrackingExecutor(new Inner(() =>
        {
            read = TestPhaseContext.Current;
            return null;
        })).Execute(StepOf(order), new object());
        return read;
    }

    private static object Execute(ExecutionOrder order, Func<object?> body) =>
        new BDDfyStepTrackingExecutor(new Inner(body)).Execute(StepOf(order), new object());

    private static async Task Yielding() => await Task.Yield();

    /// <summary>What a flow with nothing of this test's on it reads: what the test's steps decided.</summary>
    private string TestsOwnPhase()
    {
        var phase = TestPhase.Unknown;

        // A thread of its own: a waited Task.Run can run inline on the waiter, under the waiter's flow.
        var thread = new Thread(() =>
        {
            var frame = StepCollector.StartFrame(_id, "AND", "a read", phaseKeyword: "AND");
            phase = TestPhaseContext.Current;
            StepCollector.CompleteFrame(_id, frame, passed: true);
        });
        thread.UnsafeStart();
        thread.Join();
        return phase.ToString();
    }

    // review4/F1: BDDfy waits on the task and only then starts the next step, on the flow that started this one. The frame has closed on the
    // task's own flow, and the flow that handed it back must not read as one that outlived it, or no later step decides.
    [Fact]
    public void An_async_Given_that_has_ended_leaves_the_When_to_decide()
    {
        ((Task)Execute(ExecutionOrder.SetupState, () => Yielding())).GetAwaiter().GetResult();

        Execute(ExecutionOrder.Transition, () => null);

        Assert.Equal("Action", TestsOwnPhase());
    }

    [Fact]
    public void An_async_Given_that_finished_at_once_leaves_the_When_to_decide()
    {
        ((Task)Execute(ExecutionOrder.SetupState, () => Task.CompletedTask)).GetAwaiter().GetResult();

        Execute(ExecutionOrder.Transition, () => null);

        Assert.Equal("Action", TestsOwnPhase());
    }

    [Fact]
    public void An_async_Given_then_an_async_When_decide_each_in_turn()
    {
        ((Task)Execute(ExecutionOrder.SetupState, () => Yielding())).GetAwaiter().GetResult();
        Assert.Equal("Setup", TestsOwnPhase());

        ((Task)Execute(ExecutionOrder.Transition, () => Yielding())).GetAwaiter().GetResult();
        Assert.Equal("Action", TestsOwnPhase());
    }

    // The option is about step attributes: BDDfy's own steps go on saying their word.
    [Fact]
    public void A_step_keeps_its_phase_with_WhenTriggersAction_off()
    {
        var options = StepCollector.Options;
        try
        {
            StepCollector.Options = new StepTrackingOptions { WhenTriggersAction = false };
            Assert.Equal(TestPhase.Action, Run(ExecutionOrder.Transition));
        }
        finally
        {
            StepCollector.Options = options;
        }
    }

    private void WovenStep(string keyword)
    {
        StepCollector.StartStep(_id, keyword, "woven", null, null);
        StepCollector.CompleteStep(_id, passed: true);
    }

    [Theory]
    [InlineData(ExecutionOrder.SetupState, TestPhase.Setup)]
    [InlineData(ExecutionOrder.ConsecutiveSetupState, TestPhase.Setup)]
    [InlineData(ExecutionOrder.Transition, TestPhase.Action)]
    [InlineData(ExecutionOrder.ConsecutiveTransition, TestPhase.Action)]
    [InlineData(ExecutionOrder.Assertion, TestPhase.Action)]
    [InlineData(ExecutionOrder.ConsecutiveAssertion, TestPhase.Action)]
    public void A_step_at_the_top_of_a_story_reads_its_keywords_phase_and_ends_in_Unknown(ExecutionOrder order, TestPhase phase)
    {
        TestPhaseContext.Reset();

        Assert.Equal(phase, Run(order));
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    }

    // B10: a step with no keyword at the top of a story reads Unknown, and a call after the story reads Unknown.
    [Fact]
    public void A_TearDown_step_and_a_call_after_the_story_read_Unknown()
    {
        TestPhaseContext.Reset();

        Run(ExecutionOrder.SetupState);
        Run(ExecutionOrder.Transition);

        Assert.Equal(TestPhase.Unknown, Run(ExecutionOrder.TearDown));
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    }

    // review4/B01, B02, review5/B15 to B17: whatever the flow held before the story.
    [Fact]
    public void A_TearDown_step_reads_Unknown_after_a_woven_step_before_the_story()
    {
        WovenStep("When");
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);

        Assert.Equal(TestPhase.Unknown, Run(ExecutionOrder.TearDown));
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    }

    [Fact]
    public void A_call_after_the_story_reads_Unknown_after_an_explicit_phase_before_the_story()
    {
        DefaultTrackingDiagramOverride.StartSetup(_id);

        Run(ExecutionOrder.Transition);

        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    }

    // review6/B18, B20 to B23 and Q10: a by-name Setup method runs before any step has said anything.
    [Theory]
    [InlineData(TestPhase.Setup)]
    [InlineData(TestPhase.Action)]
    public void A_Setup_method_first_reads_Unknown_whatever_the_flow_held(TestPhase before)
    {
        TestPhaseContext.Current = before;

        Assert.Equal(TestPhase.Unknown, Run(ExecutionOrder.Initialize));
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    }

    // review5/F01: StartStep skips a story with no test id, so the executor keeps its own write.
    [Fact]
    public void A_story_with_no_test_id_writes_its_keywords_phase()
    {
        var read = TestPhase.Unknown;
        var after = TestPhase.Action;

        // A thread of its own, with no flow, so no xUnit test context either: a waited Task.Run can run inline on the waiter.
        var thread = new Thread(() =>
        {
            read = Run(ExecutionOrder.SetupState);
            after = TestPhaseContext.Current;
        });
        thread.UnsafeStart();
        thread.Join();

        Assert.Equal(TestPhase.Setup, read);
        Assert.Equal(TestPhase.Unknown, after);
    }

    // review/Z15: a whole story inside a woven Given.
    [Fact]
    public void A_keyword_under_a_woven_step_is_a_label_and_the_woven_step_goes_on_reading_its_phase()
    {
        StepCollector.StartStep(_id, "Given", "woven", null, null);

        Assert.Equal(TestPhase.Setup, Run(ExecutionOrder.Transition));
        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);

        StepCollector.CompleteStep(_id, passed: true);
        Assert.Contains(StepCollector.GetSteps(_id), step => step.Keyword == "Given");
    }

    [Fact]
    public void A_step_with_no_keyword_under_a_woven_step_gives_back_what_the_flow_held()
    {
        StepCollector.StartStep(_id, "When", "woven", null, null);

        Assert.Equal(TestPhase.Action, Run(ExecutionOrder.TearDown));
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);

        StepCollector.CompleteStep(_id, passed: true);
    }

    // F15, in its BDDfy form: the catch closed the step, and the finally then closed the step above it.
    [Fact]
    public void A_failing_step_inside_a_woven_parent_closes_itself_alone()
    {
        StepCollector.StartStep(_id, "Given", "woven", null, null);
        var failing = new BDDfyStepTrackingExecutor(new Inner(() => throw new InvalidOperationException("it failed")));

        Assert.Throws<InvalidOperationException>(() => failing.Execute(StepOf(ExecutionOrder.Transition), new object()));

        Assert.True(StepCollector.HasActiveStep(_id));
        StepCollector.CompleteStep(_id, passed: true);
        var steps = StepCollector.GetSteps(_id);
        Assert.Equal(ExecutionResult.Passed, steps.Single(step => step.Keyword == "Given").Status);
        Assert.Equal(ExecutionResult.Failed, steps.Single(step => step.Keyword != "Given").Status);
    }

    // review1/D09: a step left open above it must not take the close, or the failing step would never reach the step list.
    [Fact]
    public void A_failing_step_with_a_step_left_open_under_it_reads_Failed_and_keeps_it()
    {
        var failing = new BDDfyStepTrackingExecutor(new Inner(() =>
        {
            StepCollector.StartStep(_id, null, "left open", null, null);
            throw new InvalidOperationException("it failed");
        }));

        Assert.Throws<InvalidOperationException>(() => failing.Execute(StepOf(ExecutionOrder.Transition), new object()));

        var step = StepCollector.GetSteps(_id).Single();
        Assert.Equal(ExecutionResult.Failed, step.Status);
        Assert.Single(step.SubSteps!);
    }

    [Fact]
    public void A_failing_step_at_the_top_of_a_story_closes_once_and_reads_Failed()
    {
        var failing = new BDDfyStepTrackingExecutor(new Inner(() => throw new InvalidOperationException("it failed")));

        Assert.Throws<InvalidOperationException>(() => failing.Execute(StepOf(ExecutionOrder.Transition), new object()));

        Assert.False(StepCollector.HasActiveStep(_id));
        Assert.Equal(ExecutionResult.Failed, StepCollector.GetSteps(_id).Single().Status);
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    }
}
