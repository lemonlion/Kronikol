using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(PhaseProbe.Hs.TestRun))]

namespace PhaseProbe.Hs;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Dump.Write("hs");
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions
        {
            SpecificationsTitle = "Phase probe",
            SeparateSetup = true,
            DiagnosticMode = true,
            GenerateMergeableData = Environment.GetEnvironmentVariable("PROBE_MERGEABLE") == "on",
        });
    }
}

public sealed class TrackerPublish(MessageTracker tracker) : IPublish
{
    public void Send(string label) => tracker.TrackSendEvent("bus", label, new Uri($"bus://shop/{label}"), new { label, payload = "body-" + label });
}

public static class Hosts
{
    private static WebApplicationFactory<Program> Make(bool trackDuringSetup, bool preserve)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.TrackDependenciesForDiagrams(new XUnitTestTrackingMessageHandlerOptions
            {
                CallerName = "Shop",
                FixedNameForReceivingService = "Down",
                TrackDuringSetup = trackDuringSetup,
            });
            services.TrackMessagesForDiagrams(new MessageTrackerOptions
            {
                CallerName = "Shop",
                ServiceName = "Bus",
                UseHttpContextCorrelation = true,
                CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher,
                TrackDuringSetup = trackDuringSetup,
                SetupVerbosity = MessageTrackerVerbosity.Summarised,
                ActionVerbosity = MessageTrackerVerbosity.Detailed,
            });
            services.AddSingleton<IPublish>(sp => new TrackerPublish(sp.GetRequiredService<MessageTracker>()));
        }));
        if (preserve)
            factory.Server.PreserveExecutionContext = true;
        return factory;
    }

    // Lazily, as a consumer's static fixture is: the host starts inside the first test that asks.
    public static readonly Lazy<WebApplicationFactory<Program>> Default = new(() => Make(true, false));
    public static readonly Lazy<WebApplicationFactory<Program>> NoSetup = new(() => Make(false, false));
    public static readonly Lazy<WebApplicationFactory<Program>> Preserve = new(() => Make(true, true));
    public static readonly Lazy<WebApplicationFactory<Program>> PreserveNoSetup = new(() => Make(false, true));

    public static HttpClient Client(WebApplicationFactory<Program> factory, bool trackDuringSetup = true) =>
        factory.CreateTestTrackingClient(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = trackDuringSetup });

    public static readonly Lazy<HttpClient> SharedClient = new(() => Client(Default.Value));
}

public abstract class HostSteps : DiagrammedComponentTest
{
    protected HttpClient C = null!;

    protected HostSteps(HttpClient client)
    {
        C = client;
        Dump.Names[TestContext.Current.Test!.UniqueID] = TestContext.Current.TestMethod!.MethodName;
    }

    private async Task Get(string route) => (await C.GetAsync(route)).EnsureSuccessStatusCode();

    [GivenStep] public async Task GivenCall(string l) => await Get($"/call/{l}");
    [GivenStep] public async Task GivenNoDownstream(string l) => await Get($"/nocall/{l}");
    [GivenStep] public async Task GivenNothing() => await Task.Yield();
    [GivenStep] public async Task GivenMessage(string l) => await Get($"/message/{l}");
    [GivenStep] public async Task GivenQueued(string l) => await Get($"/queue/{l}");
    [WhenStep] public async Task WhenCall(string l) => await Get($"/call/{l}");
    [WhenStep] public async Task WhenNothing() => await Task.Yield();
    [WhenStep] public async Task WhenMessage(string l) => await Get($"/message/{l}");
    [WhenStep] public async Task WhenQueued(string l) => await Get($"/queue/{l}");
    [WhenStep] public async Task WhenExplicitFirst(string l) { TrackingDiagramOverride.StartAction(); await Get($"/call/{l}"); }
    [WhenStep] public async Task WhenPrepThenExplicit(string prep, string act) { await Get($"/call/{prep}"); TrackingDiagramOverride.StartAction(); await Get($"/call/{act}"); }
    [ThenStep] public async Task ThenCall(string l) => await Get($"/call/{l}");
    [ThenStep] public async Task ThenSettles() => await Task.Delay(150);
}

