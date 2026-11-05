using Reqnroll;

namespace EngineFixtures;

[Binding]
public class FileBindings(Driver driver, ScenarioContext scenario, IReqnrollOutputHelper output)
{
    private void Attach(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name + "-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        output.AddAttachment(path);
    }

    [BeforeScenario("seeded")] public Task Seed() => driver.Reference_data_is_seeded();
    [BeforeScenario("seededwithfile")] public Task SeedWithFile() => A_seed_with_a_file();
    [BeforeScenario("needsgpu")] public void NeedsGpu(Reqnroll.UnitTestProvider.IUnitTestRuntimeProvider runtime) => runtime.TestIgnore("no GPU here");

    [Kronikol.Tracking.Step]
    public async Task A_seed_with_a_file()
    {
        await driver.Reference_data_is_seeded();
        Attach("file");
    }

    [AfterStep("afterstepfile")] public void FileAfterEveryStep() => Attach("file");

    [AfterStep("screenshotonfailure")]
    public void ScreenshotOnFailure()
    {
        if (scenario.TestError is not null)
            Attach("shot");
    }

    [AfterStep("attachthenthrow")]
    public void AttachThenThrow()
    {
        if (!scenario.ScenarioInfo.Tags.Contains("armed"))
            return;
        Attach("shot");
        throw new InvalidOperationException("after step hook failed");
    }

    [AfterTestRun(Order = int.MaxValue)]
    public static void Report() => Kronikol.ReqNRoll.ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new Kronikol.ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
    });

    [Given("a registered customer")] public Task Customer() => driver.A_customer_is_registered();

    [Given("a customer with a screenshot")]
    public async Task CustomerWithFile()
    {
        await driver.A_customer_is_registered();
        Attach("file");
    }

    [Given("a customer that fails when tagged")]
    public async Task CustomerFailsWhenTagged()
    {
        await driver.A_customer_is_registered();
        if (scenario.ScenarioInfo.Tags.Contains("bgfails"))
            throw new InvalidOperationException("background failed");
    }

    [StepDefinition(@"an order of (\d+) is placed")] public Task Order(int amount) => driver.Post("orders/" + amount);
    [Then("the order is confirmed")] public Task Confirmed() => driver.Post("confirm");
}
