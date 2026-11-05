using System.Net;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PhaseProbe;
using TestStack.BDDfy;
using Xunit;

[assembly: AssemblyFixture(typeof(Probe.Host.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace Probe.Host;

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
        Dump.Write("host");
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Probe host", SeparateSetup = true });
        return ValueTask.CompletedTask;
    }
}

/// <summary>What the service under test does: a woven step of its own on the server's side, a tracked outgoing call, and work it leaves behind.</summary>
public sealed class Service(IHttpContextAccessor accessor)
{
    private readonly HttpClient _downstream = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Service", FixedNameForReceivingService = "Downstream", HttpContextAccessor = accessor }) { InnerHandler = new AnswerEverything() });

    public Task Call(string label) => _downstream.GetAsync($"http://downstream.local/{label}");

    [GivenStep] public async Task ReserveOnTheServer(string label) { await Task.Yield(); await Call(label); }
    [WhenStep] public async Task BackgroundStep(string label) { await Task.Delay(30); await Call(label); }
    public Task? LastLeft;
    public void LeaveBehind(string label) => LastLeft = Task.Run(() => BackgroundStep(label));
}

public sealed class Hosted : IAsyncLifetime
{
    public IHost App = null!;
    public TestServer Server = null!;

    public async ValueTask InitializeAsync()
    {
        App = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(s => { s.AddHttpContextAccessor(); s.AddSingleton<Service>(); s.AddRouting(); s.AddTestTrackingContextPropagation(); })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/plain/{label}", async (string label, Service svc) => { await svc.Call(label + "-down"); return Results.Ok(); });
                        e.MapGet("/woven/{label}", async (string label, Service svc) => { await svc.ReserveOnTheServer(label + "-down"); return Results.Ok(); });
                        e.MapGet("/leaves/{label}", async (string label, Service svc) => { svc.LeaveBehind(label + "-late"); await svc.Call(label + "-down"); return Results.Ok(); });
                    });
                }))
            .StartAsync();
        Server = App.GetTestServer();
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        App.Dispose();
    }
}

public class Stories : IClassFixture<Hosted>
{
    private readonly Hosted _host;
    private readonly HttpClient _client;

    public Stories(Hosted host)
    {
        Known.Register();
        _host = host;
        _client = new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Test", FixedNameForReceivingService = "Service" }) { InnerHandler = host.Server.CreateHandler() }) { BaseAddress = new Uri("http://service.local") };
    }

    private async Task Get(string path) => (await _client.GetAsync(path)).EnsureSuccessStatusCode();

    public async Task GivenPlain(string l) { await Task.Yield(); await Get($"/plain/{l}"); }
    public async Task WhenPlain(string l) { await Task.Yield(); await Get($"/plain/{l}"); Track.AssertionPassed("when " + l); }
    public async Task WhenWovenOnServer(string l) { await Task.Yield(); await Get($"/woven/{l}"); await Get($"/plain/{l}-after"); Track.AssertionPassed("when " + l); }
    public async Task WhenLeaves(string l) { await Task.Yield(); await Get($"/leaves/{l}"); await Get($"/plain/{l}-after"); Track.AssertionPassed("when " + l); }
    public async Task ThenPlain(string l) { await Task.Yield(); await Task.Delay(60); await Get($"/plain/{l}"); Track.AssertionPassed("then " + l); }

    [Fact] public void H1_plain_calls_through_a_host() =>
        this.Given(x => x.GivenPlain("h1-g")).When(x => x.WhenPlain("h1-w")).Then(x => x.ThenPlain("h1-t")).BDDfy();

    [Fact] public void H2_a_woven_step_on_the_servers_side_of_the_call() =>
        this.Given(x => x.GivenPlain("h2-g")).When(x => x.WhenWovenOnServer("h2-w")).Then(x => x.ThenPlain("h2-t")).BDDfy();

    [Fact] public void H3_the_server_leaves_a_woven_step_running_past_the_response() =>
        this.Given(x => x.GivenPlain("h3-g")).When(x => x.WhenLeaves("h3-w")).Then(x => x.ThenPlain("h3-t")).BDDfy();
}
