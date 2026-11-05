using Kronikol;
using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit2;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Core.Execution;
using LightBDD.Core.Extensibility.Execution;
using LightBDD.Framework;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit2;
using Probe;

[assembly: Probe.LightBdd.ProbeScope]
[assembly: ClassCollectionBehavior(AllowTestParallelization = false)]
[assembly: TrackSteps]

namespace Probe.LightBdd;

internal class ProbeScopeAttribute : LightBddScopeAttribute
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        // A suite's own decorator, registered first, so it runs outside Kronikol's: an authenticated client, a clock, a log.
        configuration.ExecutionExtensionsConfiguration().EnableStepDecorator<ConsumerDecorator>();
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "LightBDD probe", SeparateSetup = true });
    }

    protected override void OnTearDown() => Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "lightbdd");
}

/// <summary>A decorator of the suite's own that makes a tracked call before each step of a scenario that asks for it.</summary>
public sealed class ConsumerDecorator : IStepDecorator
{
    public static readonly AsyncLocal<HttpClient?> Client = new();
    public static readonly AsyncLocal<int> Count = new();

    public async Task ExecuteAsync(IStep step, Func<Task> stepInvocation)
    {
        if (Scenarios.DecoratorCalls.Value is { } calls)
            await calls($"decorator-before-{step.Info.Name.StepTypeName?.OriginalName ?? "untyped"}-{step.Info.Name}");
        await stepInvocation();
    }
}

public class Scenarios : FeatureFixture
{
    public static readonly AsyncLocal<Func<string, Task>?> DecoratorCalls = new();

