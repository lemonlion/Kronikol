using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using Xunit;

[assembly: AssemblyFixture(typeof(Probe.Shapes.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace Probe.Shapes;

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
        Dump.Write("shapes");
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Probe shapes", SeparateSetup = true });
        return ValueTask.CompletedTask;
    }
}

public abstract class Base
{
    protected readonly Calls C;
    protected readonly HttpClient Http;

    protected Base()
    {
        Known.Register();
        Http = new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });
        C = new Calls(Http, new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher }));
    }

    protected static string Id => Known.Id;

    // woven steps a consumer's step methods call
    [GivenStep] public void WovenGivenSync(string label) => C.Make(label).GetAwaiter().GetResult();
    [GivenStep] public async Task WovenGiven(string label) => await C.Make(label);
    [WhenStep] public async Task WovenWhen(string label) => await C.Make(label);
    [ThenStep] public async Task WovenThen(string label) => await C.Make(label);
    [GivenStep] public async Task<int> WovenGivenT(string label) { await C.Make(label); return 1; }
    [WhenStep] public async Task WovenThrows(string label) { await C.Make(label); await Task.Yield(); throw new InvalidOperationException("woven " + label); }
    [GivenStep] public async Task WovenSlow(string label, int ms) { await Task.Delay(ms); await C.Make(label); }
    [GivenStep] public async Task WovenGated(string label, Task gate) { await gate; await C.Make(label); }
    public bool Always => true;
    [GivenStep(SkipIf = nameof(Always))] public async Task WovenSkipped(string label) => await C.Make(label);
}
