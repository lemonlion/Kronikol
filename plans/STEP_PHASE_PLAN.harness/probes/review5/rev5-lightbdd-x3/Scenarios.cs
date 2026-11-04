using Kronikol;
using Kronikol.LightBDD;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Framework;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using Rev5;
using Kronikol.LightBDD.xUnit3;
using LightBDD.XUnit3;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(Rev5.LightBdd.ProbeScope))]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace Rev5.LightBdd;

public class ProbeScope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.StepTypeConfiguration().UpdatePredefinedStepTypes("given", "when", "then", "and", "but", "setup", "arrange");
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Rev5 probe", SeparateSetup = true });
    }

    protected override void OnTearDown()
    {
        Skeleton.Write("rev5-lightbdd-x3");
    }
}

public partial class Scenarios : FeatureFixture { }

public partial class Scenarios
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
    private readonly MessageTracker _tracker = new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher });

    private async Task Call(string label) => (await _http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    private async Task All(string label)
    {
        await Call(label);
        _tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
        await _noSetup.GetAsync($"http://probe.local/{label}-nosetup");
        await _noAction.GetAsync($"http://probe.local/{label}-noaction");
    }

    [GivenStep] public async Task WovenGiven(string label) => await All(label);
    [WhenStep] public async Task WovenWhen(string label) => await All(label);
    [Step] public async Task WovenPlain(string label) => await All(label);

    private Task Given_a_call_named(string label) => All(label);
    private Task And_a_call_named(string label) => All(label);
    private Task But_a_call_named(string label) => All(label);
    private Task When_a_call_named(string label) => All(label);
    private Task Then_a_call_named(string label) => All(label);
    private Task Setup_a_call_named(string label) => All(label);
    private Task Arrange_a_call_named(string label) => All(label);
    private Task An_untyped_call_named(string label) => All(label);

    private async Task When_a_step_runs_a_woven_Given(string label) { await All(label + "-before"); await WovenGiven(label + "-sub"); await All(label + "-after"); }
    private async Task Given_a_step_runs_a_woven_When(string label) { await All(label + "-before"); await WovenWhen(label + "-sub"); await All(label + "-after"); }
    private async Task When_a_step_marks_the_boundary(string label) { TrackingDiagramOverride.StartAction(); await All(label); }
    private async Task Then_a_step_marks_the_boundary(string label) { TrackingDiagramOverride.StartAction(); await All(label); }
    private async Task When_a_step_fails(string label) { await All(label); throw new InvalidOperationException("boom"); }
    private async Task Given_a_step_bypasses(string label) { await All(label); StepExecution.Current.Bypass("bypassed"); }

    private Task<CompositeStep> Given_a_composite_with_typed_sub_steps(string label) => Task.FromResult(CompositeStep.DefineNew()
        .AddAsyncSteps(
            _ => Given_a_call_named(label + "-sub-given"),
            _ => When_a_call_named(label + "-sub-when"),
            _ => Then_a_call_named(label + "-sub-then"))
        .Build());

    private Task<CompositeStep> A_keyword_free_composite(string label) => Task.FromResult(CompositeStep.DefineNew()
        .AddAsyncSteps(
            _ => Given_a_call_named(label + "-sub-given"),
            _ => When_a_call_named(label + "-sub-when"),
            _ => And_a_call_named(label + "-sub-and"))
        .Build());

    private Task<CompositeStep> When_a_composite_whose_sub_step_fails(string label) => Task.FromResult(CompositeStep.DefineNew()
        .AddAsyncSteps(
            _ => Given_a_call_named(label + "-sub-given"),
            _ => When_a_step_fails(label + "-sub-fails"))
        .Build());

    //BEGIN_SCENARIOS
    [Scenario] public async Task L01_plain_given_when_then() => await Runner.RunScenarioAsync(
        given => A_call(  "l01-given"),
        when => A_call("l01-when"),
        then => A_call("l01-then"));

    [Scenario] public async Task L02_given_but_when_then_but() => await Runner.RunScenarioAsync(
        given => A_call("l02-given"),
        but => A_call("l02-given-but"),
        when => A_call("l02-when"),
        then => A_call("l02-then"),
        but => A_call("l02-then-but"));

    [Scenario] public async Task L03_and_first_then_when() => await Runner.RunScenarioAsync(
        and => A_call("l03-and-first"),
        when => A_call("l03-when"),
        then => A_call("l03-then"));

    [Scenario] public async Task L04_a_custom_step_type_after_a_when() => await Runner.RunScenarioAsync(
        given => A_call("l04-given"),
        when => A_call("l04-when"),
        arrange => A_call("l04-arrange"),
        then => A_call("l04-then"));

    [Scenario] public async Task L05_setup_type_then_when() => await Runner.RunScenarioAsync(
        setup => A_call("l05-setup"),
        when => A_call("l05-when"));

    [Scenario] public async Task L06_a_when_runs_a_woven_given() => await Runner.RunScenarioAsync(
        given => A_call("l06-given"),
        when => When_a_step_runs_a_woven_Given("l06"),
        then => A_call("l06-then"));

    [Scenario] public async Task L07_a_given_runs_a_woven_when() => await Runner.RunScenarioAsync(
        given => Given_a_step_runs_a_woven_When("l07"),
        when => A_call("l07-when"),
        then => A_call("l07-then"));

    [Scenario] public async Task L08_a_given_composite_with_typed_sub_steps() => await Runner.RunScenarioAsync(
        given => Given_a_composite_with_typed_sub_steps("l08"),
        when => A_call("l08-when"),
        then => A_call("l08-then"));

    [Scenario] public async Task L09_a_keyword_free_composite_first() => await Runner.RunScenarioAsync(
        _ => A_keyword_free_composite("l09"),
        then => A_call("l09-then"));

    [Scenario] public async Task L10_a_step_fails_with_a_composite_parent() => await Assert_fails(Runner.RunScenarioAsync(
        given => A_call("l10-given"),
        when => When_a_composite_whose_sub_step_fails("l10"),
        then => A_call("l10-then")));

    [Scenario] public async Task L11_a_bypassed_given_then_when() => await Runner.RunScenarioAsync(
        given => Given_a_step_bypasses("l11-bypassed"),
        when => A_call("l11-when"),
        then => A_call("l11-then"));

    [Scenario] public async Task L12_a_marking_then() => await Runner.RunScenarioAsync(
        given => A_call("l12-given"),
        when => A_call("l12-when"),
        then => Then_a_step_marks_the_boundary("l12-then"));

    [Scenario] public async Task L13_a_marking_when_after_an_unmarked_when() => await Runner.RunScenarioAsync(
        given => A_call("l13-given"),
        when => A_call("l13-when1"),
        and => When_a_step_marks_the_boundary("l13-when2"),
        then => A_call("l13-then"));

    [Scenario] public async Task L14_then_first_then_given_when() => await Runner.RunScenarioAsync(
        then => A_call("l14-then-first"),
        given => A_call("l14-given"),
        when => A_call("l14-when"));

    private Task A_call(string label) => All(label);

    private static async Task Assert_fails(Task task)
    {
        try { await task; } catch (InvalidOperationException) { }
    }
}
