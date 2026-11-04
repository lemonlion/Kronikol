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
        // The default step types: given, when, then, setup, and.
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
    }

    protected override void OnTearDown()
    {
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        var lines = new List<string>();
        using (TestIdentityScope.Begin("outside", "outside-a-scenario"))
        {
            lines.Add("handler that drew a boundary for an earlier scenario: " + Try(Scenarios.SharedClient));
            lines.Add("handler that has drawn none: " + Try(Scenarios.FreshClient));
        }
        File.WriteAllLines(Path.Combine(dir, "outside-a-scenario.txt"), lines);
        Dump.Write("lightbdd");
    }

    private static string Try(HttpClient client)
    {
        try { client.GetAsync("http://probe.local/outside").GetAwaiter().GetResult(); return "tracked, no exception"; }
        catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
    }
}

public class Scenarios : FeatureFixture
{
    internal static readonly HttpClient SharedClient = new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });
    internal static readonly HttpClient FreshClient = new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });
    private async Task Given_a_shared_call() => await SharedClient.GetAsync("http://probe.local/l5-given");
    private async Task When_a_shared_call() => await SharedClient.GetAsync("http://probe.local/l5-when");

    [Scenario]
    public async Task L5_a_shared_handler_draws_a_boundary() => await Runner.RunScenarioAsync(
        _ => Given_a_shared_call(),
        _ => When_a_shared_call());

    private readonly Calls _active = new(
        new HttpClient(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    private async Task Setup_a_call_named(string label) => await _active.Make(label);
    private async Task Given_a_call_named(string label) => await _active.Make(label);
    private async Task And_a_call_named(string label) => await _active.Make(label);
    private async Task When_a_call_named(string label) => await _active.Make(label);
    private async Task Then_a_call_named(string label) => await _active.Make(label);
    private async Task A_call_named(string label) => await _active.Make(label);
    private Task A_step_that_bypasses(string label) { StepExecution.Current.Bypass("not today"); return Task.CompletedTask; }

    private Task<CompositeStep> Given_composite_with_a_bypassed_sub_step_then_a_When(string label) =>
        Task.FromResult(CompositeStep.DefineNew().AddAsyncSteps(
                _ => A_step_that_bypasses($"{label}-bypassed"),
                _ => When_a_call_named($"{label}-when-sub"),
                _ => A_call_named($"{label}-untyped-sub"))
            .Build());

    private Task<CompositeStep> And_composite_with_typed_sub_steps(string label) =>
        Task.FromResult(CompositeStep.DefineNew().AddAsyncSteps(
                _ => Given_a_call_named($"{label}-given-sub"),
                _ => When_a_call_named($"{label}-when-sub"))
            .Build());

    [Scenario]
    public async Task L1_the_default_setup_step_type() => await Runner.RunScenarioAsync(
        _ => Setup_a_call_named("l1-setup"),
        _ => When_a_call_named("l1-when"),
        _ => Then_a_call_named("l1-then"));

    [Scenario]
    public async Task L2_a_bypassed_sub_step_in_a_Given_composite() => await Runner.RunScenarioAsync(
        _ => Given_composite_with_a_bypassed_sub_step_then_a_When("l2"),
        _ => And_a_call_named("l2-and"),
        _ => When_a_call_named("l2-when"));

    [Scenario]
    public async Task L3_an_And_first_with_typed_sub_steps() => await Runner.RunScenarioAsync(
        _ => And_composite_with_typed_sub_steps("l3"),
        _ => A_call_named("l3-after"));

    [Scenario]
    public async Task L4_repeated_keyword_types() => await Runner.RunScenarioAsync(
        _ => Given_a_call_named("l4-given"),
        _ => Given_a_call_named("l4-given-2"),
        _ => When_a_call_named("l4-when"),
        _ => When_a_call_named("l4-when-2"),
        _ => Then_a_call_named("l4-then"));
}
