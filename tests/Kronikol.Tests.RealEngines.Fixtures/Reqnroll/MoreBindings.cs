using Reqnroll;
using Kronikol.Tracking;

namespace EngineFixtures;

public sealed record Transformed(int Value);

[Binding]
public class MoreBindings(Driver driver, ScenarioContext scenario, ITestRunner runner)
{
    private static readonly Driver Shared = new();
    private static Task? _pending;

    [BeforeScenario("seeded")] public Task Seed() => driver.Reference_data_is_seeded();
    [BeforeScenario("seedalice")] public Task SeedAlice() => driver.Seed_for("alice");
    [BeforeScenario("seedbob")] public Task SeedBob() => driver.Seed_for("bob");
    [BeforeScenario("seedfails")] public Task SeedFails() => driver.A_seed_that_fails();
    [BeforeScenario("failhook")] public void FailHook() => throw new InvalidOperationException("the hook failed");
    [BeforeScenario("needsgpu")] public void NeedsGpu(Reqnroll.UnitTestProvider.IUnitTestRuntimeProvider runtime) => runtime.TestIgnore("no GPU here");

    [BeforeScenario("seededrow")]
    public async Task SeedRow()
    {
        if ((string?)scenario.ScenarioInfo.Arguments["amount"] == "20")
            await driver.Reference_data_is_seeded();
    }

    [BeforeFeature("lifecycle")] public static Task FeatureSeed() => Shared.Reference_data_is_seeded();
    [AfterFeature("lifecycle")] public static Task FeatureClean() => Shared.Reference_data_is_cleaned();
    [BeforeTestRun] public static Task RunSeed() => Shared.Block_prepared();
    [AfterTestRun] public static Task RunClean() => Shared.Step_observed();

    [BeforeScenario("throwbeforestep")] public void Arm() => scenario["arm"] = true;

    // Ahead of Kronikol's own BeforeStep, so the When opens no frame.
    [BeforeStep("q4", Order = int.MinValue)]
    public void ThrowBeforeTheWhenOpens()
    {
        if (scenario.StepContext.StepInfo.StepDefinitionType == Reqnroll.Bindings.StepDefinitionType.When)
            throw new InvalidOperationException("BeforeStep hook failed");
    }

    [BeforeStep(Order = 100)]
    public void MaybeThrow()
    {
        if (scenario.ContainsKey("arm") && scenario.StepContext.StepInfo.StepDefinitionType == Reqnroll.Bindings.StepDefinitionType.When)
            throw new InvalidOperationException("BeforeStep hook failed");
    }

    [AfterScenario("lastseed", Order = int.MaxValue)] public Task LastSeed() => driver.Reference_data_is_cleaned();

    [StepArgumentTransformation(@"a transformed value of (\d+)")]
    public Transformed Transform(int value)
    {
        driver.Reference_data_is_seeded().GetAwaiter().GetResult();
        return new Transformed(value);
    }

    [Given("a step that runs another")] public Task RunsAnother() => runner.GivenAsync("a registered customer");

    [Then(@"order (\d+) is read back")] public Task ReadBack(int amount) => driver.Post("orders/" + amount);

    [Given("the shop is ready")]
    public async Task Ready()
    {
        await driver.Post("ready");
        if (scenario.ScenarioInfo.Tags.Contains("failgiven"))
            throw new InvalidOperationException("the shop is not ready");
    }

    [When(@"an order of (\d+) is placed")] public Task Order(int amount) => driver.Post("orders/" + amount);
    [When(@"(a transformed value of \d+) is used")] public Task Used(Transformed value) => driver.Post("used/" + value.Value);
    [When("a pending step runs")] public void Pending() => throw new PendingStepException();

    [Given("a call that outlasts its step")] public void Outlasts() => _pending = driver.Post("spans");
    [When("the payment passes")] public Task Passes() => driver.Post("payments");
    [Then("the call is answered")] public Task Answered() => _pending!;
}

[Binding]
public class ConstructedBindings
{
    private readonly Driver _driver;

    public ConstructedBindings(Driver driver)
    {
        _driver = driver;
        driver.Block_prepared().GetAwaiter().GetResult();
    }

    [Given("a step whose binding constructor runs a step")] public Task Run() => _driver.Post("ctor-step");
}

[Binding]
public class WhatDidNotRunBindings(Driver driver, ScenarioContext scenario, ITestRunner runner)
{
    [BeforeScenario("plainhookthrows")]
    public void PlainHookThrows() => throw new InvalidOperationException("the plain hook failed");

    [When("this step is ambiguous")] public Task AmbiguousOne() => driver.Post("ambiguous-one");

    [When("this step is ambiguous")] public Task AmbiguousTwo() => driver.Post("ambiguous-two");

    [Given("a step that runs an inner step a hook stops, then a step method")]
    public async Task RunsAStoppedStep()
    {
        await runner.GivenAsync("an inner step a hook stops");
        await driver.A_customer_is_registered();
    }

    [Given("an inner step a hook stops")] public Task Stopped() => driver.Post("stopped");

    [BeforeStep("stopsinner", Order = int.MinValue)]
    public void StopInner()
    {
        if (scenario.StepContext.StepInfo.Text == "an inner step a hook stops")
            throw new InvalidOperationException("the hook stopped it");
    }
}
