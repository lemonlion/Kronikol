using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;

namespace HookSteps.Repro;

public class ZDriver(Driver driver)
{
    [Step] public Task Seed_for(string tenant) => driver.Post("seed/" + tenant);
    [Step] public async Task Optional_seed() { await driver.Post("optional"); throw new InvalidOperationException("optional seed failed"); }
}

[Binding]
public class ZSteps(Driver driver, ScenarioContext scenario, ITestRunner runner)
{
    private bool Tagged(string tag) => scenario.ScenarioInfo.Tags.Contains(tag);

    [Given("the shop is ready")]
    public async Task Ready()
    {
        await driver.A_customer_is_registered();
        if (Tagged("fail-given")) throw new InvalidOperationException("the shop did not start");
    }

    [When("the customer orders")]
    public async Task Orders()
    {
        await driver.An_order_is_submitted();
        if (Tagged("fail-when")) throw new InvalidOperationException("the order was refused");
    }

    [Then("the order is listed")] public Task Listed() => driver.The_order_is_read_back();
    [Then("the receipt is sent")] public Task Receipt() => driver.Post("receipt");
    [Then("the invoice is sent")] public Task Invoice() => driver.Post("invoice");
    [Then("this one is pending")] public void Pending() => throw new PendingStepException();
}

[Binding]
public class ZHooks(Driver driver, ZDriver z, ScenarioContext scenario)
{
    [BeforeScenario("fail-hook")] public void FailHook() => throw new InvalidOperationException("the hook failed");
    [BeforeScenario("fail-hook-step")] public Task FailHookStep() => driver.Seeding_fails();
    [BeforeScenario("seeded")] public Task Seeded() => driver.Reference_data_is_seeded();
    [BeforeScenario("seed-a")] public Task SeedA() => z.Seed_for("alice");
    [BeforeScenario("seed-b")] public Task SeedB() => z.Seed_for("bob");

    [BeforeScenario("swallow")]
    public async Task Swallow()
    {
        try { await z.Optional_seed(); } catch (InvalidOperationException) { }
    }

    [AfterTestRun(Order = int.MaxValue)]
    public static void Report() => ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
    });
}

[Binding]
public class ZMore(Driver driver)
{
    [Given("the shop is ready for (.*)")]
    public async Task ReadyFor(string row)
    {
        await driver.A_customer_is_registered();
        if (row == "two") throw new InvalidOperationException("row two did not start");
    }

    [When("the payment fails")]
    public async Task Fails()
    {
        await driver.Post("payments");
        throw new InvalidOperationException("payment declined");
    }
}
