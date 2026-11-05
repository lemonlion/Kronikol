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

// Review 4: a step whose task BDDfy waits on closes on the executor's own continuation, not on the flow the next step starts on.
public class AsyncShapes
{
    private static MessageTracker Tracker(bool setup = true) => new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher, TrackDuringSetup = setup });

    private Calls C = new(new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }), Tracker());

    private Calls NoSetup = new(new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = false }) { InnerHandler = new AnswerEverything() }), Tracker(false));

    public async Task Given_an_async_call(string l) => await C.Make(l);
    public async Task When_an_async_call(string l) => await C.Make(l);
    public async Task Then_an_async_call(string l) => await C.Make(l);
    public async Task Given_an_async_message(string l) { await Task.Yield(); C.Message(l); }
    public async Task When_an_async_message(string l) { await Task.Yield(); C.Message(l); }
    public void Given_a_sync_call(string l) => C.Make(l).GetAwaiter().GetResult();
    public void When_a_sync_call(string l) => C.Make(l).GetAwaiter().GetResult();
    public void Then_a_sync_call(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task When_explicit_then_async_call(string a, string b) { await C.Make(a); TrackingDiagramOverride.StartAction(); await C.Make(b); }
    public async Task Given_no_setup(string l) => await NoSetup.Make(l);
    public async Task When_no_setup(string l) => await NoSetup.Make(l);
    public async Task Then_no_setup(string l) => await NoSetup.Make(l);

    public void Given_a_call_then_the_step_list_cleared(string l) { C.Make(l).GetAwaiter().GetResult(); StepCollector.ClearSteps(Xunit.TestContext.Current.Test!.UniqueID); }
    [Fact] public void A12_steps_cleared_in_a_given() => this.Given(x => x.Given_a_call_then_the_step_list_cleared("a12-g")).When(x => x.When_a_sync_call("a12-w")).Then(x => x.Then_a_sync_call("a12-t")).BDDfy();
    public Task Given_a_completed_task() { C.Message("a13-g"); return Task.CompletedTask; }
    [Fact] public void A13_a_given_that_returns_a_completed_task() => this.Given(x => x.Given_a_completed_task()).When(x => x.When_a_sync_call("a13-w")).BDDfy();

    private readonly MessageTracker Variants = new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher, SetupVerbosity = MessageTrackerVerbosity.Summarised, ActionVerbosity = MessageTrackerVerbosity.Detailed });
    private void Send(string l) => Variants.TrackSendEvent("bus", l, new Uri($"bus://probe/{l}"), new { payload = "body-" + l });
    public async Task Given_an_async_send(string l) { await Task.Yield(); Send(l); }
    public void When_a_sync_send(string l) => Send(l);
    public async Task When_an_async_send(string l) { await Task.Yield(); Send(l); }
    public void Then_a_sync_send(string l) => Send(l);
    public async Task Then_an_async_send(string l) { await Task.Yield(); Send(l); }
    [Fact] public void A14_per_phase_detail_async_given_sync_when_sync_then() => this.Given(x => x.Given_an_async_send("a14-g")).When(x => x.When_a_sync_send("a14-w")).Then(x => x.Then_a_sync_send("a14-t")).BDDfy();
    [Fact] public void A15_per_phase_detail_all_async() => this.Given(x => x.Given_an_async_send("a15-g")).When(x => x.When_an_async_send("a15-w")).Then(x => x.Then_an_async_send("a15-t")).BDDfy();

    [Fact] public void A01_async_given_sync_when_sync_then() => this.Given(x => x.Given_an_async_call("a01-g")).When(x => x.When_a_sync_call("a01-w")).Then(x => x.Then_a_sync_call("a01-t")).BDDfy();
    [Fact] public void A02_all_async() => this.Given(x => x.Given_an_async_call("a02-g")).When(x => x.When_an_async_call("a02-w")).Then(x => x.Then_an_async_call("a02-t")).BDDfy();
    [Fact] public void A03_sync_given_async_when_async_then() => this.Given(x => x.Given_a_sync_call("a03-g")).When(x => x.When_an_async_call("a03-w")).Then(x => x.Then_an_async_call("a03-t")).BDDfy();
    [Fact] public void A04_all_sync_control() => this.Given(x => x.Given_a_sync_call("a04-g")).When(x => x.When_a_sync_call("a04-w")).Then(x => x.Then_a_sync_call("a04-t")).BDDfy();
    [Fact] public void A05_async_given_two_sync_whens() => this.Given(x => x.Given_an_async_call("a05-g")).When(x => x.When_a_sync_call("a05-w1")).And(x => x.When_a_sync_call("a05-w2")).Then(x => x.Then_a_sync_call("a05-t")).BDDfy();
    [Fact] public void A06_two_async_givens_async_when() => this.Given(x => x.Given_an_async_call("a06-g1")).And(x => x.Given_an_async_call("a06-g2")).When(x => x.When_an_async_call("a06-w")).Then(x => x.Then_a_sync_call("a06-t")).BDDfy();
    [Fact] public void A07_message_only_async() => this.Given(x => x.Given_an_async_message("a07-g")).When(x => x.When_an_async_message("a07-w")).Then(x => x.Then_a_sync_call("a07-t")).BDDfy();
    [Fact] public void A08_async_given_explicit_mid_async_when() => this.Given(x => x.Given_an_async_call("a08-g")).When(x => x.When_explicit_then_async_call("a08-prep", "a08-act")).Then(x => x.Then_a_sync_call("a08-t")).BDDfy();
    [Fact] public void A09_async_no_setup_tracking() => this.Given(x => x.Given_no_setup("a09-g")).When(x => x.When_no_setup("a09-w")).Then(x => x.Then_no_setup("a09-t")).BDDfy();
    [Fact] public void A10_sync_given_async_given_sync_when() => this.Given(x => x.Given_a_sync_call("a10-g1")).And(x => x.Given_an_async_call("a10-g2")).When(x => x.When_a_sync_call("a10-w")).BDDfy();
    [Fact] public async Task A11_async_story_in_an_async_test() { await Task.Yield(); this.Given(x => x.Given_an_async_call("a11-g")).When(x => x.When_an_async_call("a11-w")).Then(x => x.Then_an_async_call("a11-t")).BDDfy(); }
}
