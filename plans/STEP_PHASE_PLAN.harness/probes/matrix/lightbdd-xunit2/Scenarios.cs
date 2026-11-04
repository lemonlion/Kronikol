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

[assembly: PhaseProbe.LightBdd.ProbeScope]
[assembly: ClassCollectionBehavior(AllowTestParallelization = false)]
[assembly: TrackSteps]

namespace PhaseProbe.LightBdd;

internal class ProbeScopeAttribute : LightBddScopeAttribute
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        // A suite can add step types; "but" is not one of LightBDD's own.
        configuration.StepTypeConfiguration().UpdatePredefinedStepTypes("given", "when", "then", "and", "but", "setup");
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
    }

    protected override void OnTearDown()
    {
        Dump.Write("lightbdd-xunit2");
    }
}

public class Scenarios : FeatureFixture
{
    private readonly Calls _calls = new(
        new HttpClient(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    private static readonly HttpClient SharedClient =
        new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });

    private Calls _active => _sharedActive ?? _calls;
    private Calls? _sharedActive;

    private void UseSharedHandler() =>
        _sharedActive = new Calls(SharedClient, new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    // ---- LightBDD steps ----
    private async Task Setup_a_call_named(string label) => await _active.Make(label);

    private Task A_bypassed_sub_step()
    {
        StepExecution.Current.Bypass("bypassed");
        return Task.CompletedTask;
    }

    private async Task<CompositeStep> Given_composite_with_a_bypassed_sub_step(string label)
    {
        await _active.Make($"{label}-body");
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => A_bypassed_sub_step(),
                _ => When_a_call_named($"{label}-when-sub"),
                _ => A_call_named($"{label}-untyped-sub"))
            .Build();
    }

