using Kronikol;
using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit3;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Framework;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;
using PhaseProbe;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(PhaseProbe.Lb.Scope))]
[assembly: TrackSteps]

namespace PhaseProbe.Lb;

public class Scope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.StepTypeConfiguration().UpdatePredefinedStepTypes("given", "when", "then", "and", "but", "setup");
#pragma warning disable CS0618
        if (Environment.GetEnvironmentVariable("PROBE_LEGACY") == "on")
            configuration.ReportWritersConfiguration().CreateStandardReportsWithDiagrams(Options());
        else
            configuration.CreateStandardReportsWithDiagrams(Options());
#pragma warning restore CS0618
        configuration.ProgressNotifierConfiguration().Clear();
    }

    private static ReportConfigurationOptions Options() => new() { SpecificationsTitle = "Phase probe", SeparateSetup = true, DiagnosticMode = true };

    protected override void OnTearDown()
    {
        Dump.Write(Environment.GetEnvironmentVariable("PROBE_LEGACY") == "on" ? "lb-legacy" : "lb");
    }
}

public static class Shared
{
    public static readonly HttpClient Http = new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });

    public static MessageTracker Tracker() => new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher });
}

public class Base : FeatureFixture
{
    protected Calls C = new(
        new HttpClient(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        Shared.Tracker());

    protected async Task Given_a_call(string l) => await C.Make(l);
    protected async Task And_a_call(string l) => await C.Make(l);
    protected async Task But_a_call(string l) => await C.Make(l);
    protected async Task When_a_call(string l) => await C.Make(l);
    protected async Task Then_a_call(string l) => await C.Make(l);
    protected async Task Setup_a_call(string l) => await C.Make(l);
    protected async Task A_call(string l) => await C.Make(l);
    protected Task Given_a_message(string l) { C.Message(l); return Task.CompletedTask; }
    protected Task When_a_message(string l) { C.Message(l); return Task.CompletedTask; }
    protected Task When_nothing() => Task.CompletedTask;
    protected Task Given_nothing() => Task.CompletedTask;
    protected async Task When_StartAction_then_a_call(string l) { TrackingDiagramOverride.StartAction(); await C.Make(l); }
    protected async Task When_a_call_then_StartAction_then_a_call(string a, string b) { await C.Make(a); TrackingDiagramOverride.StartAction(); await C.Make(b); }
    protected async Task When_a_call_then_StartAction(string a) { await C.Make(a); TrackingDiagramOverride.StartAction(); }
    protected async Task Then_StartAction_then_a_call(string l) { TrackingDiagramOverride.StartAction(); await C.Make(l); }
    protected async Task And_StartAction_then_a_call(string l) { TrackingDiagramOverride.StartAction(); await C.Make(l); }
    protected async Task When_a_bypassed_step() { StepExecution.Current.Bypass("bypassed"); await Task.Yield(); }
    protected async Task Given_a_bypassed_step() { StepExecution.Current.Bypass("bypassed"); await Task.Yield(); }
    protected async Task Given_an_ignored_scenario(string l) { await C.Make(l); StepExecution.Current.IgnoreScenario("not today"); }
    protected async Task When_a_failing_step(string l) { await C.Make(l); throw new InvalidOperationException("fails on purpose"); }

    protected async Task<CompositeStep> Given_a_composite_with_a_when(string l)
    {
        await C.Make(l + "-body");
        return CompositeStep.DefineNew().AddAsyncSteps(_ => When_a_call(l + "-when-sub"), _ => A_call(l + "-plain-sub")).Build();
    }

    protected async Task<CompositeStep> When_a_composite_with_a_given(string l)
    {
        await C.Make(l + "-body");
        return CompositeStep.DefineNew().AddAsyncSteps(_ => Given_a_call(l + "-given-sub"), _ => A_call(l + "-plain-sub")).Build();
    }

    protected async Task<CompositeStep> When_a_composite_with_StartAction(string l)
    {
        await C.Make(l + "-body");
        return CompositeStep.DefineNew().AddAsyncSteps(_ => A_call(l + "-prep-sub"), _ => When_StartAction_then_a_call(l + "-act-sub")).Build();
    }

    protected async Task<CompositeStep> A_composite_with_given_and_when(string l)
    {
        return CompositeStep.DefineNew().AddAsyncSteps(_ => Given_a_call(l + "-given-sub"), _ => When_a_call(l + "-when-sub")).Build();
    }
}

public class Shapes : Base
{
    [Scenario] public Task LB01_control() => Runner.RunScenarioAsync(_ => Given_a_call("lb01-g"), _ => When_a_call("lb01-w"), _ => Then_a_call("lb01-t"));
    [Scenario] public Task LB02_message_given_no_call_when() => Runner.RunScenarioAsync(_ => Given_a_message("lb02-g"), _ => When_nothing(), _ => Then_a_call("lb02-t"));
    [Scenario] public Task LB03_composite_given_with_when() => Runner.RunScenarioAsync(_ => Given_a_composite_with_a_when("lb03"), _ => When_a_call("lb03-w"), _ => Then_a_call("lb03-t"));
    [Scenario] public Task LB04_composite_when_with_given() => Runner.RunScenarioAsync(_ => Given_a_call("lb04-g"), _ => When_a_composite_with_a_given("lb04"), _ => Then_a_call("lb04-t"));
    [Scenario] public Task LB05_explicit_first_in_when() => Runner.RunScenarioAsync(_ => Given_a_call("lb05-g"), _ => When_StartAction_then_a_call("lb05-w"), _ => Then_a_call("lb05-t"));
    [Scenario] public Task LB06_explicit_mid_when() => Runner.RunScenarioAsync(_ => Given_a_call("lb06-g"), _ => When_a_call_then_StartAction_then_a_call("lb06-prep", "lb06-w"), _ => Then_a_call("lb06-t"));
    [Scenario] public Task LB07_explicit_last_in_when() => Runner.RunScenarioAsync(_ => Given_a_call("lb07-g"), _ => When_a_call_then_StartAction("lb07-w"), _ => Then_a_call("lb07-t"));
    [Scenario] public Task LB08_explicit_in_later_step() => Runner.RunScenarioAsync(_ => Given_a_call("lb08-g"), _ => When_a_message("lb08-prep"), _ => And_StartAction_then_a_call("lb08-w"), _ => Then_a_call("lb08-t"));
    [Scenario] public Task LB09_explicit_nested_in_composite_when() => Runner.RunScenarioAsync(_ => Given_a_call("lb09-g"), _ => When_a_composite_with_StartAction("lb09"), _ => Then_a_call("lb09-t"));
    [Scenario] public Task LB10_bypassed_when_then_given_when() => Runner.RunScenarioAsync(_ => Given_a_call("lb10-g1"), _ => When_a_bypassed_step(), _ => Given_a_call("lb10-g2"), _ => When_a_call("lb10-w"));
    [Scenario] public Task LB11_bypassed_when_first() => Runner.RunScenarioAsync(_ => When_a_bypassed_step(), _ => Given_a_call("lb11-g"), _ => When_a_call("lb11-w"));
    [Scenario] public Task LB12_ignored_in_given() => Runner.RunScenarioAsync(_ => Given_an_ignored_scenario("lb12-g"), _ => When_a_call("lb12-w"));
    [Scenario] public Task LB13_and_first() => Runner.RunScenarioAsync(_ => And_a_call("lb13-a"), _ => When_a_call("lb13-w"), _ => Then_a_call("lb13-t"));
    [Scenario] public Task LB14_custom_setup_word() => Runner.RunScenarioAsync(_ => Setup_a_call("lb14-s"), _ => When_a_call("lb14-w"));
    [Scenario] public Task LB15_keyword_free_steps_only() => Runner.RunScenarioAsync(_ => A_call("lb15-a"), _ => A_call("lb15-b"));
    [Scenario] public Task LB16_keyword_free_composite_with_given_when() => Runner.RunScenarioAsync(_ => A_composite_with_given_and_when("lb16"), _ => Then_a_call("lb16-t"));
    [Scenario] public Task LB17_when_then_given_when() => Runner.RunScenarioAsync(_ => When_a_call("lb17-w1"), _ => Given_a_call("lb17-g"), _ => When_a_call("lb17-w2"));
    [Scenario] public Task LB18_but_after_given() => Runner.RunScenarioAsync(_ => Given_a_call("lb18-g"), _ => But_a_call("lb18-b"), _ => When_a_call("lb18-w"), _ => But_a_call("lb18-b2"));
    [Scenario] public Task LB19_when_fails() => Runner.RunScenarioAsync(_ => Given_a_call("lb19-g"), _ => When_a_failing_step("lb19-w"), _ => Then_a_call("lb19-t"));
    [Scenario] public Task LB20_explicit_in_then() => Runner.RunScenarioAsync(_ => Given_a_call("lb20-g"), _ => When_a_message("lb20-prep"), _ => Then_StartAction_then_a_call("lb20-t"));
    [Scenario] public Task LB21_bypassed_given_call_when() => Runner.RunScenarioAsync(_ => Given_a_bypassed_step(), _ => A_call("lb21-a"), _ => When_a_call("lb21-w"));

