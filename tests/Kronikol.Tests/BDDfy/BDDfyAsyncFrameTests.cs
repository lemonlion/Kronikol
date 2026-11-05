using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;

namespace Kronikol.Tests.BDDfy;

/// <summary>
/// BDDfy's async runner waits on what the executor returns, so an async step stays open until its task ends, with what it opens after its first await under
/// it (plans/STEP_IDENTITY_PLAN.md 5.1, #122's closing half). The executor over a fake inner executor, on this test's own id; the real one is in Kronikol.Tests.RealEngines.
/// </summary>
[Collection("DiagramsFetcher")]
public class BDDfyAsyncFrameTests : IDisposable
{
    private readonly string _id = Xunit.TestContext.Current.Test!.UniqueID;

    public BDDfyAsyncFrameTests() => StepCollector.ClearSteps(_id);

    public void Dispose() => StepCollector.ClearSteps(_id);

    private sealed class Inner(Func<object?> body) : IStepExecutor
    {
        public object Execute(Step step, object testObject) => body()!;
    }

    private static object Execute(Func<object?> body) =>
        new BDDfyStepTrackingExecutor(new Inner(body)).Execute(new Step(_ => null!, new StepTitle("a step"), false, ExecutionOrder.Transition, true, []), new object());

    private static Task ExecuteAsync(Func<Task> body) => Assert.IsAssignableFrom<Task>(Execute(() => body()));

    [Fact]
    public async Task An_async_step_stays_open_until_its_task_ends()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var returned = ExecuteAsync(async () => await gate.Task);

        Assert.True(StepCollector.HasActiveStep(_id));
        Assert.Empty(StepCollector.GetSteps(_id));

        gate.SetResult();
        await returned;

        Assert.False(StepCollector.HasActiveStep(_id));
        Assert.Equal(ExecutionResult.Passed, Assert.Single(StepCollector.GetSteps(_id)).Status);
    }

    [Fact]
    public async Task What_an_async_step_opens_after_its_first_await_is_under_it()
    {
        await ExecuteAsync(async () =>
        {
            await Task.Yield();
            StepCollector.AddAssertionSubStep(_id, "the total is 3", passed: true);
        });

        Assert.Equal("the total is 3", Assert.Single(Assert.Single(StepCollector.GetSteps(_id)).SubSteps!).Text);
    }

    [Fact]
    public async Task A_faulted_task_fails_the_step_and_still_throws()
    {
        var returned = ExecuteAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("no");
        });

        Assert.Equal("no", (await Assert.ThrowsAsync<InvalidOperationException>(() => returned)).Message);
        Assert.Equal(ExecutionResult.Failed, Assert.Single(StepCollector.GetSteps(_id)).Status);
    }

    [Fact]
    public void A_step_that_returns_no_task_closes_as_it_returns()
    {
        Execute(() => null);

        Assert.False(StepCollector.HasActiveStep(_id));
        Assert.Single(StepCollector.GetSteps(_id));
    }

    // With async void support off BDDfy waits on nothing, so the next step would start with this one open.
    [Fact]
    public async Task With_async_void_support_off_an_async_step_closes_as_it_returns()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var was = Configurator.AsyncVoidSupportEnabled;
        Configurator.AsyncVoidSupportEnabled = false;
        try
        {
            var returned = ExecuteAsync(async () => await gate.Task);

            Assert.False(StepCollector.HasActiveStep(_id));
            gate.SetResult();
            await returned;
        }
        finally
        {
            Configurator.AsyncVoidSupportEnabled = was;
        }
    }

    [Fact]
    public void A_step_left_running_by_an_executor_around_this_one_is_over_when_the_next_step_starts()
    {
        var gate = new TaskCompletionSource();
        _ = ExecuteAsync(async () => await gate.Task);

        Execute(() => null);

        var steps = StepCollector.GetSteps(_id);
        Assert.Equal(2, steps.Length);
        Assert.Empty(steps[0].SubSteps ?? []);
        gate.SetResult();
    }

    [Fact]
    public async Task A_story_run_inside_an_async_step_stays_under_it_after_an_await()
    {
        await ExecuteAsync(async () =>
        {
            await Task.Yield();
            Execute(() => null);
        });

        Assert.Single(Assert.Single(StepCollector.GetSteps(_id)).SubSteps!);
    }

    [Fact]
    public async Task An_async_story_inside_an_async_step_nests_and_the_next_step_stands_beside_them()
    {
        await ExecuteAsync(async () =>
        {
            await Task.Yield();
            await ExecuteAsync(async () => await Task.Yield());
            Execute(() => null);
        });
        Execute(() => null);

        var steps = StepCollector.GetSteps(_id);
        Assert.Equal(2, steps.Length);
        Assert.Equal(2, steps[0].SubSteps!.Length);
    }

    [Fact]
    public async Task A_step_still_running_when_the_steps_are_read_keeps_what_it_opened_and_its_late_end_changes_nothing()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = ExecuteAsync(async () =>
        {
            StepCollector.AddAssertionSubStep(_id, "inner", passed: true);
            await gate.Task;
            throw new InvalidOperationException("too late");
        });
        Assert.Empty(StepCollector.GetSteps(_id));

        BDDfyStepTrackingExecutor.CloseLeftRunning(_id);

        var step = Assert.Single(StepCollector.GetSteps(_id));
        Assert.Equal("inner", Assert.Single(step.SubSteps!).Text);
        Assert.Equal(ExecutionResult.Passed, step.Status);

        gate.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => returned);
        Assert.Equal(ExecutionResult.Passed, Assert.Single(StepCollector.GetSteps(_id)).Status);
    }

    [Fact]
    public async Task A_step_two_stories_down_keeps_both_open()
    {
        await ExecuteAsync(async () =>
        {
            await Task.Yield();
            await ExecuteAsync(async () =>
            {
                await Task.Yield();
                Execute(() => null);
            });
        });

        var outer = Assert.Single(StepCollector.GetSteps(_id));
        Assert.Single(Assert.Single(outer.SubSteps!).SubSteps!);
    }

    [Fact]
    public void A_step_still_running_when_the_report_reads_the_scenario_keeps_what_it_opened()
    {
        var gate = new TaskCompletionSource();
        _ = ExecuteAsync(async () =>
        {
            StepCollector.AddAssertionSubStep(_id, "inner", passed: true);
            await gate.Task;
        });

        var scenario = new[]
        {
            new BDDfyScenarioInfo { TestId = _id, StoryTitle = "Story", ScenarioTitle = "Scenario", Tags = [], Steps = [], Result = TestStack.BDDfy.Result.Failed }
        }.ToFeatures()[0].Scenarios[0];

        Assert.Equal("inner", Assert.Single(Assert.Single(scenario.Steps!).SubSteps!).Text);
        gate.SetResult();
    }
}
