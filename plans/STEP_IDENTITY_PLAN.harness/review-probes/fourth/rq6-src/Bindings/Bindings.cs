using Reqnroll;
using Kronikol.Tracking;

namespace Rq6;

[Binding]
public class Rq6Bindings(Driver driver, ScenarioContext scenario, IReqnrollOutputHelper output)
{
    [BeforeScenario("seeded")] public Task Seed() => driver.Reference_data_is_seeded();

    [AfterStep("afterstepfile")]
    public void FileAfterEveryStep()
    {
        var path = Path.Combine(Path.GetTempPath(), "rq6-file-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        output.AddAttachment(path);
    }

    [AfterStep("screenshotonfailure")]
    public void ScreenshotOnFailure()
    {
        if (scenario.TestError is null) return;
        var path = Path.Combine(Path.GetTempPath(), "rq6-shot-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        output.AddAttachment(path);
    }

    [AfterStep("attachthenthrow")]
    public void AttachThenThrow()
    {
        if (!scenario.ScenarioInfo.Tags.Contains("armed")) return;
        var path = Path.Combine(Path.GetTempPath(), "rq6-shot-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        output.AddAttachment(path);
        throw new InvalidOperationException("after step hook failed");
    }

    [AfterTestRun(Order = int.MaxValue)]
    public static void Report() => Kronikol.ReqNRoll.ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new Kronikol.ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("R6_DIR") ?? "Reports"),
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
    });

    [Given("a registered customer")] public Task Customer() => driver.A_customer_is_registered();

    [Given("a customer with a screenshot")]
    public async Task CustomerWithFile()
    {
        await driver.A_customer_is_registered();
        var path = Path.Combine(Path.GetTempPath(), "rq6-file-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        output.AddAttachment(path);
    }

    [Given("a customer that fails when tagged")]
    public async Task CustomerFailsWhenTagged()
    {
        await driver.A_customer_is_registered();
        if (scenario.ScenarioInfo.Tags.Contains("bgfails")) throw new InvalidOperationException("background failed");
    }

    [StepDefinition(@"an order of (\d+) is placed")] public Task Order(int amount) => driver.Post("orders/" + amount);
    [Then("the order is confirmed")] public Task Confirmed() => driver.Post("confirm");
}
