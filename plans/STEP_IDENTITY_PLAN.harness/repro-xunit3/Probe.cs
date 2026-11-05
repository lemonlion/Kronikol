using System.Net;
using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Xunit;

[assembly: TrackSteps]

namespace HookSteps.XUnit3;

// The lane with no framework steps: where a step is a [Step] method and nothing else, the walk's answers must not move.
public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
        });
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class DiagrammedTestCollection : ICollectionFixture<TestRun> { }

public class Driver
{
    private sealed class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private readonly HttpClient _client = new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
    {
        CallerName = "Tests",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Stub() }) { BaseAddress = new Uri("http://shop.test/") };

    public Task Post(string path) => _client.PostAsync(path, new StringContent("{}"));

    [Step] public async Task A_customer_is_registered() => await Post("customers");
    [Step] public async Task An_order_is_submitted() => await Post("orders");
    [Step] public Task A_step_with_no_call() => Task.CompletedTask;
}

public class Probes : DiagrammedComponentTest, IAsyncDisposable
{
    private readonly Driver _driver = new();
    private bool _teardownCall;

    [Fact]
    public async Task P0_control()
    {
        await _driver.A_customer_is_registered();
        await _driver.An_order_is_submitted();
    }

    [Fact]
    public async Task P1_a_call_between_two_steps()
    {
        await _driver.A_customer_is_registered();
        await _driver.Post("between");
        await _driver.An_order_is_submitted();
    }

    [Fact]
    public async Task P2_a_call_before_the_first_step_and_after_the_last()
    {
        await _driver.Post("before");
        await _driver.A_customer_is_registered();
        await _driver.Post("after");
    }

    [Fact]
    public async Task P3_a_call_after_a_step_that_made_none()
    {
        await _driver.A_customer_is_registered();
        await _driver.A_step_with_no_call();
        await _driver.Post("after-empty-step");
    }

    [Fact]
    public async Task P4_a_call_in_teardown()
    {
        _teardownCall = true;
        await _driver.A_customer_is_registered();
    }

    public async ValueTask DisposeAsync()
    {
        if (_teardownCall)
            await _driver.Post("teardown");
    }
}
