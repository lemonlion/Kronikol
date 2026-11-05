using Reqnroll;
using Kronikol.Tracking;

namespace Rq5;

[Binding]
public class Rq5Bindings(Driver driver, ScenarioContext scenario, ITestRunner runner)
{
    private static readonly Driver Shared = new();

    [BeforeScenario("seeded")] public Task Seed() => driver.Reference_data_is_seeded();
    [BeforeScenario("tenanted")] public Task Tenant() => driver.A_tenant_exists();
    [BeforeScenario("seedfails")] public Task SeedFails() => driver.Seeding_fails();
    [AfterScenario("cleaned")] public Task Clean() => driver.Reference_data_is_cleaned();
    [AfterScenario("cleanfails")] public Task CleanFails() => driver.Cleaning_fails();
    [BeforeScenarioBlock("blockhook")] public Task Block() => driver.Block_prepared();
    [AfterScenario("hookthrows", Order = 50)] public void Throws() => throw new InvalidOperationException("suite hook threw");
    [AfterFeature("stale")] public static Task FeatureEnd() => Shared.Reference_data_is_cleaned();

    [AfterTestRun(Order = int.MaxValue)]
    public static void Report() => Kronikol.ReqNRoll.ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new Kronikol.ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
        GenerateMergeableData = Environment.GetEnvironmentVariable("R5_MERGEABLE") == "1",
        TestRunReportDataFormat = Environment.GetEnvironmentVariable("R5_FORMAT") switch { "xml" => Kronikol.Reports.DataFormat.Xml, "yaml" => Kronikol.Reports.DataFormat.Yaml, _ => Kronikol.Reports.DataFormat.Json },
    });

    [Given("a registered customer")] public Task Customer() => driver.A_customer_is_registered();
    [Given("a second fixture")] public Task Fixture2() => driver.Post("fixture2");
    [Given("a customer that fails when tagged")]
    public async Task CustomerFailsWhenTagged()
    {
        await driver.Post("customers");
        if (scenario.ScenarioInfo.Tags.Contains("bgfails")) throw new InvalidOperationException("background failed");
    }
    [StepDefinition(@"an order of (\d+) is placed")] public Task Order(int amount) => driver.Post("orders/" + amount);
    [Then("the order is confirmed")] public Task Confirmed() => driver.Post("confirm");
    [Then("the order fails")] public async Task Fails() { await driver.Post("confirmfail"); throw new InvalidOperationException("order failed"); }
    [Given("a step with a table:")] public Task Table(DataTable table) => driver.Post("table/" + table.Rows.Count);
    [StepDefinition(@"the amount (\d+) is checked")] public async Task Checked(int amount) { await driver.Post("check/" + amount); if (amount == 13) throw new InvalidOperationException("unlucky"); }
}
