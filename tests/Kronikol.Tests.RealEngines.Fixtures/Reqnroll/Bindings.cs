using Kronikol;
using Kronikol.ReqNRoll;
using Reqnroll;

namespace EngineFixtures;

[Binding]
public class Steps(Driver driver, ITestRunner runner)
{
    [Given("a registered customer")] public Task Registered() => driver.A_customer_is_registered();
    [When("the customer places the order")] public Task Places() => driver.An_order_is_submitted();

    [When("the payment fails")]
    public async Task Fails()
    {
        await driver.Post("payments");
        throw new InvalidOperationException("payment declined");
    }

    [Given("a step method is left running")] public Task LeftRunning() { _ = driver.A_slow_background_check(); return driver.Post("started"); }
    [Given("two step methods run in parallel")] public Task Parallel() => Task.WhenAll(driver.First_parallel(), driver.Second_parallel());

    [Given("a step that runs another step")]
    public async Task Outer()
    {
        await runner.GivenAsync("a registered customer", null, null, "Given ");
        await driver.Post("outer");
    }

    [Given("two step methods are left running")]
    public async Task TwoLeft()
    {
        _ = driver.A_quick_check();
        _ = driver.A_slow_background_check();
        await Task.Delay(60);
        await driver.Post("started");
    }

    [When("the order takes a while")] public Task TakesAWhile() => driver.An_order_is_submitted_slowly();

    [Given("a step that runs steps with arguments")]
    public async Task OuterWithArguments()
    {
        var table = new Table("sku", "count");
        table.AddRow("APPLE", "3");
        await runner.GivenAsync("the basket is listed", null, table);
        await runner.GivenAsync("the note \"kept\" says", "a doc string", null);
    }

    [Given("the basket is listed")] public Task Basket(Table table) => driver.Post("basket");
    [Given("the note \"(.*)\" says")] public Task Note(string name, string body) => driver.Post("note");

    [Given("a step that runs two steps, the first failing")]
    public async Task OuterFirstFails()
    {
        await runner.GivenAsync("an inner step fails");
        await runner.GivenAsync("a registered customer");
    }

    [Given("an inner step fails")]
    public async Task InnerFails()
    {
        await driver.Post("inner");
        throw new InvalidOperationException("inner step failed");
    }

    [Then("the order is confirmed")] public Task Confirmed() => driver.The_order_is_read_back();
}

/// <summary>One hook per suspected trigger, each scoped to its scenario's tag.</summary>
[Binding]
public class Hooks(Driver driver, ScenarioContext scenario)
{
    [BeforeScenario("t01")] public Task T01() => driver.Reference_data_is_seeded();
    [BeforeScenarioBlock("t02")] public Task T02() => driver.Block_prepared();
    [AfterScenarioBlock("t03")] public Task T03() => driver.Block_prepared();
    [AfterScenario("t04")] public Task T04() => driver.Reference_data_is_cleaned();
    [BeforeScenario("t05")] public Task T05() => driver.Seeding_fails();
    [AfterStep("t06")] public void T06() => throw new InvalidOperationException("after step hook failed");
    [BeforeStep("t07")] public void T07() => throw new InvalidOperationException("before step hook failed");
    [BeforeScenario("t29")] public Task T29Seed() => driver.Reference_data_is_seeded();
    [BeforeStep("t29", Order = int.MinValue)] public void T29() => throw new InvalidOperationException("before step hook failed");
    [BeforeScenario("t09")] public Task T09() => driver.Post("seed");
    [BeforeStep("t10")] public Task T10() => driver.Step_observed();
    [BeforeScenario("t11")] public Task T11() => driver.A_tenant_exists();
    [AfterStep("t12")] public Task T12() => driver.Step_observed();
    [BeforeScenario("t13")] public Task T13() => driver.Reference_data_is_seeded();

    [BeforeScenario("t14")]
    public async Task T14(IReqnrollOutputHelper output)
    {
        var path = Path.Combine(Path.GetTempPath(), "hook-step-attachment.txt");
        await File.WriteAllTextAsync(path, "seeded");
        output.AddAttachment(path);
        await driver.Post("seed");
    }

    [BeforeScenarioBlock("t22")]
    public void T22()
    {
        if (scenario.CurrentScenarioBlock == ScenarioBlock.When)
            throw new InvalidOperationException("block hook failed");
    }

    [BeforeScenario("t24")]
    public Task T24()
    {
        _ = driver.A_slow_background_check();
        return Task.CompletedTask;
    }

    [BeforeScenario("t27")] public Task T27(ITestRunner runner) => runner.GivenAsync("a registered customer");
    [AfterScenario("t20")] public Task T20() => driver.Post("teardown");
    [AfterScenario("t15")] public void T15() => throw new InvalidOperationException("after scenario hook failed");
    [BeforeScenario("t16", Order = int.MinValue)] public Task T16() => driver.Reference_data_is_seeded();

    // A suite can read the public key on the scenario, as it always could, and a Gherkin step run by another must not lose it.
    [AfterStep("t28")]
    public void T28()
    {
        if (scenario[Kronikol.ReqNRoll.ReqNRollConstants.StepStopwatchKey] is not System.Diagnostics.Stopwatch)
            throw new InvalidOperationException("the step stopwatch is not on the scenario");
    }

    [BeforeTestRun]
    public static void Bars()
    {
        if (Environment.GetEnvironmentVariable("NO_BARS") == "1")
            Kronikol.Tracking.StepCollector.Options = new Kronikol.Tracking.StepTrackingOptions { ShowStepDelimiters = false };
    }

    [AfterTestRun(Order = int.MaxValue)]
    public static void Report() => ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
    });
}