public class OnDefault() : HostSteps(Hosts.Client(Hosts.Default.Value))
{
    [Fact] public async Task HS01_given_when_then() { await GivenCall("hs01-g"); await WhenCall("hs01-w"); await ThenCall("hs01-t"); }
    [Fact] public async Task HS02_given_nothing_when_call() { await GivenNothing(); await WhenCall("hs02-w"); }
    [Fact] public async Task HS03_given_call_when_nothing_then_call() { await GivenCall("hs03-g"); await WhenNothing(); await ThenCall("hs03-t"); }
    [Fact] public async Task HS04_server_side_messages_with_a_verbosity_per_phase() { await GivenMessage("hs04-g"); await WhenMessage("hs04-w"); await ThenCall("hs04-t"); }
    [Fact] public async Task HS05_explicit_first_in_when() { await GivenCall("hs05-g"); await WhenExplicitFirst("hs05-w"); await ThenCall("hs05-t"); }
    [Fact] public async Task HS06_prep_then_explicit_in_when() { await GivenCall("hs06-g"); await WhenPrepThenExplicit("hs06-prep", "hs06-w"); await ThenCall("hs06-t"); }
    [Fact] public async Task HS07_worker_queued_in_given_lands_in_when() { await GivenQueued("hs07-g"); await WhenCall("hs07-w"); await ThenSettles(); await ThenCall("hs07-t"); }
    [Fact] public async Task HS08_worker_queued_in_when_lands_after() { await GivenCall("hs08-g"); await WhenQueued("hs08-w"); await ThenSettles(); await ThenCall("hs08-t"); }
    [Fact] public async Task HS09_no_step_at_all() { await C.GetAsync("/call/hs09-a"); await C.GetAsync("/call/hs09-b"); }
    [Fact] public async Task HS10_call_in_a_plain_body_then_given_when() { await C.GetAsync("/call/hs10-before"); await GivenCall("hs10-g"); await WhenCall("hs10-w"); }
}

public class OnShared() : HostSteps(Hosts.SharedClient.Value)
{
    [Fact] public async Task HS11_shared_client_one() { await GivenCall("hs11-g"); await WhenCall("hs11-w"); await ThenCall("hs11-t"); }
    [Fact] public async Task HS12_shared_client_two() { await GivenCall("hs12-g"); await WhenCall("hs12-w"); await ThenCall("hs12-t"); }
    [Fact] public async Task HS13_shared_client_three() { await GivenCall("hs13-g"); await WhenCall("hs13-w"); await ThenCall("hs13-t"); }
}

public class OnNoSetup() : HostSteps(Hosts.Client(Hosts.NoSetup.Value, trackDuringSetup: false))
{
    [Fact] public async Task HS21_no_setup_given_when_then() { await GivenCall("hs21-g"); await WhenCall("hs21-w"); await ThenCall("hs21-t"); }
    [Fact] public async Task HS22_no_setup_given_message() { await GivenMessage("hs22-g"); await WhenMessage("hs22-w"); }
}

public class OnPreserve() : HostSteps(Hosts.Client(Hosts.Preserve.Value))
{
    [Fact] public async Task HS31_preserved_flow_given_when_then() { await GivenCall("hs31-g"); await WhenCall("hs31-w"); await ThenCall("hs31-t"); }
    [Fact] public async Task HS32_preserved_flow_message() { await GivenMessage("hs32-g"); await WhenMessage("hs32-w"); }
}

public class OnPreserveNoSetup() : HostSteps(Hosts.Client(Hosts.PreserveNoSetup.Value, trackDuringSetup: false))
{
    [Fact] public async Task HS41_preserved_no_setup_given_when_then() { await GivenCall("hs41-g"); await WhenCall("hs41-w"); await ThenCall("hs41-t"); }
}
