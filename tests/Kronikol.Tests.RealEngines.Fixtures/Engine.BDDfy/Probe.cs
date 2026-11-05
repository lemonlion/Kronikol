using System.Net;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using TestStack.BDDfy;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(EngineFixtures.Setup))]

namespace EngineFixtures;

public class Setup : IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("NO_BARS") == "1")
            StepCollector.Options = new StepTrackingOptions { ShowStepDelimiters = false };
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
        foreach (var scenario in BDDfyScenarioCollector.GetAll().Where(s => s.ScenarioTitle.StartsWith("F5", StringComparison.Ordinal) || s.ScenarioTitle.StartsWith("HS2", StringComparison.Ordinal)))
            scenario.Steps[1] = new BDDfyStepInfo(scenario.Steps[1].Keyword, "the order is ***", scenario.Steps[1].Result, scenario.Steps[1].Duration);
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
        });
        return ValueTask.CompletedTask;
    }
}

public class Driver
{
    [Step] public void Seeds_with_a_file() => Files.Attach("seed-file");
    private sealed class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private readonly HttpClient _client = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions
    {
        CallerName = "Tests",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Stub() }) { BaseAddress = new Uri("http://shop.test/") };

    public void Post(string path) => _client.PostAsync(path, new StringContent("{}")).GetAwaiter().GetResult();

    [Step] public void Reference_data_is_seeded() => Post("seed");
    [Step] public void Reference_data_is_cleaned() => Post("clean");
    [Step] public void Customer_named(string name) => Post("customers/" + name);
    [Step] public void A_customer_is_registered() => Post("customers");
    [Step] public void An_order_is_submitted() => Post("orders");
    [Step] public void The_order_is_read_back() => Post("orders/1");
    [Step] public async Task A_slow_background_check() { await Task.Delay(200); Post("background"); }
}

[Story(AsA = "probe", IWant = "to see where a step lands", SoThat = "the plan has numbers")]
public class Probes
{
    private readonly Driver _driver = new();

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void GivenAStepIsLeftRunning() { _ = _driver.A_slow_background_check(); _driver.Post("started"); }
    private void WhenTheCustomerPlacesTheOrder() => _driver.An_order_is_submitted();
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    [Fact]
    public void B0_control() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTheCustomerPlacesTheOrder()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void B1_a_step_method_before_BDDfy()
    {
        _driver.Reference_data_is_seeded();
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTheCustomerPlacesTheOrder()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
    }

    [Fact]
    public void B2_a_step_method_after_BDDfy()
    {
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTheCustomerPlacesTheOrder()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
        _driver.Reference_data_is_cleaned();
    }

    [Fact]
    public void B3_a_step_method_left_running() =>
        this.Given(x => x.GivenAStepIsLeftRunning()).When(x => x.WhenTheCustomerPlacesTheOrder()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
}

public class AsyncAndTeardownProbe
{
    private readonly Driver _driver = new();

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private async Task WhenTheCustomerPlacesTheOrderLater()
    {
        await Task.Delay(30);
        _driver.Post("orders-after-await");
    }
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();
    private void Cleanup() => _driver.Post("teardown-with");
    private void CleanupWithAFile() => Files.Attach("teardown-file");

    [Fact]
    public void B6_an_async_step_with_a_call_after_its_first_await() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTheCustomerPlacesTheOrderLater()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void F1_a_file_in_a_teardown_the_report_does_not_list() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).TearDownWith(x => x.CleanupWithAFile()).BDDfy();

    [Fact]
    public void B7_a_teardown_step_the_report_does_not_list() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).TearDownWith(x => x.Cleanup()).BDDfy();
}

public class HiddenSetupWithFiles
{
    private readonly Driver _driver = new();

    public void Setup()
    {
        Files.Attach("setup-file");
        _driver.Seeds_with_a_file();
    }

    public void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    public void WhenThePaymentFails() => throw new InvalidOperationException("payment declined");

    [Fact]
    public void F3_a_hidden_setups_files_when_a_later_step_fails() => this.BDDfy();
}

