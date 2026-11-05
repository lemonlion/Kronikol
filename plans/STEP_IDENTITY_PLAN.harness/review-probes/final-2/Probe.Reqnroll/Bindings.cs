using System.Net;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Xunit;

[assembly: TrackSteps]

namespace EngineFixtures;

public class Driver
{
    private sealed class Stub : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(1, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private readonly HttpClient _client = new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "Tests",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Stub() }) { BaseAddress = new Uri("http://shop.test/") };

    public Task Post(string path) => _client.PostAsync(path, new StringContent("{}"));

    [Step] public async Task Reference_data_is_seeded() => await Post("seed");
    [Step] public async Task Reference_data_is_cleaned() => await Post("clean");
    [Step] public async Task A_customer_is_registered() => await Post("customers");
    [Step] public async Task An_order_is_submitted() => await Post("orders");
    [Step] public async Task The_order_is_read_back() => await Post("orders/1");
    [Step] public async Task Block_prepared() => await Post("block");

    [Step]
    public async Task Seeding_fails()
    {
        await Post("seed");
        throw new InvalidOperationException("seeding failed");
    }

    [Step]
    public async Task Cleaning_fails()
    {
        await Post("clean");
        throw new InvalidOperationException("cleaning failed");
    }

    [Step]
    public async Task A_slow_check_that_fails()
    {
        await Task.Delay(150);
        await Post("slow-check");
        throw new InvalidOperationException("slow check failed");
    }

    [Step] public void Skips_itself() => Assert.Skip("not on this platform");

    [Step]
    public async Task A_step_with_a_file(string name)
    {
        await Post("with-file");
        Files.Attach(name);
    }
}

public static class Files
{
    public static void Attach(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, name);
        Track.Attachment(path, name);
    }
}

[Binding]
public class Steps(Driver driver, ITestRunner runner, IReqnrollOutputHelper output, ScenarioContext scenario)
{
    [Given("a registered customer")] public Task Registered() => driver.A_customer_is_registered();
    [When("the customer places the order")] public Task Places() => driver.An_order_is_submitted();
    [Then("the order is confirmed")] public Task Confirmed() => driver.The_order_is_read_back();

    [When("the payment fails")]
    public async Task Fails()
    {
        await driver.Post("payments");
        throw new InvalidOperationException("payment declined");
    }

    [Given("a step that runs another step")]
    public async Task Outer()
    {
        await runner.GivenAsync("a registered customer", null, null, "Given ");
        await driver.Post("outer");
    }

    [Given("a step that runs a step with a table")]
    public async Task OuterWithTable()
    {
        var table = new Table("sku", "count");
        table.AddRow("APPLE", "3");
        await runner.GivenAsync("the basket is listed", null, table);
        await driver.Post("outer");
    }

    [Given("the basket is listed")] public Task Basket(Table table) => driver.Post("basket");

    [Given("a step that runs a step with a file")]
    public async Task OuterWithFile()
    {
        await runner.GivenAsync("a step with a file");
        await driver.Post("outer");
    }

