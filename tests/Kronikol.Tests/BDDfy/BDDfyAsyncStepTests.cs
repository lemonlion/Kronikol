using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;

namespace Kronikol.Tests.BDDfy;

/// <summary>
/// #224 and the step-closing half of #122 (plans/STEP_PHASE_PLAN.md 5.7): BDDfy's async runner waits on what the executor returns,
/// so an async step stays open until its task ends. Before this its <c>finally</c> closed it at its first <c>await</c>, so what ran
/// after that had no parent to be under. The executor over a fake inner executor, on this test's own id.
/// </summary>
[Collection("DiagramsFetcher")]
public class BDDfyAsyncStepTests : IDisposable
{
    private readonly string _id = Xunit.TestContext.Current.Test!.UniqueID;

    public BDDfyAsyncStepTests() => StepCollector.ClearSteps(_id);

    public void Dispose() => StepCollector.ClearSteps(_id);

    private sealed class Inner(Func<object?> body) : IStepExecutor
    {
        public object Execute(Step step, object testObject) => body()!;
    }

    private static Step StepOf(ExecutionOrder order) => new(_ => null!, new StepTitle("a step"), false, order, true, []);

    private static object Execute(ExecutionOrder order, Func<object?> body) =>
        new BDDfyStepTrackingExecutor(new Inner(body)).Execute(StepOf(order), new object());

    /// <summary>An async step, whose method returns its task at its first await.</summary>
    private static Task ExecuteAsync(ExecutionOrder order, Func<Task> body) =>
        Assert.IsAssignableFrom<Task>(Execute(order, () => body()));

    [Fact]
    public async Task An_async_step_stays_open_until_its_task_ends()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var returned = ExecuteAsync(ExecutionOrder.Transition, async () => await gate.Task);

        Assert.True(StepCollector.HasActiveStep(_id));
        Assert.Null(Assert.Single(StepCollector.GetSteps(_id)).Status);

        gate.SetResult();
        await returned;

        Assert.False(StepCollector.HasActiveStep(_id));
        Assert.Equal(ExecutionResult.Passed, Assert.Single(StepCollector.GetSteps(_id)).Status);
    }

    // #122's own: "assertions made after the await".
    [Fact]
    public async Task An_assertion_after_the_first_await_lands_under_the_step()
    {
        var returned = ExecuteAsync(ExecutionOrder.Assertion, async () =>
        {
            await Task.Yield();
            StepCollector.AddAssertionSubStep(_id, "the total is 3", passed: true);
        });

        await returned;

        var step = Assert.Single(StepCollector.GetSteps(_id));
        Assert.Equal("the total is 3", Assert.Single(step.SubSteps!).Text);
    }

    // B04: the woven step is a sub-step, so the rule covers it.
    [Fact]
    public async Task A_woven_step_after_the_first_await_is_a_sub_step_and_a_label()
    {
        var read = TestPhase.Unknown;
        var after = TestPhase.Unknown;

        var returned = ExecuteAsync(ExecutionOrder.Transition, async () =>
        {
            await Task.Yield();
            StepCollector.StartStep(_id, "Given", "woven", null, null);
            read = TestPhaseContext.Current;
            StepCollector.CompleteStep(_id, passed: true);
            after = TestPhaseContext.Current;
        });

        await returned;

        Assert.Equal(TestPhase.Action, read);
        Assert.Equal(TestPhase.Action, after);
        var when = Assert.Single(StepCollector.GetSteps(_id));
        Assert.Equal("When", when.Keyword);
        Assert.Equal("Given", Assert.Single(when.SubSteps!).Keyword);
    }

    [Fact]
    public async Task A_faulted_task_fails_the_step_and_still_throws()
    {
        var returned = ExecuteAsync(ExecutionOrder.Transition, async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("it failed");
        });

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => returned);

        Assert.Equal("it failed", thrown.Message);
        Assert.False(StepCollector.HasActiveStep(_id));
        Assert.Equal(ExecutionResult.Failed, Assert.Single(StepCollector.GetSteps(_id)).Status);
    }

    [Fact]
    public async Task A_step_left_running_in_its_own_flow_does_not_sit_above_the_next_step_of_the_story()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var given = ExecuteAsync(ExecutionOrder.SetupState, async () => await gate.Task);

        // The executor's flow left it as it returned its task, so a next step decides for itself.
        var when = TestPhase.Unknown;
        Execute(ExecutionOrder.Transition, () =>
        {
            when = TestPhaseContext.Current;
            return null;
        });
        Assert.Equal(TestPhase.Action, when);

        gate.SetResult();
        await given;
    }

    [Fact]
    public void A_sync_step_closes_as_before()
    {
        var result = Execute(ExecutionOrder.Transition, () => "a value");

        Assert.Equal("a value", result);
        Assert.False(StepCollector.HasActiveStep(_id));
        Assert.Equal(ExecutionResult.Passed, Assert.Single(StepCollector.GetSteps(_id)).Status);
    }

    // BDDfy waits on nothing with this off, so an async step would still be open when the next starts (B11).
    [Fact]
    public async Task With_AsyncVoidSupportEnabled_off_an_async_step_closes_at_its_first_await_as_it_did()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = Configurator.AsyncVoidSupportEnabled;
        Task inner = Task.CompletedTask;
        try
        {
            Configurator.AsyncVoidSupportEnabled = false;

            var returned = Execute(ExecutionOrder.Transition, () => inner = Waiting());
            Assert.Same(inner, returned);
            Assert.False(StepCollector.HasActiveStep(_id));
            Assert.Single(StepCollector.GetSteps(_id));
        }
        finally
        {
            Configurator.AsyncVoidSupportEnabled = original;
            gate.SetResult();
            await inner;
        }

        async Task Waiting() => await gate.Task;
    }

    [Fact]
    public async Task A_story_with_no_test_id_hands_back_a_task_that_ends_as_its_step_does()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? returned = null;

        // A thread of its own, with no flow, so no xUnit test context.
        var thread = new Thread(() => returned = Assert.IsAssignableFrom<Task>(Execute(ExecutionOrder.Transition, () => gate.Task)));
        thread.UnsafeStart();
        thread.Join();

        Assert.False(returned!.IsCompleted);
        gate.SetException(new InvalidOperationException("the step's"));
        Assert.Equal("the step's", (await Assert.ThrowsAsync<InvalidOperationException>(() => returned)).Message);
    }
}
