using Reqnroll;

namespace EngineFixtures;

[Binding]
public class StepsTwo(Driver driver, ITestRunner runner, ScenarioContext scenario)
{
    [Given("a step that runs an inner step whose hook throws, then a step method")]
    public async Task TieInner()
    {
        await runner.GivenAsync("an inner step a hook stops");
        await driver.Late_after_skip();
    }

    [Given("an inner step a hook stops")] public Task Stopped() => driver.Post("stopped");

    [BeforeStep("tieinner", Order = int.MinValue)]
    public void StopInner()
    {
        if (scenario.StepContext.StepInfo.Text == "an inner step a hook stops")
            throw new InvalidOperationException("the hook stopped it");
    }

    [BeforeScenarioBlock("blockstep")] public Task BlockStep() => driver.Seed();

    [Given("a step that runs the inner step an AfterStep hook throws on")]
    public async Task InnerAfterStep()
    {
        await runner.GivenAsync("the inner step with a bad after hook");
        await runner.GivenAsync("a registered customer");
    }

    [Given("the inner step with a bad after hook")] public Task BadAfter() => driver.Post("bad-after");

    [AfterStep("afterstepinnerthrows")]
    public void InnerAfterStepThrows()
    {
        if (scenario.StepContext.StepInfo.Text == "the inner step with a bad after hook")
            throw new InvalidOperationException("after step hook threw on the inner step");
    }

    [AfterScenario("lastrunsstep", Order = int.MaxValue)] public Task LastRuns() => runner.GivenAsync("a registered customer");

    [BeforeScenario("needsgpu")] public void NeedsGpu(Reqnroll.UnitTestProvider.IUnitTestRuntimeProvider runtime) => runtime.TestIgnore("no GPU here");

    // A Gherkin step let go in one scenario, arriving in the next on the same test thread
    [Given("a step that leaves a Gherkin step to run later")]
    public Task Strays()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            await runner.GivenAsync("the stray step");
        });
        return Task.CompletedTask;
    }

    [Given("the stray step")] public Task Stray() => driver.Post("stray");
    [AfterScenario("waitsinafter")] public Task Waits() => Task.Delay(700);
}
