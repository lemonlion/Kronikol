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

[assembly: PhaseProbe.Rev3.LightBddLegacy.ProbeScope]
[assembly: ClassCollectionBehavior(AllowTestParallelization = false)]
[assembly: TrackSteps]

namespace PhaseProbe.Rev3.LightBddLegacy;

internal class ProbeScopeAttribute : LightBddScopeAttribute
{
#pragma warning disable CS0618 // the legacy overload: no step decorator
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.ReportWritersConfiguration().CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Rev3 probe", SeparateSetup = true });
    }
#pragma warning restore CS0618

    protected override void OnTearDown()
    {
        Dump2.Write("rev3-lightbdd-legacy");
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

    [Step] public async Task WovenPlain(string label) => await C.Make(label);
    [GivenStep] public async Task WovenGiven(string label) => await C.Make(label);
    [WhenStep] public async Task WovenWhen(string label) => await C.Make(label);

    private async Task Given_a_call_named(string label) => await C.Make(label);
    private async Task And_a_call_named(string label) => await C.Make(label);
    private async Task When_a_call_named(string label) => await C.Make(label);
    private async Task When_a_nosetup_call_named(string label) => await NoSetup.Make(label);
    private async Task Then_a_call_named(string label) => await C.Make(label);
    private async Task Then_a_nosetup_call_named(string label) => await NoSetup.Make(label);

    private async Task Given_a_step_calls_a_woven_Given(string label) { await C.Make(label + "-before"); await WovenGiven(label + "-sub"); await C.Make(label + "-after"); }
    private async Task When_a_step_calls_a_woven_Given(string label) { await NoSetup.Make(label + "-before"); await WovenGiven(label + "-sub"); await NoSetup.Make(label + "-after"); await NoSetup.Make(label + "-after2"); }
    private async Task Given_a_step_calls_a_woven_When(string label) { await C.Make(label + "-before"); await WovenWhen(label + "-sub"); await C.Make(label + "-after"); }
    private async Task When_a_step_calls_a_woven_plain(string label) { await C.Make(label + "-before"); await WovenPlain(label + "-sub"); await C.Make(label + "-after"); }

    [Scenario] public async Task N01_plain_Given_When_Then() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("n01-given"),
        _ => When_a_call_named("n01-when"),
        _ => When_a_call_named("n01-when2"),
        _ => Then_a_call_named("n01-then"));

    // The one in the issue, with no decorator: top-level WHEN, a woven Given under it.
    [Scenario] public async Task N02_a_When_calls_a_woven_Given() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("n02-given"),
        _ => When_a_step_calls_a_woven_Given("n02"),
        _ => Then_a_nosetup_call_named("n02-then"),
        _ => Then_a_nosetup_call_named("n02-then2"));

    // A woven Given under a GIVEN: the handler stops asking for the rest of the test.
    [Scenario] public async Task N03_a_Given_calls_a_woven_Given_then_When_Then() => await Runner.RunScenarioAsync(
        _ => Given_a_step_calls_a_woven_Given("n03"),
        _ => When_a_nosetup_call_named("n03-when"),
        _ => Then_a_nosetup_call_named("n03-then"));

    [Scenario] public async Task N04_a_Given_calls_a_woven_When() => await Runner.RunScenarioAsync(
        _ => Given_a_step_calls_a_woven_When("n04"),
        _ => When_a_nosetup_call_named("n04-when"),
        _ => Then_a_nosetup_call_named("n04-then"));

    // One client, as most suites have: the handler's own memory at 5.28.2 and the test's on the patch agree on when it stops asking.
    private async Task Given_one_client_woven_given(string label) { await C.Make(label + "-before"); await WovenGiven(label + "-sub"); }

    [Scenario] public async Task N06_one_client_a_Given_calls_a_woven_Given_then_When_Then() => await Runner.RunScenarioAsync(
        _ => Given_one_client_woven_given("n06"),
        _ => When_a_call_named("n06-when"),
        _ => Then_a_call_named("n06-then"));

    private async Task When_one_client_woven_given(string label) { await C.Make(label + "-before"); await WovenGiven(label + "-sub"); await C.Make(label + "-after"); }

    [Scenario] public async Task N07_one_client_a_When_calls_a_woven_Given() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("n07-given"),
        _ => When_one_client_woven_given("n07"),
        _ => Then_a_call_named("n07-then"));

    [Scenario] public async Task N05_a_woven_plain_sub_step() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("n05-given"),
        _ => When_a_step_calls_a_woven_plain("n05"),
        _ => Then_a_call_named("n05-then"));
}