    [Scenario]
    [Xunit.InlineData("a")]
    [Xunit.InlineData("b")]
    public Task LB30_parameterised(string row) => Runner.RunScenarioAsync(_ => Given_a_call($"lb30-{row}-g"), _ => When_a_call($"lb30-{row}-w"));

    private static readonly Calls OnShared = new(Shared.Http, Shared.Tracker());
    private async Task Given_shared(string l) => await OnShared.Make(l);
    private async Task When_shared(string l) => await OnShared.Make(l);
    [Scenario] public Task LB31_shared_one() => Runner.RunScenarioAsync(_ => Given_shared("lb31-g"), _ => When_shared("lb31-w"));
    [Scenario] public Task LB32_shared_two() => Runner.RunScenarioAsync(_ => Given_shared("lb32-g"), _ => When_shared("lb32-w"));

    private static readonly HttpClient SharedNoSetupHttp = new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = false }) { InnerHandler = new AnswerEverything() });
    private static readonly Calls OnSharedNoSetup = new(SharedNoSetupHttp, Shared.Tracker());
    private async Task Given_shared_no_setup(string l) => await OnSharedNoSetup.Make(l);
    private async Task When_shared_no_setup(string l) => await OnSharedNoSetup.Make(l);
    private async Task Then_shared_no_setup(string l) => await OnSharedNoSetup.Make(l);
    [Scenario] public Task LB33_shared_no_setup_one() => Runner.RunScenarioAsync(_ => Given_shared_no_setup("lb33-g"), _ => When_shared_no_setup("lb33-w"), _ => When_shared_no_setup("lb33-w2"), _ => Then_shared_no_setup("lb33-t"));
    [Scenario] public Task LB34_shared_no_setup_two() => Runner.RunScenarioAsync(_ => Given_shared_no_setup("lb34-g"), _ => When_shared_no_setup("lb34-w"), _ => When_shared_no_setup("lb34-w2"), _ => Then_shared_no_setup("lb34-t"));
    [Scenario] public Task LB35_shared_no_setup_three() => Runner.RunScenarioAsync(_ => Given_shared_no_setup("lb35-g"), _ => When_shared_no_setup("lb35-w"), _ => When_shared_no_setup("lb35-w2"), _ => Then_shared_no_setup("lb35-t"));

    [GivenStep] public async Task WovenGiven(string l) => await C.Make(l);
    [WhenStep] public async Task WovenWhen(string l) => await C.Make(l);
    private async Task A_step_with_woven_given_when(string l) { await WovenGiven(l + "-wg"); await WovenWhen(l + "-ww"); }
    [Scenario] public Task LB40_woven_under_keyword_free() => Runner.RunScenarioAsync(_ => A_step_with_woven_given_when("lb40"), _ => Then_a_call("lb40-t"));
    private async Task Given_a_step_with_woven_when(string l) { await WovenWhen(l + "-ww"); await C.Make(l + "-after"); }
    [Scenario] public Task LB41_woven_when_under_given() => Runner.RunScenarioAsync(_ => Given_a_step_with_woven_when("lb41"), _ => When_a_call("lb41-w"));
}

