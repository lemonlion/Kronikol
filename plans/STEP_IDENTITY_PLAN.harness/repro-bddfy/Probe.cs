using System.Net;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using TestStack.BDDfy;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(HookSteps.Bddfy.Setup))]

namespace HookSteps.Bddfy;

public class Setup : IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
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

    [Fact]
    public void B6_an_async_step_with_a_call_after_its_first_await() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTheCustomerPlacesTheOrderLater()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void B7_a_teardown_step_the_report_does_not_list() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).TearDownWith(x => x.Cleanup()).BDDfy();
}

public class ExamplesProbe
{
    private readonly Driver _driver = new();

    private void GivenTheCustomerIsNamed(string customer) => _driver.Customer_named(customer);
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

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
