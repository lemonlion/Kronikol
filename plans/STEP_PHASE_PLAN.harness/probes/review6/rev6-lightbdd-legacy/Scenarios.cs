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
        #pragma warning disable CS0618
        configuration.ReportWritersConfiguration().CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Rev6 probe", SeparateSetup = true });
#pragma warning restore CS0618
    }

    protected override void OnTearDown()
    {
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "rev6-lightbdd-legacy");
    }
}

public static class Clients
{
    public static HttpClient New(bool trackDuringSetup = true, bool trackDuringAction = true) => new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = trackDuringSetup,
        TrackDuringAction = trackDuringAction
    }) { InnerHandler = new Answer() });

    public static readonly HttpClient Http = New();
    public static readonly HttpClient NoSetup = New(trackDuringSetup: false);
    public static readonly HttpClient NoAction = New(trackDuringAction: false);
}

public abstract class Fixture : FeatureFixture
{
    protected async Task All(string label)
    {
        await Clients.Http.GetAsync($"http://probe.local/{label}");
        await Clients.NoSetup.GetAsync($"http://probe.local/{label}-nosetup");
        await Clients.NoAction.GetAsync($"http://probe.local/{label}-noaction");
    }

    protected async Task OnlyHttp(string label) => await Clients.Http.GetAsync($"http://probe.local/{label}");
    protected async Task OnlyQuiet(string label) => await Clients.NoSetup.GetAsync($"http://probe.local/{label}-nosetup");
    protected Task Given_a_call_named(string label) => All(label);
    protected Task When_a_call_named(string label) => All(label);
    protected Task Then_a_call_named(string label) => All(label);
    protected Task When_a_marking_call_named(string label) { TrackingDiagramOverride.StartAction(); return All(label); }
    protected async Task When_a_call_marks_after_named(string label) { await All(label + "-arrange"); TrackingDiagramOverride.StartAction(); await All(label); }
    protected Task Given_only_http(string label) => OnlyHttp(label);
    protected Task When_only_quiet(string label) => OnlyQuiet(label);
    protected Task When_only_http(string label) => OnlyHttp(label);
    protected Task Then_only_quiet(string label) => OnlyQuiet(label);
}

public class FeatureA : Fixture
{
    [Scenario] public Task S01_first_test_on_the_shared_handlers() => Runner.RunScenarioAsync(_ => Given_a_call_named("s01-given"), _ => When_a_call_named("s01-when"), _ => Then_a_call_named("s01-then"));
    [Scenario] public Task S02_second_test_on_the_shared_handlers() => Runner.RunScenarioAsync(_ => Given_a_call_named("s02-given"), _ => When_a_call_named("s02-when"), _ => Then_a_call_named("s02-then"));
    [Scenario] public Task S03_marks_in_the_When() => Runner.RunScenarioAsync(_ => Given_a_call_named("s03-given"), _ => When_a_call_marks_after_named("s03-when"), _ => Then_a_call_named("s03-then"));
    [Scenario] public Task S04_one_client_sees_the_Given_another_the_When() => Runner.RunScenarioAsync(_ => Given_only_http("s04-given"), _ => When_only_quiet("s04-when"), _ => When_only_http("s04-when2"), _ => Then_only_quiet("s04-then"));
}

public class FeatureB : Fixture
{
    [Scenario] public Task S05_another_fixture_on_the_same_handlers() => Runner.RunScenarioAsync(_ => Given_a_call_named("s05-given"), _ => When_a_call_named("s05-when"), _ => Then_a_call_named("s05-then"));
    [Scenario] public Task S06_a_When_first_on_the_same_handlers() => Runner.RunScenarioAsync(_ => When_a_call_named("s06-when"), _ => Then_a_call_named("s06-then"));
}
