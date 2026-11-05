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

[Story(AsA = "probe", IWant = "to see a retried test", SoThat = "the plan has numbers")]
public class Retries
{
    private readonly Driver _driver = new();
    private static int _attempts;
    private static int _alwaysAttempts;

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void WhenAFlakyStepRuns()
    {
        _driver.An_order_is_submitted();
        if (Interlocked.Increment(ref _attempts) == 1)
            throw new InvalidOperationException("first attempt fails");
    }
    private void WhenAStepAlwaysFails()
    {
        _driver.An_order_is_submitted();
        Interlocked.Increment(ref _alwaysAttempts);
        throw new InvalidOperationException("always fails");
    }
    private void ThenTheOrderIsReadBack() => _driver.The_order_is_read_back();

    // What a retrying runner does: the test again under the same test id. xRetry.v3 1.0.0 does not load on xunit.v3 3.2.2.
    private static void Attempts(int times, Action test)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { test(); return; }
            catch when (attempt < times) { }
        }
    }

    [Fact]
    public void R0_a_test_that_passes_on_its_second_attempt() => Attempts(3, () =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFlakyStepRuns()).Then(x => x.ThenTheOrderIsReadBack()).BDDfy());

    [Fact]
    public void R1_a_control_with_no_retry() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsReadBack()).BDDfy();

    // Every attempt fails: the same id three times.
    [Fact]
    public void R2_a_test_that_fails_every_attempt() => Attempts(3, () =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAStepAlwaysFails()).Then(x => x.ThenTheOrderIsReadBack()).BDDfy());
}