public class ClearedSteps
{
    private readonly Driver _driver = new();

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    [Fact]
    public void F5_a_row_a_consumer_rebuilt() => this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void F4_steps_cleared_after_the_run()
    {
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
        StepCollector.ClearSteps(Xunit.TestContext.Current.Test!.UniqueID);
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

public class ExamplesProbe
{
    private readonly Driver _driver = new();

    private void GivenTheCustomerIsNamed(string customer) => _driver.Customer_named(customer);
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();
    private void GivenAFileNamedAfter(string customer) => Files.Attach("row-" + customer);

    [Fact]
    public void F2_a_file_in_each_row_of_an_example_table()
    {
        string customer = null!;
        this.Given(x => x.GivenAFileNamedAfter(customer))
            .WithExamples(new ExampleTable("customer") { "alice", "bob" })
            .BDDfy();
    }

    [Fact]
    public void B5_examples_in_one_test()
    {
        string customer = null!;
        this.Given(x => x.GivenTheCustomerIsNamed(customer))
            .Then(x => x.ThenTheOrderIsConfirmed())
            .WithExamples(new ExampleTable("customer") { "alice", "bob" })
            .BDDfy();
    }
}

public class ConstructorProbe
{
    private readonly Driver _driver = new();

    public ConstructorProbe() => _driver.Reference_data_is_seeded();

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void WhenTheCustomerPlacesTheOrder() => _driver.An_order_is_submitted();
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    [Fact]
    public void B4_a_step_method_in_the_constructor() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTheCustomerPlacesTheOrder()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
}

public class ExamplesThatStopEarly
{
    private readonly Driver _driver = new();
    private void GivenTheCustomerIsNamed(string customer)
    {
        _driver.Customer_named(customer);
        if (customer == "alice") throw new InvalidOperationException("alice is refused");
    }
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    [Fact]
    public void Z8_examples_the_first_row_fails_at_its_given()
    {
        string customer = null!;
        this.Given(x => x.GivenTheCustomerIsNamed(customer))
            .Then(x => x.ThenTheOrderIsConfirmed())
            .WithExamples(new ExampleTable("customer") { "alice", "bob" })
            .BDDfy();
    }
}

public class TwoScenariosInOneTest
{
    private readonly Driver _driver = new();
    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void WhenTheCustomerPlacesTheOrder() => _driver.An_order_is_submitted();
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    [Fact]
    public void Z9_two_scenarios_in_one_test()
    {
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy("Z9 first of two");
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTheCustomerPlacesTheOrder()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy("Z9 second of two");
    }
}

public class ReflectiveSetupAndTeardown
{
    private readonly Driver _driver = new();
    public void Setup() => _driver.Reference_data_is_seeded();
    public void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    public void WhenTheCustomerPlacesTheOrder() => _driver.An_order_is_submitted();
    public void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();
    public void TearDown() => _driver.Reference_data_is_cleaned();

    [Fact]
    public void Z10_reflective_setup_and_teardown() => this.BDDfy();
}

public class ALongStepBeforeOneThatFails
{
    private readonly Driver _driver = new();
    private void GivenAStepWhoseTitleIsLongerThanAnyBarDrawsItWholeSoItsBarCannotBeMatchedAgainstItsTextByAnyoneReadingTheDiagramLater() => _driver.Post("long");
    private void WhenTheNextStepFails()
    {
        _driver.Post("fails");
        throw new InvalidOperationException("it failed");
    }
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    // A row that never ran has no id, and that must not cost the rows that ran theirs: the long step's bar cannot be read by its text.
    [Fact]
    public void Z11_a_long_step_and_a_failing_step_with_one_after_it() =>
        this.Given(x => x.GivenAStepWhoseTitleIsLongerThanAnyBarDrawsItWholeSoItsBarCannotBeMatchedAgainstItsTextByAnyoneReadingTheDiagramLater())
            .When(x => x.WhenTheNextStepFails())
            .Then(x => x.ThenTheOrderIsConfirmed())
            .BDDfy();
}

public class Retries
{
    private readonly Driver _driver = new();
    private static int _attempts;

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void WhenAFlakyStepRuns()
    {
        _driver.An_order_is_submitted();
        if (Interlocked.Increment(ref _attempts) == 1)
            throw new InvalidOperationException("first attempt fails");
    }
    private void ThenTheOrderIsReadBack() => _driver.The_order_is_read_back();

    // What a retrying runner does: the test again under the same test id.
    [Fact]
    public void Z12_a_test_that_passes_on_its_second_attempt()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFlakyStepRuns()).Then(x => x.ThenTheOrderIsReadBack()).BDDfy();
                return;
            }
            catch when (attempt < 3)
            {
            }
        }
    }
}
