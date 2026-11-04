using Kronikol.Tracking;
using Reqnroll;
using Shouldly;

namespace Spike.Substeps.Steps;

[Binding]
public class ProbeSteps(HttpClient client, ProbeDriver probe, ScenarioContext scenario)
{
    private static Task? _left;

    [When("two sub-steps run at once")]
    public Task WhenTwoSubStepsRunAtOnce() => Task.WhenAll(probe.The_left_side_runs(), probe.The_right_side_runs());

    [Given("a call is started and left running")]
    public void GivenACallIsStartedAndLeftRunning() =>
        _left = Task.Run(async () =>
        {
            await Task.Delay(150);
            await client.GetAsync("late/started-in-step-0");
        });

    [When("a later step runs")]
    public async Task WhenALaterStepRuns()
    {
        await client.GetAsync("later-step/first");
        await Task.Delay(300);
        await client.GetAsync("later-step/second");
        if (_left is not null) await _left;
        _left = null;
    }

    [Given("a step skips an async sub-step and goes on")]
    public async Task GivenASkippedAsyncSubStep()
    {
        await probe.A_skipped_async_sub_step();
        await client.GetAsync("after-skip/own");
        await probe.A_sub_step_after_the_skip();
    }

    [Given("a step skips a sync sub-step and goes on")]
    public async Task GivenASkippedSyncSubStep()
    {
        probe.A_skipped_sync_sub_step();
        await client.GetAsync("after-sync-skip/own");
        await probe.A_sub_step_after_the_skip();
    }

    [Given("a step with a long label and a call")]
    public Task GivenALongLabel() => client.GetAsync("long-label");

    [Given("a basket that held two apples and a pear last week but nothing at all this week, while the shop's prices rose")]
    public Task GivenALongOwnLabel() => client.GetAsync("long-own-label");

    [Given(@"the shopper uploads C:\\baskets\\monday\.csv")]
    public Task GivenABackslash() => client.GetAsync("backslash");

    [When("two sub-steps run at once and the first fails")]
    public Task WhenTwoSubStepsRunAtOnceAndTheFirstFails() => Task.WhenAll(probe.The_left_side_fails(), probe.The_right_side_runs());

    [Given("a sub-step is started and left running")]
    public void GivenASubStepIsStartedAndLeftRunning() => _left = probe.A_slow_sub_step();

    [When("a sub-step hands its call to another thread")]
    public Task WhenASubStepHandsItsCallToAnotherThread() => probe.A_call_on_another_thread();

    [Given("a shared step whose sub-steps depend on the scenario")]
    public async Task GivenASharedStepWhoseSubStepsDependOnTheScenario()
    {
        if (scenario.ScenarioInfo.Tags.Contains("big"))
        {
            await probe.The_same_sub_step("big-one");
            await probe.The_same_sub_step("big-two");
            await probe.The_same_sub_step("big-three");
        }
        else
            await probe.The_same_sub_step("small-one");
    }

    [Then("the shape is small")]
    [Then("the shape is big")]
    public void ThenTheShapeIs() { }

    [When("a sub-step is still running at an assertion")]
    public async Task WhenASubStepIsStillRunningAtAnAssertion()
    {
        var running = probe.A_slow_sub_step();
        1.ShouldBe(1);
        await running;
    }

    [Given("one sub-step runs twice")]
    public async Task GivenOneSubStepRunsTwice()
    {
        await probe.The_same_sub_step("first");
        await probe.The_same_sub_step("second");
    }

    [Given("a step makes its own calls around a sub-step")]
    public async Task GivenAStepMakesItsOwnCallsAroundASubStep()
    {
        await client.GetAsync("own/before");
        await probe.The_same_sub_step("inner");
        await client.GetAsync("own/after");
    }
}

public class ProbeDriver(HttpClient client)
{
    public bool Off => true;

    [Step(SkipIf = nameof(Off), SkipReason = "off")]
    public async Task A_skipped_async_sub_step() => await client.GetAsync("skipped/async");

    [Step(SkipIf = nameof(Off), SkipReason = "off")]
    public void A_skipped_sync_sub_step() => client.GetAsync("skipped/sync").GetAwaiter().GetResult();

    [Step]
    public async Task A_sub_step_after_the_skip() => await client.GetAsync("after-skip/sub");

    [Step]
    public async Task The_left_side_runs()
    {
        await Task.Delay(20);
        await client.GetAsync("left/one");
        await Task.Delay(80);
        await client.GetAsync("left/two");
    }

    [Step]
    public async Task The_right_side_runs()
    {
        await Task.Delay(60);
        await client.GetAsync("right/one");
        await Task.Delay(80);
        await client.GetAsync("right/two");
    }

    [Step]
    public async Task The_left_side_fails()
    {
        await Task.Delay(20);
        await client.GetAsync("left/before-failing");
        throw new InvalidOperationException("the left side failed");
    }

    [Step]
    public async Task A_call_on_another_thread() =>
        await Task.Run(async () => await client.GetAsync("other-thread"));

    [Step]
    public async Task A_slow_sub_step()
    {
        await Task.Delay(50);
        await client.GetAsync("slow");
    }

    [Step]
    public async Task The_same_sub_step(string which) => await client.GetAsync($"same/{which}");
}
