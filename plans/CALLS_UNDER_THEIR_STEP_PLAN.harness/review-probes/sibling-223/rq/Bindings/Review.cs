using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;

namespace HookSteps.Repro;

public class ReviewDriver(Driver driver)
{
    [Step] public async Task A_slow_inner_check() { await Task.Delay(300); await driver.Post("background"); }
    [Step] public Task A_forwarding_check() => A_slow_inner_check();
    [Step] public void A_sync_wrapper() { _ = A_slow_inner_check(); }
    [Step] public async Task A_failing_inner_check() { await Task.Delay(100); throw new InvalidOperationException("background check failed"); }
    [Step] public Task A_failing_forwarding_check() => A_failing_inner_check();
    [Step] public async Task A_fast_parallel() { await Task.Delay(10); await driver.Post("fast"); }
    [Step] public async Task A_slow_parallel() { await Task.Delay(120); await driver.Post("slow"); }
    [Step] public async Task Cleaning_fails() { await driver.Post("clean"); throw new InvalidOperationException("cleaning failed"); }
}

[Binding]
public class ReviewSteps(Driver driver, ReviewDriver review)
{
    [Given("a forwarding step method is awaited")] public Task Awaited() => review.A_forwarding_check();
    [Given("a forwarding step method is left running")] public Task LeftForward() { _ = review.A_forwarding_check(); return driver.Post("started"); }
    [Given("a sync step method leaves one running")] public Task LeftSync() { review.A_sync_wrapper(); return driver.Post("started"); }

    [Given("a failing forwarding step method is left running")]
    public Task LeftFailing() { _ = review.A_failing_forwarding_check().ContinueWith(_ => { }); return driver.Post("started"); }

    [When("the order takes a while and is then read back")]
    public async Task TakesAWhileThenRead()
    {
        await driver.An_order_is_submitted_slowly();
        await driver.The_order_is_read_back();
    }

    [Given("the run waits a while")] public Task Waits() => Task.Delay(1500);
}

[Binding]
public class ReviewHooks(Driver driver, ReviewDriver review, ITestRunner runner)
{
    [BeforeScenario("r03")] public Task R03() { _ = review.A_slow_inner_check(); return Task.CompletedTask; }
    [BeforeScenario("r04", Order = int.MinValue)] public void R04() => throw new InvalidOperationException("first hook failed");
    [BeforeScenario("r05")] public Task R05() => SeedThroughAStep();
    [Step] public async Task SeedThroughAStep() { await runner.GivenAsync("a registered customer"); await driver.Post("seed-after"); }
    [BeforeScenario("r06")] public Task R06() => Task.WhenAll(review.A_fast_parallel(), review.A_slow_parallel());
    [AfterScenario("r13")] public Task R13() => review.Cleaning_fails();
}
