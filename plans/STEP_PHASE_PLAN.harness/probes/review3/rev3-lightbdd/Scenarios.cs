using Kronikol;
using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit2;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Framework.Configuration;
using LightBDD.Framework;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit2;
using PhaseProbe;

[assembly: PhaseProbe.Rev3.LightBdd.ProbeScope]
[assembly: ClassCollectionBehavior(AllowTestParallelization = false)]
[assembly: TrackSteps]

namespace PhaseProbe.Rev3.LightBdd;

internal class ProbeScopeAttribute : LightBddScopeAttribute
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.StepTypeConfiguration().UpdatePredefinedStepTypes("given", "when", "then", "and", "but", "setup");
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Rev3 probe", SeparateSetup = true });
    }

    protected override void OnTearDown()
    {
        Dump2.Write("rev3-lightbdd");
    }
}

public class Scenarios : FeatureFixture
{
    private readonly Calls C = new(
        new HttpClient(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher }));
    private readonly Calls NoSetup = new(
        new HttpClient(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = false }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;

    [Step] public async Task WorkerStep(string label) { await C.Make(label + "-worker-start"); await _gate.Task; await C.Make(label + "-worker-end"); }
    [ThenStep] public async Task PollThen(string label) => await C.Make(label);
    [GivenStep] public async Task PollGiven(string label) => await C.Make(label);
    [Step] public async Task PollPlain(string label) => await NoSetup.Make(label);

    private async Task Given_a_call_named(string label) => await C.Make(label);
    private async Task And_a_call_named(string label) => await C.Make(label);
    private async Task And_a_nosetup_call_named(string label) => await NoSetup.Make(label);
    private async Task When_a_call_named(string label) => await C.Make(label);
    private async Task Then_a_call_named(string label) => await C.Make(label);
    private async Task But_a_call_named(string label) => await C.Make(label);
    private async Task A_nosetup_call_named(string label) => await NoSetup.Make(label);
    private async Task A_call_named(string label) => await C.Make(label);

    private Task Given_a_worker_left_running(string label) { _worker = WorkerStep(label); return Task.CompletedTask; }
    private async Task Then_the_worker_is_released() { _gate.SetResult(); await _worker!; }

    private Task Given_a_Then_loop_starts(string label)
    {
        _worker = Task.Run(async () =>
        {
            var i = 0;
            while (!_stop.IsCancellationRequested) { await PollThen($"{label}-poll{i++}"); await Task.Delay(15); }
        });
        return Task.Delay(60);
    }

    private Task Given_a_Given_loop_starts(string label)
    {
        _worker = Task.Run(async () =>
        {
            var i = 0;
            while (!_stop.IsCancellationRequested) { await PollGiven($"{label}-poll{i++}"); await Task.Delay(15); }
        });
        return Task.Delay(30);
    }

    private async Task Then_the_loop_is_stopped() { _stop.Cancel(); await _worker!; }

    private async Task Given_a_step_sets_the_phase_directly(string label)
    {
        await C.Make(label);
        TestPhaseContext.Current = TestPhase.Action;
    }

    private async Task Given_a_step_calls_StartAction(string label)
    {
        await C.Make(label);
        TrackingDiagramOverride.StartAction();
    }

    [Scenario]
    public async Task M01_a_Given_leaves_a_worker_running() => await Runner.RunScenarioAsync(
        _ => Given_a_worker_left_running("m01"),
        _ => When_a_call_named("m01-when"),
        _ => And_a_call_named("m01-when-and"),
        _ => Then_a_call_named("m01-then"),
        _ => Then_the_worker_is_released());

    // A background Then loop moves the test's phase; the next AND reads it, and a call in an AND on a TrackDuringSetup=false client.
    [Scenario]
    public async Task M02_a_background_Then_loop_then_an_And_in_setup() => await Runner.RunScenarioAsync(
        _ => Given_a_Then_loop_starts("m02"),
        _ => And_a_call_named("m02-given-and"),
        _ => When_a_call_named("m02-when"),
        _ => Then_a_call_named("m02-then"),
        _ => Then_the_loop_is_stopped());

    // A background Given loop in Action; the next AND reads Setup. On a TrackDuringSetup=false client it is dropped.
    [Scenario]
    public async Task M03_a_background_Given_loop_then_an_And_in_action() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("m03-given"),
        _ => When_a_call_named("m03-when"),
        _ => Given_a_Given_loop_starts("m03"),
        _ => When_a_call_named("m03-second-when"),
        _ => Then_a_call_named("m03-then"),
        _ => And_a_nosetup_call_named("m03-then-and-nosetup"),
        _ => A_nosetup_call_named("m03-untyped-nosetup"),
        _ => Then_the_loop_is_stopped());

    // A When left running that nests nothing, then an AND: LightBDD's flows.
    [Scenario]
    public async Task M04_phase_set_directly_in_a_Given_then_an_And() => await Runner.RunScenarioAsync(
        _ => Given_a_step_sets_the_phase_directly("m04-given"),
        _ => And_a_call_named("m04-and"),
        _ => Then_a_call_named("m04-then"));

    [Scenario]
    public async Task M05_StartAction_in_a_Given_then_an_And_and_a_When() => await Runner.RunScenarioAsync(
        _ => Given_a_step_calls_StartAction("m05-given"),
        _ => And_a_call_named("m05-and"),
        _ => When_a_call_named("m05-when"));

    // Untyped steps only, after nothing.
    [Scenario]
    public async Task M06_untyped_steps_only() => await Runner.RunScenarioAsync(
        _ => A_call_named("m06-a"),
        _ => A_nosetup_call_named("m06-b"));
}