    private async Task<CompositeStep> And_composite_with_a_Given_sub_step(string label)
    {
        await _active.Make($"{label}-body");
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => Given_a_call_named($"{label}-given-sub"))
            .Build();
    }

    private async Task Given_a_call_named(string label) => await _active.Make(label);
    private async Task And_a_call_named(string label) => await _active.Make(label);
    private async Task When_a_call_named(string label) => await _active.Make(label);
    private async Task Then_a_call_named(string label) => await _active.Make(label);
    private async Task But_a_call_named(string label) => await _active.Make(label);
    private async Task A_call_named(string label) => await _active.Make(label);
    private Task Given_a_message_named(string label) { _active.Message(label); return Task.CompletedTask; }
    private Task When_a_message_named(string label) { _active.Message(label); return Task.CompletedTask; }

    private async Task<CompositeStep> When_composite_with_a_Given_sub_step(string label)
    {
        await _active.Make($"{label}-composite-body");
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => A_call_named($"{label}-first-untyped-sub"),
                _ => Given_a_call_named($"{label}-given-sub"),
                _ => A_call_named($"{label}-untyped-sub-after-given"))
            .Build();
    }

    private async Task<CompositeStep> Given_composite_with_a_When_sub_step(string label)
    {
        await _active.Make($"{label}-composite-body");
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => When_a_call_named($"{label}-when-sub"),
                _ => A_call_named($"{label}-untyped-sub-after-when"))
            .Build();
    }

    // ---- steps woven by Kronikol.StepTracking, called from a LightBDD step ----
    [GivenStep] public async Task WovenGiven(string label) => await _active.Make(label);
    [WhenStep] public async Task WovenWhen(string label) => await _active.Make(label);
    [Step] public async Task WovenPlain(string label) => await _active.Make(label);

    private async Task When_a_LightBDD_step_calls_a_woven_Given(string label)
    {
        await _active.Make($"{label}-before-sub");
        await WovenGiven($"{label}-in-sub");
        await _active.Make($"{label}-after-sub");
    }

    private async Task Given_a_LightBDD_step_calls_a_woven_When(string label)
    {
        await _active.Make($"{label}-before-sub");
        await WovenWhen($"{label}-in-sub");
        await _active.Make($"{label}-after-sub");
    }

    // ---- scenarios ----
    [Scenario]
    public async Task L01_plain_Given_When_Then() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("l01-given"),
        _ => And_a_call_named("l01-and"),
        _ => When_a_call_named("l01-when"),
        _ => And_a_call_named("l01-when-and"),
        _ => Then_a_call_named("l01-then"),
        _ => But_a_call_named("l01-then-but"));

    [Scenario]
    public async Task L02_When_composite_with_a_Given_sub_step() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("l02-given"),
        _ => When_composite_with_a_Given_sub_step("l02"),
        _ => Then_a_call_named("l02-then"));

    [Scenario]
    public async Task L03_Given_composite_with_a_When_sub_step() => await Runner.RunScenarioAsync(
        _ => Given_composite_with_a_When_sub_step("l03"),
        _ => When_a_call_named("l03-when"));

    [Scenario]
    public async Task L04_a_LightBDD_When_calls_a_woven_Given() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("l04-given"),
        _ => When_a_LightBDD_step_calls_a_woven_Given("l04"),
        _ => Then_a_call_named("l04-then"));

    [Scenario]
    public async Task L05_a_LightBDD_Given_calls_a_woven_When() => await Runner.RunScenarioAsync(
        _ => Given_a_LightBDD_step_calls_a_woven_When("l05"),
        _ => When_a_call_named("l05-when"));

    [Scenario]
    public async Task L06_first_scenario_on_a_shared_handler()
    {
        UseSharedHandler();
        await Runner.RunScenarioAsync(
            _ => Given_a_call_named("l06-given"),
            _ => When_a_call_named("l06-when"),
            _ => Then_a_call_named("l06-then"));
    }

    [Scenario]
    public async Task L07_second_scenario_on_a_shared_handler()
    {
        UseSharedHandler();
        await Runner.RunScenarioAsync(
            _ => Given_a_call_named("l07-given"),
            _ => When_a_call_named("l07-when"),
            _ => Then_a_call_named("l07-then"));
    }

    [Scenario]
    public async Task L09_a_When_with_no_HTTP_call_then_an_And() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("l09-given"),
        _ => When_a_message_named("l09-when-message"),
        _ => And_a_call_named("l09-when-and"),
        _ => Then_a_call_named("l09-then"));

    [Scenario]
    public async Task L10_a_Given_with_no_HTTP_call() => await Runner.RunScenarioAsync(
        _ => Given_a_message_named("l10-given-message"),
        _ => When_a_call_named("l10-when"));

    [Scenario]
    public async Task L11_a_message_tracker_that_does_not_track_setup()
    {
        _sharedActive = new Calls(
            new HttpClient(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", TrackDuringSetup = false, CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher }));
        await Runner.RunScenarioAsync(
            _ => Given_a_call_named("l11-given"),
            _ => When_a_call_named("l11-when"));
    }

    [Scenario]
    public async Task L12_an_untyped_step_after_a_typed_one() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("l12-given"),
        _ => A_call_named("l12-untyped-after-given"),
        _ => When_a_call_named("l12-when"),
        _ => A_call_named("l12-untyped-after-when"));

    [Scenario]
    public async Task L13_LightBDDs_own_setup_step_type() => await Runner.RunScenarioAsync(
        _ => Setup_a_call_named("l13-setup"),
        _ => When_a_call_named("l13-when"));

    [Scenario]
    public async Task L14_a_bypassed_sub_step_in_a_Given_composite() => await Runner.RunScenarioAsync(
        _ => Given_composite_with_a_bypassed_sub_step("l14"),
        _ => And_a_call_named("l14-and"),
        _ => When_a_call_named("l14-when"));

    [Scenario]
    public async Task L15_an_And_first_over_a_Given_sub_step() => await Runner.RunScenarioAsync(
        _ => And_composite_with_a_Given_sub_step("l15"),
        _ => When_a_call_named("l15-when"));

    [Scenario]
    public async Task L16_an_And_first_then_a_When() => await Runner.RunScenarioAsync(
        _ => And_a_call_named("l16-and"),
        _ => When_a_call_named("l16-when"));

    [Scenario]
    public async Task L08_untyped_steps_only() => await Runner.RunScenarioAsync(
        _ => A_call_named("l08-first"),
        _ => A_call_named("l08-second"));
}
