using Kronikol;
using Kronikol.LightBDD;
using Kronikol.Tracking;
using LightBDD.Framework;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;
using PhaseProbe;

namespace PhaseProbe.Lb;

public class R4 : Base
{
    private async Task A_step_with_a_woven_when(string l) { await WovenWhenHere(l + "-ww"); }
    [WhenStep] public async Task WovenWhenHere(string l) => await C.Make(l);
    private async Task When_with_no_setup(string l) => await OffCalls.Make(l);
    private async Task Given_with_no_setup(string l) => await OffCalls.Make(l);
    private async Task And_with_no_setup(string l) => await OffCalls.Make(l);
    private async Task Setup_with_no_setup(string l) => await OffCalls.Make(l);
    private async Task Then_with_no_setup(string l) => await OffCalls.Make(l);
    private readonly Calls OffCalls = new(new HttpClient(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = false }) { InnerHandler = new AnswerEverything() }), Shared.Tracker());

    private async Task Whenever_a_call(string l) => await C.Make(l);
    private async Task Givenall_a_call(string l) => await C.Make(l);
    private async Task Thenalso_a_call(string l) => await C.Make(l);
    private async Task Whenever_with_no_action(string l) => await NoActionCalls.Make(l);
    private async Task Givenall_with_no_setup(string l) => await OffCalls.Make(l);
    private readonly Calls NoActionCalls = new(new HttpClient(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringAction = false }) { InnerHandler = new AnswerEverything() }), Shared.Tracker());
    [Scenario] public Task LB59_a_suites_word_that_starts_with_when_after_a_given() => Runner.RunScenarioAsync(_ => Given_a_call("lb59-g"), _ => Whenever_a_call("lb59-w"), _ => Thenalso_a_call("lb59-t"));
    [Scenario] public Task LB60_a_suites_word_that_starts_with_given_then_when() => Runner.RunScenarioAsync(_ => Givenall_a_call("lb60-g"), _ => When_a_call("lb60-w"));
    [Scenario] public Task LB61_words_with_the_tracking_off() => Runner.RunScenarioAsync(_ => Givenall_with_no_setup("lb61-g"), _ => Whenever_with_no_action("lb61-w"), _ => Then_a_call("lb61-t"));

    private async Task Given_a_call_then_the_step_list_cleared(string l) { await C.Make(l); StepCollector.ClearSteps(LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher().Id); }
    [Scenario] public Task LB62_steps_cleared_in_a_given() => Runner.RunScenarioAsync(_ => Given_a_call_then_the_step_list_cleared("lb62-g"), _ => When_a_call("lb62-w"), _ => Then_a_call("lb62-t"));

    private Task Given_StartSetup_by_hand_then_a_call(string l) { TrackingDiagramOverride.StartSetup(); return C.Make(l); }
    private Task A_step_with_a_hand_written_phase(TestPhase phase) { TestPhaseContext.Current = phase; return Task.CompletedTask; }

    [Scenario] public async Task LB50_setup_written_before_the_runner_then_given_when_and() { TestPhaseContext.Current = TestPhase.Setup; await Runner.RunScenarioAsync(_ => Given_a_call("lb50-g"), _ => When_a_call("lb50-w"), _ => And_with_no_setup("lb50-a"), _ => And_a_call("lb50-a2")); }
    [Scenario] public async Task LB51_action_written_before_the_runner_then_given_and() { TestPhaseContext.Current = TestPhase.Action; await Runner.RunScenarioAsync(_ => Given_a_call("lb51-g"), _ => And_a_call("lb51-a"), _ => When_a_call("lb51-w")); }
    [Scenario] public Task LB52_top_level_keyword_free_with_woven_when_after_a_given() => Runner.RunScenarioAsync(_ => Given_a_call("lb52-g"), _ => A_step_with_a_woven_when("lb52"), _ => A_call("lb52-loose"), _ => Then_a_call("lb52-t"));
    [Scenario] public Task LB53_setup_word_with_TrackDuringSetup_off() => Runner.RunScenarioAsync(_ => Setup_with_no_setup("lb53-s"), _ => When_with_no_setup("lb53-w"));
    [Scenario] public Task LB54_when_and_with_TrackDuringSetup_off() => Runner.RunScenarioAsync(_ => Given_with_no_setup("lb54-g"), _ => When_with_no_setup("lb54-w"), _ => And_with_no_setup("lb54-a"), _ => Then_with_no_setup("lb54-t"), _ => And_with_no_setup("lb54-a2"));
    [Scenario] public Task LB55_composite_when_with_given_and_TrackDuringSetup_off() => Runner.RunScenarioAsync(_ => When_composite_off("lb55"), _ => Then_with_no_setup("lb55-t"));
    private async Task<CompositeStep> When_composite_off(string l) { await OffCalls.Make(l + "-body"); return CompositeStep.DefineNew().AddAsyncSteps(_ => Given_with_no_setup(l + "-gsub"), _ => A_call_off(l + "-plainsub")).Build(); }
    private async Task A_call_off(string l) => await OffCalls.Make(l);
    [Scenario] public Task LB56_keyword_free_step_between_messages() => Runner.RunScenarioAsync(_ => Given_a_message("lb56-g"), _ => A_call("lb56-loose"), _ => When_a_message("lb56-w"), _ => Then_a_call("lb56-t"));
    [Scenario] public Task LB57_explicit_setup_then_when_and() => Runner.RunScenarioAsync(_ => Given_StartSetup_by_hand_then_a_call("lb57-g"), _ => When_a_call("lb57-w"), _ => And_a_call("lb57-a"));
    [Scenario] public Task LB58_a_hand_written_phase_in_a_step_does_not_flow_back() => Runner.RunScenarioAsync(_ => Given_a_call("lb58-g"), _ => A_step_with_a_hand_written_phase(TestPhase.Action), _ => And_a_call("lb58-a"), _ => When_a_call("lb58-w"));
}
