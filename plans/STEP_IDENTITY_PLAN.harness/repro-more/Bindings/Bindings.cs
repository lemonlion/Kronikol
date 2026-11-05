using Reqnroll;
using Kronikol.Tracking;

namespace HookSteps.More;

public sealed record Transformed(int Value);

[Binding]
public class MoreBindings(Driver driver, ScenarioContext scenario, ITestRunner runner)
{
    private static readonly Driver Shared = new();
    private static Task? _pending;

    [BeforeScenario("seeded")] public Task Seed() => driver.Reference_data_is_seeded();

    [BeforeScenario("seededrow")]
    public async Task SeedRow()
    {
        if ((string?)scenario.ScenarioInfo.Arguments["amount"] == "20")
            await driver.Reference_data_is_seeded();
    }

    [BeforeFeature("lifecycle")] public static Task FeatureSeed() => Shared.Reference_data_is_seeded();
    [AfterFeature("lifecycle")] public static Task FeatureClean() => Shared.Reference_data_is_cleaned();
    [AfterTestRun(Order = int.MaxValue)]
    public static void Report() => Kronikol.ReqNRoll.ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new Kronikol.ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
    });

    [BeforeTestRun] public static Task RunSeed() => Shared.Block_prepared();
    [AfterTestRun] public static Task RunClean() => Shared.Step_observed();

    [BeforeScenario("throwbeforestep")] public void Arm() => scenario["arm"] = true;

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

    [Given("a registered customer")] public Task Customer() => driver.A_customer_is_registered();
    [Given("a step that runs another")] public Task RunsAnother() => runner.GivenAsync("a registered customer");
    [When(@"an order of (\d+) is placed")] public Task Order(int amount) => driver.Post("orders/" + amount);
    [Then("the order is confirmed")] public Task Confirmed() => driver.Post("confirm");
    [When(@"(a transformed value of \d+) is used")] public Task Used(Transformed value) => driver.Post("used/" + value.Value);
    [When("a pending step runs")] public void Pending() => throw new PendingStepException();

    [Given("a call that outlasts its step")] public void Outlasts() => _pending = driver.Post("spans");
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
