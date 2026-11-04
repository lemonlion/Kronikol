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
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
    }

    protected override void OnTearDown() => Dump.Write("lightbdd");
}

public class Scenarios : FeatureFixture
{
    private readonly Calls _active = new(
        new HttpClient(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerAfterAWait() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    private async Task And_a_call_named(string label) => await _active.Make(label);
    private async Task When_a_call_named(string label) => await _active.Make(label);
    private async Task Then_a_call_named(string label) => await _active.Make(label);

    [Scenario]
    public async Task M1_an_And_first_then_a_When() => await Runner.RunScenarioAsync(
        _ => And_a_call_named("m1-and"),
        _ => When_a_call_named("m1-when"),
        _ => Then_a_call_named("m1-then"));
}
