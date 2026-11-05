using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using Xunit;

[assembly: AssemblyFixture(typeof(PhaseProbe.Bf.Setup))]
[assembly: TrackSteps]

namespace PhaseProbe.Bf;

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
        Dump.Write("bf");
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true, DiagnosticMode = true });
        return ValueTask.CompletedTask;
    }
}

public class Scenarios
{
    private static readonly HttpClient SharedHttp = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });

    private static MessageTracker Tracker() => new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher });

    private Calls C = new(new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }), Tracker());

    public void Given_a_call(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task Given_an_async_call(string l) => await C.Make(l);
    public void Given_a_message(string l) => C.Message(l);
    public void When_a_call(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task When_an_async_call(string l) => await C.Make(l);
    public void When_nothing() { }
    public void Then_a_call(string l) => C.Make(l).GetAwaiter().GetResult();
    public void When_StartAction_then_a_call(string l) { TrackingDiagramOverride.StartAction(); C.Make(l).GetAwaiter().GetResult(); }
    public void When_a_call_then_StartAction_then_a_call(string a, string b) { C.Make(a).GetAwaiter().GetResult(); TrackingDiagramOverride.StartAction(); C.Make(b).GetAwaiter().GetResult(); }
    public void Then_StartAction_then_a_call(string l) { TrackingDiagramOverride.StartAction(); C.Make(l).GetAwaiter().GetResult(); }
    public void Teardown_a_call(string l) => C.Make(l).GetAwaiter().GetResult();

    [Fact] public void F01_control() => this.Given(x => x.Given_a_call("f01-g")).When(x => x.When_a_call("f01-w")).Then(x => x.Then_a_call("f01-t")).BDDfy();
    [Fact] public void F02_async_steps() => this.Given(x => x.Given_an_async_call("f02-g")).When(x => x.When_an_async_call("f02-w")).Then(x => x.Then_a_call("f02-t")).BDDfy();
    [Fact] public void F03_message_given_no_call_when() => this.Given(x => x.Given_a_message("f03-g")).When(x => x.When_nothing()).Then(x => x.Then_a_call("f03-t")).BDDfy();
    [Fact] public void F04_explicit_first_in_when() => this.Given(x => x.Given_a_call("f04-g")).When(x => x.When_StartAction_then_a_call("f04-w")).Then(x => x.Then_a_call("f04-t")).BDDfy();
    [Fact] public void F05_explicit_mid_when() => this.Given(x => x.Given_a_call("f05-g")).When(x => x.When_a_call_then_StartAction_then_a_call("f05-prep", "f05-w")).Then(x => x.Then_a_call("f05-t")).BDDfy();
    [Fact] public void F06_explicit_in_then() => this.Given(x => x.Given_a_call("f06-g")).When(x => x.When_nothing()).Then(x => x.Then_StartAction_then_a_call("f06-t")).BDDfy();
    [Fact] public void F07_two_stories() { this.Given(x => x.Given_a_call("f07-g1")).When(x => x.When_a_call("f07-w1")).BDDfy(); this.Given(x => x.Given_a_call("f07-g2")).When(x => x.When_a_call("f07-w2")).BDDfy(); }
    [Fact] public void F08_teardown() => this.Given(x => x.Given_a_call("f08-g")).When(x => x.When_a_call("f08-w")).TearDownWith(x => x.Teardown_a_call("f08-td")).BDDfy();
    [Fact] public void F09_body_before_and_after_the_story() { C.Make("f09-before").GetAwaiter().GetResult(); this.Given(x => x.Given_a_call("f09-g")).When(x => x.When_a_call("f09-w")).BDDfy(); C.Make("f09-after").GetAwaiter().GetResult(); }
    [Fact] public void F10_explicit_before_the_story() { TrackingDiagramOverride.StartSetup(); C.Make("f10-before").GetAwaiter().GetResult(); TrackingDiagramOverride.StartAction(); this.Given(x => x.Given_a_call("f10-g")).When(x => x.When_a_call("f10-w")).BDDfy(); }
    [Fact] public void F11_shared_one() { C = new Calls(SharedHttp, Tracker()); this.Given(x => x.Given_a_call("f11-g")).When(x => x.When_a_call("f11-w")).BDDfy(); }
    [Fact] public void F12_shared_two() { C = new Calls(SharedHttp, Tracker()); this.Given(x => x.Given_a_call("f12-g")).When(x => x.When_a_call("f12-w")).BDDfy(); }
    [Fact] public void F13_failing_when() { try { this.Given(x => x.Given_a_call("f13-g")).When(x => x.When_a_call_then_throw("f13-w")).BDDfy(); } catch { } }
    public void When_a_call_then_throw(string l) { C.Make(l).GetAwaiter().GetResult(); throw new InvalidOperationException("x"); }
}