    [Given("a step with a file")]
    public async Task WithFile()
    {
        await driver.A_step_with_a_file("inner-file");
        var path = Path.Combine(Path.GetTempPath(), $"output-file-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "x");
        output.AddAttachment(path);
    }

    [Given("a step that runs a pending step")]
    public async Task OuterPending()
    {
        await runner.GivenAsync("a pending step");
        await driver.Post("outer");
    }

    [Given("a pending step")] public void Pending() => throw new PendingStepException();

    [Given("a step that runs a step nobody wrote")]
    public async Task OuterUndefined()
    {
        await runner.GivenAsync("nobody wrote this step");
        await driver.Post("outer");
    }

    [Given("a step that runs a step whose after step hook throws")]
    public async Task OuterHookThrows()
    {
        await runner.GivenAsync("an inner step for the hook");
        await driver.Post("outer");
    }

    [Given("an inner step for the hook")] public Task InnerForHook() => driver.Post("inner");

    [Given("a binding that is a step method")]
    [Step]
    public async Task Woven() => await driver.Post("woven");

    [When("a step method skips the test")]
    public void Skips() => driver.Skips_itself();

    [Given("the shop is ready")]
    public async Task Ready()
    {
        await driver.Post("ready");
        if (scenario.ScenarioInfo.Tags.Contains("failgiven"))
            throw new InvalidOperationException("the shop is not ready");
    }

    [When(@"an order of (\d+) is placed")] public Task Order(int amount) => driver.Post("orders/" + amount);
}

[Binding]
public class Hooks(Driver driver, ScenarioContext scenario, ITestRunner runner)
{
    [BeforeScenario("seededrow")]
    public async Task SeedRow()
    {
        if ((string?)scenario.ScenarioInfo.Arguments["amount"] == "20")
            await driver.Reference_data_is_seeded();
    }

    [BeforeScenario("seeded")] public Task Seed() => driver.Reference_data_is_seeded();

    [BeforeScenario("caught")]
    public async Task Caught()
    {
        try { await driver.Seeding_fails(); }
        catch (InvalidOperationException) { }
    }

    [BeforeScenarioBlock("q4")] public Task Q4Block() => driver.Block_prepared();
    [BeforeStep("q4", Order = int.MinValue)]
    public void Q4Throw()
    {
        if (scenario.StepContext.StepInfo.StepDefinitionType == Reqnroll.Bindings.StepDefinitionType.When)
            throw new InvalidOperationException("before step hook failed");
    }

    [AfterScenario("q7")] public Task Q7() => runner.GivenAsync("a registered customer");

    [BeforeScenario("q8")]
    public Task Q8()
    {
        _ = Run();
        return Task.CompletedTask;
        async Task Run()
        {
            try { await driver.A_slow_check_that_fails(); }
            catch (InvalidOperationException) { }
        }
    }

    [AfterStep("q18")]
    public void Q18()
    {
        if (scenario.StepContext.StepInfo.Text == "an inner step for the hook")
            throw new InvalidOperationException("after step hook failed");
    }

    [BeforeScenario("needsgpu")] public void NeedsGpu(Reqnroll.UnitTestProvider.IUnitTestRuntimeProvider runtime) => runtime.TestIgnore("no GPU here");

    [AfterScenario("q43")] public Task Q43() => driver.Cleaning_fails();
    [BeforeScenario("seededfile")] public Task SeedFile() => driver.A_step_with_a_file("hook-file");

    [BeforeScenarioBlock("q23")] public Task Q23() => driver.Block_prepared();

    [AfterTestRun(Order = int.MaxValue)]
    public static void Report()
    {
        foreach (var info in ReqNRollScenarioCollector.GetAll())
        {
            if (info.ScenarioTitle.StartsWith("Q3 ", StringComparison.Ordinal) || info.ScenarioTitle.StartsWith("Q3b", StringComparison.Ordinal) || info.ScenarioTitle.StartsWith("Q3c", StringComparison.Ordinal))
            {
                var last = info.Steps.Count - 1;
                var step = info.Steps[last];
                info.Steps[last] = new ReqNRollStepInfo(step.Keyword, step.Text.Replace("confirmed", "***"), step.Status, step.Duration);
            }
        }

        var infos = ReqNRollScenarioCollector.GetAll().Select(i => !i.ScenarioTitle.StartsWith("Q5", StringComparison.Ordinal) ? i : new ReqNRollScenarioInfo
        {
            ScenarioId = i.ScenarioId,
            ScenarioTitle = i.ScenarioTitle,
            FeatureTitle = i.FeatureTitle,
            ScenarioTags = i.ScenarioTags,
            CombinedTags = i.CombinedTags,
            Steps = i.Steps,
            ExecutionStatus = i.ExecutionStatus,
        }).ToArray();
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(infos, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
        });
    }
}
