using System.Net;
using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Xunit;

[assembly: TrackSteps]

namespace ProbeRuntimeAsync;

// The same steps compiled for state machines and for runtime async (-p:RuntimeAsyncOn=true, DOTNET_RuntimeAsync=1). A runtime-async step has no stub,
// so nothing runs in its caller's frame: #226's weave brackets it, and StartStep's flow write stays inside the callee.
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

public class Steps
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

    [Step] public async Task First() { await Task.Yield(); await Post("first"); }
    [Step] public async Task Second() { await Task.Yield(); await Post("second"); }

    [Step] public async Task Fast() { await Task.Delay(10); await Post("fast"); }
    [Step] public async Task Slow() { await Task.Delay(120); await Post("slow"); }
    [Step] public async Task The_next() { await Post("next"); }

    [Step] public async Task Both_at_once() => await Task.WhenAll(Fast(), Slow());

    [Step] public async Task Throws() { await Task.Delay(10); await Post("throws"); throw new InvalidOperationException("this one threw"); }
    [Step] public async Task Goes_on() { await Task.Delay(80); await Post("goes-on"); }
    [Step] public async Task One_throws() => await Task.WhenAll(Throws(), Goes_on());

    [Step] public async Task Starts_one_and_calls() { var child = Slow(); await Post("own"); await child; }
}

public class Probes : DiagrammedComponentTest
{
    private readonly Steps _s = new();

    [Fact] public async Task A0_one_after_another() { await _s.First(); await _s.Second(); }
    [Fact] public async Task A1_two_at_the_top_level_then_a_step() { await Task.WhenAll(_s.Fast(), _s.Slow()); await _s.The_next(); }
    [Fact] public Task A2_two_inside_a_step() => _s.Both_at_once();
    [Fact] public async Task A3_one_left_running_at_the_top_level() { var left = _s.Slow(); await _s.The_next(); await left; }
    [Fact] public Task A4_a_step_starts_one_and_calls() => _s.Starts_one_and_calls();
    [Fact] public async Task A5_two_inside_a_step_the_first_throws()
    {
        try { await _s.One_throws(); }
        catch (InvalidOperationException) { }
    }
}
