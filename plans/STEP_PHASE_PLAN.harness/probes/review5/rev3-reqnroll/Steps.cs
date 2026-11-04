using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using PhaseProbe;
using Reqnroll;
using Reqnroll.BoDi;

[assembly: TrackSteps]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe.Rev3.Reqnroll;

public class SubSteps(Calls calls)
{
    public readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task? Worker;
    public readonly CancellationTokenSource Stop = new();

    [Step] public async Task WorkerStep(string label) { await calls.Make(label + "-worker-start"); await Gate.Task; await calls.Make(label + "-worker-end"); }
    [Step] public async Task PollPlain(string label) => await calls.Make(label);
    [ThenStep] public async Task PollThen(string label) => await calls.Make(label);
    [GivenStep] public async Task SeedGiven(string label) => await calls.Make(label);
    [Step] public async Task CleanupPlain(string label) => await calls.Make(label);
}

[Binding]
public class PhaseSteps(Calls calls, SubSteps sub, ScenarioContext scenario)
{
    [StepDefinition("a call named (.*)")]
    public Task ACall(string label) => calls.Make(label);

    [Given("a sync binding leaves a worker running as (.*)")]
    public void SyncLeaves(string label) { sub.Worker = sub.WorkerStep(label); }

    [Given("an async binding leaves a worker running as (.*)")]
    public async Task AsyncLeaves(string label) { sub.Worker = sub.WorkerStep(label); await calls.Make(label + "-given-call"); }

    [StepDefinition("the worker is released")]
    public async Task Release() { sub.Gate.SetResult(); await sub.Worker!; }

    [Given("a binding starts a keyword free loop as (.*)")]
    public void KeywordFreeLoop(string label)
    {
        sub.Worker = Task.Run(async () =>
        {
            var i = 0;
            while (!sub.Stop.IsCancellationRequested) { await sub.PollPlain($"{label}-poll{i++}"); await Task.Delay(15); }
        });
    }

    [Given("a binding starts a Then loop as (.*)")]
    public async Task ThenLoop(string label)
    {
        sub.Worker = Task.Run(async () =>
        {
            var i = 0;
            while (!sub.Stop.IsCancellationRequested) { await sub.PollThen($"{label}-poll{i++}"); await Task.Delay(15); }
        });
        await Task.Delay(60);
    }

    [StepDefinition("the loop is stopped")]
    public async Task StopLoop() { sub.Stop.Cancel(); await sub.Worker!; }

    [Given("nothing happens")]
    public void Nothing() { }

    [When("a binding calls then marks the boundary as (.*)")]
    public async Task CallsThenMarks(string label)
    {
        await calls.Make(label + "-arrange");
        DefaultTrackingDiagramOverride.StartAction(ReqNRollTestContext.CurrentTestInfo!.Value.Id);
        await calls.Make(label + "-act");
    }

    [Given("a binding bypasses a manual sub-step as (.*)")]
    public async Task ManualBypass(string label)
    {
        var id = ReqNRollTestContext.CurrentTestInfo!.Value.Id;
        StepCollector.StartStep(id, null, label + "-manual", null, null);
        StepCollector.BypassStep(id, "manual");
        await calls.Make(label + "-given");
    }
}

[Binding]
public class Hooks
{
    private static HttpClient Client(bool trackDuringSetup) => new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = trackDuringSetup,
    }) { InnerHandler = new AnswerEverything() });

    [BeforeScenario(Order = 0)]
    public void Register(IObjectContainer container, ScenarioContext scenario)
    {
        var tags = scenario.ScenarioInfo.Tags;
        container.RegisterInstanceAs(Client(!tags.Contains("no-setup-tracking")));
        container.RegisterInstanceAs(new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Probe",
            ServiceName = "Bus",
            CurrentTestInfoFetcher = ReqNRollTestTrackingMessageHandlerOptions.TestInfoFetcher,
        }));
    }

    [BeforeScenario("woven-given-hook", Order = 1)]
    public Task Seed(SubSteps sub) => sub.SeedGiven("hook-seed");

    [AfterScenario("woven-plain-after-hook")]
    public Task Cleanup(SubSteps sub) => sub.CleanupPlain("hook-cleanup");

    [AfterTestRun]
    public static void Report()
    {
        Dump2.Write("rev3-reqnroll");
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Rev3 probe", SeparateSetup = true });
    }
}