public class ParallelA : Base
{
    private static readonly Calls OnShared = new(Shared.Http, Shared.Tracker());
    private async Task Given_shared(string l) => await OnShared.Make(l);
    private async Task When_shared(string l) => await OnShared.Make(l);
    [Scenario] public Task PA1() => Runner.RunScenarioAsync(_ => Given_shared("pa1-g0"), _ => Given_shared("pa1-g1"), _ => When_shared("pa1-w0"), _ => When_shared("pa1-w1"));
    [Scenario] public Task PA2() => Runner.RunScenarioAsync(_ => Given_shared("pa2-g0"), _ => Given_shared("pa2-g1"), _ => When_shared("pa2-w0"), _ => When_shared("pa2-w1"));
}

public class ParallelB : Base
{
    private static readonly Calls OnShared = new(Shared.Http, Shared.Tracker());
    private async Task Given_shared(string l) => await OnShared.Make(l);
    private async Task When_shared(string l) => await OnShared.Make(l);
    [Scenario] public Task PB1() => Runner.RunScenarioAsync(_ => Given_shared("pb1-g0"), _ => Given_shared("pb1-g1"), _ => When_shared("pb1-w0"), _ => When_shared("pb1-w1"));
    [Scenario] public Task PB2() => Runner.RunScenarioAsync(_ => Given_shared("pb2-g0"), _ => Given_shared("pb2-g1"), _ => When_shared("pb2-w0"), _ => When_shared("pb2-w1"));
}
