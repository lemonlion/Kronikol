using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit2;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: TestFramework("Kronikol.xUnit2.ReportingTestFramework", "Kronikol.xUnit2")]

namespace PhaseProbe.X2;

public sealed class Run : DiagrammedTestRun, IDisposable
{
    public void Dispose() => Dump.Write("x2");
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Def : ICollectionFixture<Run> { }

public abstract class Steps : DiagrammedComponentTest
{
    protected readonly Calls C = NewCalls();
    protected static Calls NewCalls(bool setup = true) => new(
        new HttpClient(new TestTrackingMessageHandler(new XUnit2TestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = setup }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnit2TestTrackingMessageHandlerOptions.TestInfoFetcher, TrackDuringSetup = setup }));

    protected static string Id => XUnit2TestTrackingContext.GetCurrentTestInfo().Id;
    [GivenStep] public async Task GivenCall(string l) => await C.Make(l);
    [WhenStep] public async Task WhenCall(string l) => await C.Make(l);
    [WhenStep] public void WhenOverGiven(string l) { GivenCall(l + "-sub").GetAwaiter().GetResult(); C.Make(l).GetAwaiter().GetResult(); }
    [ThenStep] public async Task ThenCall(string l) => await C.Make(l);
}

public class Plain : Steps
{
    [Fact] public async Task Y01_control() { await GivenCall("y01-g"); await WhenCall("y01-w"); await ThenCall("y01-t"); }
    [Fact] public async Task Y02_nested() { await GivenCall("y02-g"); WhenOverGiven("y02-w"); await C.Make("y02-after"); await ThenCall("y02-t"); }
    [Fact] public async Task Y03_explicit_mid() { await GivenCall("y03-g"); await C.Make("y03-more"); TrackingDiagramOverride.StartAction(); await C.Make("y03-a"); await ThenCall("y03-t"); }
}

public class CtorSetup : Steps
{
    public CtorSetup() => TrackingDiagramOverride.StartSetup();
    [Fact] public async Task Y10_StartSetup_in_ctor_then_call_then_when() { await C.Make("y10-loose"); await WhenCall("y10-w"); await ThenCall("y10-t"); }
    [Fact] public async Task Y11_StartSetup_in_ctor_then_given_when() { await GivenCall("y11-g"); await WhenCall("y11-w"); }
}

public class CtorAction : Steps
{
    public CtorAction() => TrackingDiagramOverride.StartAction();
    [Fact] public async Task Y12_StartAction_in_ctor_then_given_when() { await GivenCall("y12-g"); await WhenCall("y12-w"); await C.Make("y12-after"); }
}

public class InitGiven : Steps, IAsyncLifetime
{
    public async Task InitializeAsync() => await GivenCall("y20-init-given-has-no-test");
    public Task DisposeAsync() => Task.CompletedTask;
    [Fact] public async Task Y20_given_in_InitializeAsync_before_the_attribute() { await WhenCall("y20-w"); await ThenCall("y20-t"); }
}

public class NoAttribute
{
    [GivenStep] public async Task GivenOn(HttpClient c, string l) => await c.GetAsync("http://probe.local/" + l);
    [WhenStep] public async Task WhenOn(HttpClient c, string l) => await c.GetAsync("http://probe.local/" + l);

    [Fact]
    public async Task Y30_scope_only_identity_in_a_class_with_no_attribute()
    {
        var id = "scope-" + Guid.NewGuid().ToString("N")[..8];
        Dump.Names[id] = "Y30_scope_only";
        using var scope = TestIdentityScope.Begin("Y30", id);
        using var client = new HttpClient(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });
        await GivenOn(client, "y30-g");
        await WhenOn(client, "y30-w");
        Dump.Note(id, "flow reads " + TestPhaseContext.Current);
    }
}
