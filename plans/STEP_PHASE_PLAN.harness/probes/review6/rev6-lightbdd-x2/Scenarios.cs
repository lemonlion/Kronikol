using Kronikol;
using Kronikol.LightBDD;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Framework;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using Rev5;
using Kronikol.LightBDD.xUnit2;
using LightBDD.XUnit2;

[assembly: Rev6.LightBdd.ProbeScope]
[assembly: ClassCollectionBehavior(AllowTestParallelization = false)]
[assembly: TrackSteps]

namespace Rev6.LightBdd;

internal class ProbeScopeAttribute : LightBddScopeAttribute
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.StepTypeConfiguration().UpdatePredefinedStepTypes("given", "when", "then", "and", "but", "setup", "arrange");
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Rev6 probe", SeparateSetup = true });
    }

    protected override void OnTearDown()
    {
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "rev6-lightbdd-x2");
    }
}

public class Scenarios : FeatureFixture
{
    private static HttpClient NewClient(bool trackDuringSetup = true, bool trackDuringAction = true) => new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = trackDuringSetup,
        TrackDuringAction = trackDuringAction
    }) { InnerHandler = new Answer() });

    private readonly HttpClient _http = NewClient();
    private readonly HttpClient _noSetup = NewClient(trackDuringSetup: false);
    private readonly HttpClient _noAction = NewClient(trackDuringAction: false);
    private TaskCompletionSource _gate = new();
    private Task _left = Task.CompletedTask;

    private async Task Call(string label) => (await _http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    private async Task All(string label)
    {
        await Call(label);
        await _noSetup.GetAsync($"http://probe.local/{label}-nosetup");
        await _noAction.GetAsync($"http://probe.local/{label}-noaction");
    }

    public bool Yes => true;
    public bool GetterCalls { get { All("getter").GetAwaiter().GetResult(); return false; } }
    [GivenStep] public async Task WovenSlow(string label, Task gate) { await gate; await Call(label); }
    [GivenStep] public async Task WovenGiven(string label) => await All(label);
    [GivenStep(SkipIf = nameof(Yes))] public void SkipSync(string l) => Call(l).GetAwaiter().GetResult();
    [GivenStep(SkipIf = nameof(Yes))] public async Task SkipAsync(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(Yes))] public Task SkipTask(string l) => Call(l);
    [WhenStep(SkipIf = nameof(Yes))] public async Task<int> SkipWhenInt(string l) { await Call(l); return 1; }
    [GivenStep(SkipIf = nameof(GetterCalls))] public async Task WovenGivenWithGetter(string l) => await Call(l);

    private Task Given_a_call_named(string label) => All(label);
    private Task When_a_call_named(string label) => All(label);
    private Task Then_a_call_named(string label) => All(label);
    private async Task Then_a_marking_check_named(string label) { TrackingDiagramOverride.StartAction(); await All(label); }
    private async Task When_a_step_leaves_a_woven_given_running(string label)
    {
        _gate = new TaskCompletionSource();
        _left = WovenSlow(label + "-left", _gate.Task);
        await All(label);
    }

    private async Task Then_the_left_step_is_released(string label)
    {
        _gate.TrySetResult();
        await _left;
        await All(label);
    }

    private async Task When_a_step_runs_skipped_woven_steps(string label)
    {
        await All(label + "-before");
        SkipSync(label + "-skip-sync");
        await All(label + "-after-sync");
        await SkipAsync(label + "-skip-async");
        await All(label + "-after-async");
        await SkipTask(label + "-skip-task");
        await SkipWhenInt(label + "-skip-when-int");
        await WovenGiven(label + "-sub");
        await All(label + "-after");
    }

    private async Task Given_a_step_runs_a_woven_given_with_a_getter(string label)
    {
        await WovenGivenWithGetter(label);
    }

    private async Task When_a_step_bypasses_by_hand(string label)
    {
        await All(label + "-before");
        StepCollector.StartStep("Given", label + "-manual", null, null);
        StepCollector.BypassStep("manual");
        await All(label);
    }

    private static readonly HttpClient SharedClient = NewClient();
    private Task And_a_call_named(string label) => All(label);
    private Task And_no_call() => Task.CompletedTask;
    private Task But_a_call_named(string label) => All(label);
    private Task When_no_call() => Task.CompletedTask;
    private async Task When_only_a_late_client_calls(string label) { await _noSetup.GetAsync($"http://probe.local/{label}-late-nosetup"); await All(label); }
    private async Task And_the_shared_client_calls(string label) => (await SharedClient.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    private async Task When_the_shared_client_calls(string label) => (await SharedClient.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();

    [Scenario] public async Task L44_And_first_with_calls_then_When() => await Runner.RunScenarioAsync(
        _ => And_a_call_named("l44-and"),
        _ => When_a_call_named("l44-act"),
        _ => Then_a_call_named("l44-then"));

    [Scenario] public async Task L45_And_first_with_no_call_then_When() => await Runner.RunScenarioAsync(
        _ => And_no_call(),
        _ => When_a_call_named("l45-act"),
        _ => Then_a_call_named("l45-then"));

    [Scenario] public async Task L46_And_first_then_a_When_whose_first_call_is_a_client_new_to_the_test() => await Runner.RunScenarioAsync(
        _ => And_a_call_named("l46-and"),
        _ => When_only_a_late_client_calls("l46-act"),
        _ => Then_a_call_named("l46-then"));

    [Scenario] public async Task L47_And_first_then_a_When_with_no_call_then_Then() => await Runner.RunScenarioAsync(
        _ => And_a_call_named("l47-and"),
        _ => When_no_call(),
        _ => Then_a_call_named("l47-then"));

    [Scenario] public async Task L48_And_first_on_a_static_client_first_test() => await Runner.RunScenarioAsync(
        _ => And_the_shared_client_calls("l48-and"),
        _ => When_the_shared_client_calls("l48-act"));

    [Scenario] public async Task L49_And_first_on_a_static_client_later_test() => await Runner.RunScenarioAsync(
        _ => And_the_shared_client_calls("l49-and"),
        _ => When_the_shared_client_calls("l49-act"));

    [Scenario] public async Task L50_But_first_then_When() => await Runner.RunScenarioAsync(
        _ => But_a_call_named("l50-but"),
        _ => When_a_call_named("l50-act"));

    [Scenario] public async Task L51_And_first_When_And_Given_When() => await Runner.RunScenarioAsync(
        _ => And_a_call_named("l51-and"),
        _ => When_a_call_named("l51-act"),
        _ => And_a_call_named("l51-and2"),
        _ => Given_a_call_named("l51-given"),
        _ => When_a_call_named("l51-act2"));

    [Scenario] public async Task L40_a_When_leaves_a_woven_step_running_a_later_Then_marks() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("l40-seed"),
        _ => When_a_step_leaves_a_woven_given_running("l40-act"),
        _ => Then_a_marking_check_named("l40-then"),
        _ => Then_the_left_step_is_released("l40-release"));

    [Scenario] public async Task L41_a_When_runs_skipped_woven_steps() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("l41-seed"),
        _ => When_a_step_runs_skipped_woven_steps("l41"),
        _ => Then_a_call_named("l41-then"));

    [Scenario] public async Task L42_a_Given_runs_a_woven_Given_whose_getter_makes_calls() => await Runner.RunScenarioAsync(
        _ => Given_a_step_runs_a_woven_given_with_a_getter("l42-seed"),
        _ => When_a_call_named("l42-act"),
        _ => Then_a_call_named("l42-then"));

    [Scenario] public async Task L43_a_When_bypasses_a_hand_step() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("l43-seed"),
        _ => When_a_step_bypasses_by_hand("l43-act"),
        _ => Then_a_call_named("l43-then"));
}