    private static HttpClient NewClient(bool s = true, bool a = true) => new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = s, TrackDuringAction = a
    }) { InnerHandler = new Answer() });

    private static readonly HttpClient SharedHttp = NewClient();
    private static readonly HttpClient SharedNoSetup = NewClient(s: false);
    private static readonly HttpClient SharedNoAction = NewClient(a: false);

    private readonly MessageTracker _bus = new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher });
    private readonly MessageTracker _busNoSetup = new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", TrackDuringSetup = false, CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher });
    private readonly MessageTracker _busNoAction = new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", TrackDuringAction = false, CurrentTestInfoFetcher = LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher });

    private async Task Call(string label)
    {
        (await SharedHttp.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
        await SharedNoSetup.GetAsync($"http://probe.local/{label}-nosetup");
        await SharedNoAction.GetAsync($"http://probe.local/{label}-noaction");
        Message(label);
    }

    private void Message(string label)
    {
        _bus.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
        _busNoSetup.TrackSendEvent("bus", label + "-nosetup", new Uri($"bus://probe/{label}-nosetup"));
        _busNoAction.TrackSendEvent("bus", label + "-noaction", new Uri($"bus://probe/{label}-noaction"));
    }

    [GivenStep] public async Task WovenGiven(string label) => await Call(label);

    private Task Given_a_call(string label) => Call(label);
    private Task When_a_call(string label) => Call(label);
    private Task Then_a_call(string label) => Call(label);
    private Task Setup_a_call(string label) => Call(label);
    private Task And_a_call(string label) => Call(label);
    private Task A_plain_call(string label) => Call(label);
    private Task When_only_a_message(string label) { Message(label); return Task.CompletedTask; }
    private Task Given_only_a_message(string label) { Message(label); return Task.CompletedTask; }

    private async Task Given_a_failing_call(string label) { await Call(label); throw new InvalidOperationException("planned"); }
    private async Task When_a_step_is_bypassed(string label) { await Call(label); StepExecution.Current.Bypass("bypassed by the step"); }
    private async Task Then_a_reset_then_a_call(string label) { TestPhaseContext.Reset(); await Call(label); }

    private Task<CompositeStep> When_a_composite_with_a_sub_step_failing(string label) => Task.FromResult(CompositeStep.DefineNew()
        .AddAsyncSteps(_ => Given_a_call(label + "-sub-one"), _ => Given_a_failing_call(label + "-sub-fails"), _ => Then_a_call(label + "-never"))
        .Build());

    private Task<CompositeStep> When_a_composite_with_a_sub_step_bypassed(string label) => Task.FromResult(CompositeStep.DefineNew()
        .AddAsyncSteps(_ => When_a_step_is_bypassed(label + "-sub-bypassed"), _ => Then_a_call(label + "-sub-after"))
        .Build());

    [MultiAssert]
    private Task<CompositeStep> Then_a_multi_assert_composite(string label) => Task.FromResult(CompositeStep.DefineNew()
        .AddAsyncSteps(_ => Given_a_failing_call(label + "-sub-fails"), _ => Then_a_call(label + "-sub-still-runs"))
        .Build());

    private Task<CompositeStep> A_plain_composite(string label) => Task.FromResult(CompositeStep.DefineNew()
        .AddAsyncSteps(_ => Given_a_call(label + "-sub-given"), _ => When_a_call(label + "-sub-when"), _ => A_plain_call(label + "-sub-plain"))
        .Build());

    [Scenario]
    public async Task L01_first_on_the_shared_handlers() => await Runner.RunScenarioAsync(
        _ => Given_a_call("l01-given"),
        _ => When_a_call("l01-when"),
        _ => Then_a_call("l01-then"));

    [Scenario]
    public async Task L02_later_on_the_shared_handlers() => await Runner.RunScenarioAsync(
        _ => Given_a_call("l02-given"),
        _ => Given_a_call("l02-given-repeated"),
        _ => When_a_call("l02-when"),
        _ => When_a_call("l02-when-repeated"),
        _ => Then_a_call("l02-then"));

    [Scenario]
    public async Task L03_messages_only() => await Runner.RunScenarioAsync(
        _ => Given_only_a_message("l03-given"),
        _ => When_only_a_message("l03-when"));

    [Scenario]
    public async Task L04_LightBDDs_own_setup_type() => await Runner.RunScenarioAsync(
        _ => Setup_a_call("l04-setup"),
        _ => Given_a_call("l04-given"),
        _ => When_a_call("l04-when"));

    [Scenario]
    public async Task L05_the_default_And_prefix_has_no_type() => await Runner.RunScenarioAsync(
        _ => Given_a_call("l05-given"),
        _ => And_a_call("l05-and-after-given"),
        _ => When_a_call("l05-when"),
        _ => And_a_call("l05-and-after-when"));

    [Scenario]
    public async Task L06_a_plain_composite() => await Runner.RunScenarioAsync(
        _ => A_plain_composite("l06"),
        _ => A_plain_call("l06-after"));

    [Scenario]
    public async Task L07_a_consumer_decorator_makes_a_call_before_each_step()
    {
        DecoratorCalls.Value = async label => await Call("l07-" + Sanitised(label));
        try
        {
            await Runner.RunScenarioAsync(
                _ => Given_a_call("l07-given"),
                _ => Given_a_call("l07-given-2"),
                _ => When_a_call("l07-when"),
                _ => Then_a_call("l07-then"));
        }
        finally { DecoratorCalls.Value = null; }
    }

    private static string Sanitised(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());

    [Scenario]
    public async Task L08_a_failing_sub_step() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l08-given"),
        _ => When_a_composite_with_a_sub_step_failing("l08"),
        _ => Then_a_call("l08-never")));

    [Scenario]
    public async Task L09_a_bypassed_sub_step() => await Runner.RunScenarioAsync(
        _ => Given_a_call("l09-given"),
        _ => When_a_composite_with_a_sub_step_bypassed("l09"),
        _ => Then_a_call("l09-then"));

    [Scenario]
    public async Task L10_a_multi_assert_composite() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l10-given"),
        _ => When_a_call("l10-when"),
        _ => Then_a_multi_assert_composite("l10"),
        _ => Then_a_call("l10-never")));

    [Scenario]
    public async Task L11_a_step_resets_the_phase_by_hand() => await Runner.RunScenarioAsync(
        _ => Given_a_call("l11-given"),
        _ => When_a_call("l11-when"),
        _ => Then_a_reset_then_a_call("l11-then"));

    private static HttpClient? _fresh;
    private async Task Fresh(string label) => (await _fresh!.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    private Task Given_a_fresh_call(string label) => Fresh(label);
    private Task When_a_fresh_call(string label) => Fresh(label);
    private Task And_a_fresh_call(string label) => Fresh(label);
    private Task Then_a_fresh_call(string label) => Fresh(label);

    [Scenario]
    public async Task L12_a_phase_written_by_hand_before_the_scenario_then_And_after_When()
    {
        _fresh = NewClient();
        TestPhaseContext.Current = TestPhase.Setup;
        await Runner.RunScenarioAsync(
            _ => Given_a_fresh_call("l12-given"),
            _ => When_a_fresh_call("l12-when"),
            _ => And_a_fresh_call("l12-and-after-when"),
            _ => Then_a_fresh_call("l12-then"));
    }

    [Scenario]
    public async Task L13_a_fresh_client_And_after_a_When_with_no_HTTP_call()
    {
        _fresh = NewClient();
        await Runner.RunScenarioAsync(
            _ => Given_a_fresh_call("l13-given"),
            _ => When_only_a_message("l13-when-msg"),
            _ => And_a_fresh_call("l13-and-after-when"),
            _ => Then_a_fresh_call("l13-then"));
    }

    [Scenario]
    public async Task L14_a_fresh_client_repeated_When_the_first_with_no_HTTP_call()
    {
        _fresh = NewClient();
        await Runner.RunScenarioAsync(
            _ => Given_a_fresh_call("l14-given"),
            _ => When_only_a_message("l14-when-msg"),
            _ => When_a_fresh_call("l14-when-repeated"),
            _ => Then_only_a_message("l14-then-msg"));
    }

    private Task Then_only_a_message(string label) { Message(label); return Task.CompletedTask; }

    private static string ScenarioId => LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher().Id;

    [Step] public async Task WovenSlow(string label, Task gate) { await gate; await Call(label); }

    private async Task When_a_hand_step_is_left_open_and_it_fails(string label)
    {
        await Call(label + "-first");
        StepCollector.StartStep(ScenarioId, null, "a sub-step never closed", null, null);
        await Call(label + "-in-open-sub");
        throw new InvalidOperationException("planned");
    }

    private async Task When_a_sub_step_outlives_a_timeout(string label)
    {
        await Call(label + "-first");
        await WovenSlow(label + "-slow", new TaskCompletionSource().Task).WaitAsync(TimeSpan.FromMilliseconds(20));
    }

    private Task<CompositeStep> When_a_composite_with_a_hand_step_left_open(string label) => Task.FromResult(CompositeStep.DefineNew()
        .AddAsyncSteps(_ => Given_a_call(label + "-sub-one"), _ => When_a_hand_step_is_left_open_and_it_fails(label + "-sub-fails"), _ => Then_a_call(label + "-sub-never"))
        .Build());

    [Scenario]
    public async Task L15_a_failing_step_leaves_a_hand_step_open() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l15-given"),
        _ => When_a_hand_step_is_left_open_and_it_fails("l15-when"),
        _ => Then_a_call("l15-never")));

    [Scenario]
    public async Task L16_a_step_times_out_with_a_woven_sub_step_running() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l16-given"),
        _ => When_a_sub_step_outlives_a_timeout("l16-when"),
        _ => Then_a_call("l16-never")));

    [Scenario]
    public async Task L17_a_composite_whose_sub_step_fails_leaving_a_hand_step_open() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l17-given"),
        _ => When_a_composite_with_a_hand_step_left_open("l17"),
        _ => Then_a_call("l17-never")));

    private async Task When_evidence_is_attached_and_a_sub_step_outlives_a_timeout(string label)
    {
        await Call(label + "-first");
        var file = Path.Combine(Path.GetTempPath(), "k224-evidence.txt");
        await File.WriteAllTextAsync(file, "evidence");
        Track.Attachment(file, "evidence of " + label);
        await WovenSlow(label + "-slow", new TaskCompletionSource().Task).WaitAsync(TimeSpan.FromMilliseconds(20));
    }

    [Scenario]
    public async Task L18_a_step_attaches_evidence_then_times_out_with_a_woven_sub_step_running() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l18-given"),
        _ => When_evidence_is_attached_and_a_sub_step_outlives_a_timeout("l18-when"),
        _ => Then_a_call("l18-never")));

    private async Task When_a_sub_step_that_ends_later_outlives_a_timeout(string label)
    {
        await Call(label + "-first");
        await WovenSlow(label + "-slow", Task.Delay(300)).WaitAsync(TimeSpan.FromMilliseconds(20));
    }

    [Scenario]
    public async Task L19_a_step_times_out_with_a_woven_sub_step_that_ends_after_the_scenario()
    {
        await Assert_fails(Runner.RunScenarioAsync(
            _ => Given_a_call("l19-given"),
            _ => When_a_sub_step_that_ends_later_outlives_a_timeout("l19-when"),
            _ => Then_a_call("l19-never")));
        await Task.Delay(800);
    }

    [Step] public Task WovenGuarded(string label) { if (label.Length > 0) throw new ArgumentException(label); return Call(label); }

    private async Task When_a_woven_step_throws_before_returning(string label)
    {
        await Call(label + "-first");
        await WovenGuarded(label + "-guarded");
    }

    [Scenario]
    public async Task L20_a_step_fails_as_a_woven_Task_step_throws_before_returning() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l20-given"),
        _ => When_a_woven_step_throws_before_returning("l20-when"),
        _ => Then_a_call("l20-never")));

    private static async Task Assert_fails(Task run)
    {
        try { await run; } catch (Exception) { return; }
        throw new InvalidOperationException("expected the scenario to fail");
    }
}
