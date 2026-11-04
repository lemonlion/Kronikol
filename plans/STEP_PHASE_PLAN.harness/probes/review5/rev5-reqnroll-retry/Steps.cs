using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.BoDi;
using Rev5;

[assembly: TrackSteps]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Rev5.Reqnroll;

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

    public static readonly HttpClient Client = NewClient();
    public static readonly TaskCompletionSource PollGate = new();
}

public sealed class Woven(Calls calls)
{
    [ThenStep] public async Task HealthCheckIsGreen() => await calls.Make("hook-health");
    [WhenStep] public async Task LogIn() => await calls.Make("hook-login");
    [GivenStep] public async Task SeedInHook() => await calls.Make("hook-seed");
    [ThenStep] public async Task NothingWentWrong() => await calls.Make("hook-after-step");
    [ThenStep] public async Task PollOnce(string label) => await calls.Make(label);
    [GivenStep] public async Task GivenSub(string label) => await calls.Make(label);
    [WhenStep] public async Task WhenSub(string label) => await calls.Make(label);
}

[Binding]
public class Rev5Steps(Calls calls, Woven woven, ScenarioContext scenario, ITestRunner runner)
{
    [StepDefinition("a call named (.*)")]
    public Task ACall(string label) => calls.Make(label);

    [StepDefinition("a shared call named (.*)")]
    public async Task ASharedCall(string label) => (await Shared.Client.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();

    [StepDefinition("a tracked event named (.*)")]
    public void AnEvent(string label) => calls.Event(label);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Attempts = new();
    private static bool First(string label) => Attempts.AddOrUpdate(label, 1, (_, n) => n + 1) == 1;

    [Then("the first attempt of (.*) fails")]
    public async Task FirstAttemptFails(string label)
    {
        await calls.Make(label + "-then-" + Attempts.GetOrAdd(label + "#", 0));
        Attempts.AddOrUpdate(label + "#", 1, (_, n) => n + 1);
        if (First(label)) throw new InvalidOperationException("first attempt of " + label);
    }

    [When("the first attempt of (.*) fails under a nested Given")]
    public async Task FirstAttemptFailsUnderNestedGiven(string label)
    {
        var n = Attempts.AddOrUpdate(label + "#", 1, (_, n) => n + 1);
        await calls.Make(label + "-before-" + n);
        await woven.GivenSub(label + "-sub-" + n);
        await calls.Make(label + "-after-" + n);
        if (First(label)) throw new InvalidOperationException("first attempt of " + label);
    }

    [Given("the first attempt of (.*) fails in a Given")]
    public async Task FirstAttemptFailsInGiven(string label)
    {
        var n = Attempts.AddOrUpdate(label + "#", 1, (_, n) => n + 1);
        await calls.Make(label + "-seed-" + n);
        if (First(label)) throw new InvalidOperationException("first attempt of " + label);
    }

    [Then("a marking check named (.*)")]
    public async Task MarkingCheck(string label)
    {
        TrackingDiagramOverride.StartAction();
        await calls.Make(label);
    }

    [When("a marking act named (.*)")]
    public async Task MarkingAct(string label)
    {
        TrackingDiagramOverride.StartAction();
        await calls.Make(label);
    }

    [Given("a poll-releasing call named (.*)")]
    public async Task PollReleasing(string label)
    {
        await calls.Make(label);
        Hooks.Release();
        await Hooks.PollDone;
    }

    [Given("a hand step closed on another flow named (.*)")]
    public async Task HandStepOffFlow(string label)
    {
        var id = ReqNRollTestContext.CurrentTestInfo!.Value.Id;
        StepCollector.StartStep(id, "Given", label + "-hand", null, null);
        await calls.Make(label);
        await CloseLater(id);
    }

    [Given("a hand step closed on another flow in a sync binding named (.*)")]
    public void HandStepOffFlowSync(string label)
    {
        var id = ReqNRollTestContext.CurrentTestInfo!.Value.Id;
        StepCollector.StartStep(id, "Given", label + "-hand", null, null);
        calls.Make(label).GetAwaiter().GetResult();
        Task.Run(() => StepCollector.CompleteStep(id, true)).GetAwaiter().GetResult();
    }

    private static async Task CloseLater(string id)
    {
        await Task.Yield();
        StepCollector.CompleteStep(id, true);
    }

    [When("a binding that goes back to setup named (.*)")]
    public async Task BackToSetup(string label)
    {
        await calls.Make(label + "-before");
        TrackingDiagramOverride.StartSetup();
        await calls.Make(label + "-in-setup");
    }

    [When("nothing is called")]
    public void NothingCalled() { }

    [Given("a failing step named (.*)")]
    public async Task Failing(string label)
    {
        await calls.Make(label);
        throw new InvalidOperationException("failing step");
    }

    [When("a binding runs a failing nested Given named (.*)")]
    public async Task FailingNested(string label)
    {
        await calls.Make(label + "-before");
        await runner.GivenAsync($"a failing step named {label}-in");
        await calls.Make(label + "-after");
    }

    [Given("a binding runs a nested When named (.*)")]
    public async Task NestedWhen(string label)
    {
        await calls.Make(label + "-before");
        await runner.WhenAsync($"a call named {label}-in");
        await calls.Make(label + "-after");
    }

    [When("a binding calls a woven Given named (.*)")]
    public async Task WovenGiven(string label)
    {
        await calls.Make(label + "-before");
        await woven.GivenSub(label + "-sub");
        await calls.Make(label + "-after");
    }
}

[Binding]
public class Hooks(ScenarioContext scenario, IObjectContainer container)
{
    private static HttpClient Client() => Shared.NewClient();
    private static TaskCompletionSource _gate = new();
    private static TaskCompletionSource _done = new();
    public static void Release() => _gate.TrySetResult();
    public static Task PollDone => _done.Task;

    [BeforeScenario(Order = 0)]
    public void Register()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = ReqNRollTestTrackingMessageHandlerOptions.TestInfoFetcher });
        container.RegisterInstanceAs(new Calls(Client(), tracker));
    }

    [BeforeScenario("health", Order = 10)]
    public Task Health(Woven woven) => woven.HealthCheckIsGreen();

    [BeforeScenario("login", Order = 10)]
    public Task Login(Woven woven) => woven.LogIn();

    [BeforeScenario("hookgiven", Order = 10)]
    public Task HookGiven(Woven woven) => woven.SeedInHook();

    [BeforeScenario("poller", Order = 10)]
    public void Poller(Woven woven)
    {
        _gate = new TaskCompletionSource();
        _done = new TaskCompletionSource();
        var gate = _gate;
        var done = _done;
        _ = Task.Run(async () =>
        {
            await gate.Task;
            await woven.PollOnce("hook-poll");
            done.SetResult();
        });
    }

    [AfterStep("aftereach", Order = int.MaxValue)]
    public Task AfterEach(Woven woven) => woven.NothingWentWrong();

    [AfterScenario("markafter", Order = 0)]
    public async Task MarkAfter(Calls calls)
    {
        TrackingDiagramOverride.StartAction();
        await calls.Make("hook-cleanup");
    }

    [AfterScenario(Order = 1)]
    public Task After(Calls calls) => calls.Make(scenario.ScenarioInfo.Title[..3].ToLowerInvariant() + "-after-scenario");

    [AfterTestRun]
    public static void Report()
    {
        Skeleton.Write("rev5-reqnroll-retry");
    }
}
