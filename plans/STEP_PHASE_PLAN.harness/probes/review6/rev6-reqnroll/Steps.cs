using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.BoDi;
using Rev5;

[assembly: TrackSteps]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Rev6.Reqnroll;

public sealed class Calls(HttpClient client, MessageTracker tracker)
{
    public async Task Make(string label) => (await client.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    public void Event(string label) => tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
}

public static class Shared
{
    public static HttpClient NewClient() => new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Answer() });
}

public sealed class Woven(Calls calls)
{
    public bool Yes => true;
    public bool GetterCalls { get { calls.Make("hook-getter").GetAwaiter().GetResult(); return false; } }
    [WhenStep] public async Task LogIn() => await calls.Make("hook-login");
    [GivenStep] public async Task SeedInHook() => await calls.Make("hook-seed");
    [GivenStep(SkipIf = nameof(GetterCalls))] public async Task SeedWithGetter() => await calls.Make("hook-seed-with-getter");
    [GivenStep] public async Task SlowGiven(string label, Task gate) { await gate; await calls.Make(label); }
    [GivenStep(SkipIf = nameof(Yes))] public void SkipSync(string l) => calls.Make(l).GetAwaiter().GetResult();
    [GivenStep(SkipIf = nameof(Yes))] public async Task SkipAsync(string l) => await calls.Make(l);
    [GivenStep(SkipIf = nameof(Yes))] public Task SkipTask(string l) => calls.Make(l);
    [WhenStep(SkipIf = nameof(Yes))] public async Task<int> SkipWhenInt(string l) { await calls.Make(l); return 1; }
    [ThenStep(SkipIf = nameof(Yes))] public Task<string> SkipThenStr(string l) => Task.FromResult(l);
}

[Binding]
public class Rev6Steps(Calls calls, Woven woven, ScenarioContext scenario)
{
    private static TaskCompletionSource _gate = new();
    private static Task _left = Task.CompletedTask;

    [StepDefinition("a call named (.*)")]
    public Task ACall(string label) => calls.Make(label);

    [Then("a marking check named (.*)")]
    public async Task MarkingCheck(string label)
    {
        TrackingDiagramOverride.StartAction();
        await calls.Make(label);
    }

    [When("a binding marks after a call named (.*)")]
    public async Task MarksAfterACall(string label)
    {
        await calls.Make(label + "-arrange");
        TrackingDiagramOverride.StartAction();
        await calls.Make(label);
    }

    [When("a binding marks on a fork after a call named (.*)")]
    public void MarksOnAFork(string label)
    {
        calls.Make(label + "-arrange").GetAwaiter().GetResult();
        Task.Run(() => TrackingDiagramOverride.StartAction()).GetAwaiter().GetResult();
        calls.Make(label).GetAwaiter().GetResult();
    }

    [When("a binding leaves a woven Given running named (.*)")]
    public async Task LeavesRunning(string label)
    {
        _gate = new TaskCompletionSource();
        _left = woven.SlowGiven(label + "-left", _gate.Task);
        await calls.Make(label);
    }

    [StepDefinition("the left step is released named (.*)")]
    public async Task Released(string label)
    {
        _gate.TrySetResult();
        await _left;
        await calls.Make(label);
    }

    [When("a binding bypasses its own step named (.*)")]
    public async Task BypassesItsOwnStep(string label)
    {
        await calls.Make(label + "-before");
        StepCollector.BypassStep("not applicable");
        await calls.Make(label);
    }

    [When("a sync binding bypasses a hand step named (.*)")]
    public void SyncBypassesHand(string label)
    {
        calls.Make(label + "-before").GetAwaiter().GetResult();
        StepCollector.StartStep("Given", label + "-hand", null, null);
        StepCollector.BypassStep("manual");
        calls.Make(label).GetAwaiter().GetResult();
    }

    [StepDefinition("a binding runs skipped woven steps named (.*)")]
    public async Task RunsSkippedWoven(string label)
    {
        await calls.Make(label + "-before");
        woven.SkipSync(label + "-skip-sync");
        await calls.Make(label + "-after-sync");
        await woven.SkipAsync(label + "-skip-async");
        await calls.Make(label + "-after-async");
        await woven.SkipTask(label + "-skip-task");
        await calls.Make(label + "-after-task");
        await woven.SkipWhenInt(label + "-skip-when-int");
        await calls.Make(label + "-after-when-int");
        await woven.SkipThenStr(label + "-skip-then-str");
        await calls.Make(label + "-after");
    }

    [Given("a hand step closed on another flow in a sync binding named (.*)")]
    public void HandStepOffFlowSync(string label)
    {
        var id = ReqNRollTestContext.CurrentTestInfo!.Value.Id;
        StepCollector.StartStep(id, "Given", label + "-hand", null, null);
        calls.Make(label).GetAwaiter().GetResult();
        Task.Run(() => StepCollector.CompleteStep(id, true)).GetAwaiter().GetResult();
    }
}

[Binding]
public class Hooks(ScenarioContext scenario, IObjectContainer container)
{
    [BeforeScenario(Order = 0)]
    public void Register()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = ReqNRollTestTrackingMessageHandlerOptions.TestInfoFetcher });
        container.RegisterInstanceAs(new Calls(Shared.NewClient(), tracker));
    }

    [BeforeScenario("hookgivenwhen", Order = 10)]
    public async Task HookGivenWhen(Woven woven)
    {
        await woven.SeedInHook();
        await woven.LogIn();
    }

    [BeforeScenario("hookwhengiven", Order = 10)]
    public async Task HookWhenGiven(Woven woven)
    {
        await woven.LogIn();
        await woven.SeedInHook();
    }

    [BeforeScenario("hookgetter", Order = 10)]
    public Task HookGetter(Woven woven) => woven.SeedWithGetter();

    [AfterTestRun]
    public static void Report()
    {
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "rev6-reqnroll");
    }
}
