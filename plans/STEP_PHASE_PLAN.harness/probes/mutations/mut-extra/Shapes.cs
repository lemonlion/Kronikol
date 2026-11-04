using Kronikol;
using Kronikol.PlantUml;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe.MutExtra;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Dump.Write("mut-extra");
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

/// <summary>Shapes no suite had: a skipped sync sub-step that is not its parent's last act. X36 ends its parent there, so a parent closed early shows nothing.</summary>
public class Shapes : DiagrammedComponentTest
{
    private readonly Calls _calls = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    public bool Always => true;

    [WhenStep(SkipIf = nameof(Always), SkipReason = "skipped")]
    public void SyncWhenSkipped(string label) => Console.Write(label);

    [ThenStep] public void ThenSync(string label) => _calls.Make(label).GetAwaiter().GetResult();
    [WhenStep] public async Task WhenAsync(string label) => await _calls.Make(label);

    [GivenStep]
    public void SyncGivenSkipThenMore(string label)
    {
        SyncWhenSkipped($"{label}-skipped");
        ThenSync($"{label}-then-sub");
        _calls.Make($"{label}-after").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task X90_a_skipped_sync_sub_step_then_more_in_a_sync_Given()
    {
        SyncGivenSkipThenMore("x90");
        await WhenAsync("x90-when");
    }

    [GivenStep]
    public async Task AsyncGivenSkipThenMore(string label)
    {
        SyncWhenSkipped($"{label}-skipped");
        ThenSync($"{label}-then-sub");
        await _calls.Make($"{label}-after");
    }

    [Fact]
    public async Task X91_a_skipped_sync_sub_step_then_more_in_an_async_Given()
    {
        await AsyncGivenSkipThenMore("x91");
        await WhenAsync("x91-when");
    }
}

/// <summary>
/// The only ways a bypassed step can stay on a flow's chain: BypassStep takes its own node off the flow it runs on, so the step must be bypassed on another flow.
/// Each test is deterministic: a blocking wait or a gate decides who runs first.
/// </summary>
public class BypassedOnAnotherFlow : DiagrammedComponentTest
{
    private readonly Calls _calls = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _worker;

    private static string Id => TestContext.Current.Test!.UniqueID;

    [GivenStep] public async Task GivenPlain(string label) => await _calls.Make(label);
    [WhenStep] public async Task WhenPlain(string label) => await _calls.Make(label);
    [WhenStep] public void WhenSync(string label) => _calls.Make(label).GetAwaiter().GetResult();
    [ThenStep] public async Task ThenPlain(string label) => await _calls.Make(label);

    // A fork bypasses the step before its stub has its task, so the flow that opened the step still holds its node.
    [GivenStep]
    public async Task GivenBypassedByAFork(string label)
    {
        var id = Id;
        Task.Run(() => StepCollector.BypassStep(id, "from a fork")).GetAwaiter().GetResult();
        await _calls.Make($"{label}-in-given");
    }

    [GivenStep]
    public void SyncGivenBypassedByAFork(string label)
    {
        var id = Id;
        Task.Run(() => StepCollector.BypassStep(id, "from a fork")).GetAwaiter().GetResult();
        _calls.Make($"{label}-in-given").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task X92_a_fork_bypasses_an_async_Given_then_When()
    {
        await GivenPlain("x92-seed");
        await GivenBypassedByAFork("x92");
        await WhenPlain("x92-when");
        await ThenPlain("x92-then");
    }

    [Fact]
    public async Task X93_a_fork_bypasses_a_sync_Given_then_When()
    {
        await GivenPlain("x93-seed");
        SyncGivenBypassedByAFork("x93");
        await WhenPlain("x93-when");
        await ThenPlain("x93-then");
    }

    [GivenStep]
    public async Task GivenWithASubBypassedByAFork(string label)
    {
        await GivenBypassedByAFork($"{label}-sub");
        await _calls.Make($"{label}-after-sub");
        await WhenPlain($"{label}-when-label");
        await _calls.Make($"{label}-last");
    }

    [Fact]
    public async Task X94_a_Given_goes_on_after_a_fork_bypassed_its_async_sub_step()
    {
        await GivenWithASubBypassedByAFork("x94");
        await WhenPlain("x94-when");
        await ThenPlain("x94-then");
    }

    // The same with the bypass on the sub-step's own flow, to read X94 against.
    [GivenStep]
    public async Task GivenBypassedByHand(string label)
    {
        StepCollector.BypassStep(Id, "by hand");
        await _calls.Make($"{label}-in-given");
    }

    [GivenStep]
    public async Task GivenWithASubBypassedByHand(string label)
    {
        await GivenBypassedByHand($"{label}-sub");
        await _calls.Make($"{label}-after-sub");
        await WhenPlain($"{label}-when-label");
        await _calls.Make($"{label}-last");
    }

    [Fact]
    public async Task X94b_a_Given_goes_on_after_a_hand_bypass_of_its_async_sub_step_on_its_own_flow()
    {
        await GivenWithASubBypassedByHand("x94b");
        await WhenPlain("x94b-when");
        await ThenPlain("x94b-then");
    }

    // No fork at all: the write inside an async body does not flow back to its stub, so a plain self-bypass leaves the node on the caller's chain too.
    [Fact]
    public async Task X99_an_async_Given_bypasses_itself_at_the_top_then_When()
    {
        await GivenPlain("x99-seed");
        await GivenBypassedByHand("x99");
        await WhenPlain("x99-when");
        await ThenPlain("x99-then");
    }

    [GivenStep]
    public void SyncGivenWithASubBypassedByAFork(string label)
    {
        SyncGivenBypassedByAFork($"{label}-sub");
        _calls.Make($"{label}-after-sub").GetAwaiter().GetResult();
        WhenSync($"{label}-when-label");
        _calls.Make($"{label}-last").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task X95_a_sync_Given_goes_on_after_a_fork_bypassed_its_sync_sub_step()
    {
        SyncGivenWithASubBypassedByAFork("x95");
        await WhenPlain("x95-when");
        await ThenPlain("x95-then");
    }

    // A worker forked inside a step outlives it. The step bypassed itself on its own flow, so only the worker still holds its node.
    [GivenStep]
    public void GivenForksAWorkerThenBypassesItself(string label)
    {
        _worker = Task.Run(async () => { await _gate.Task; await ThenPlain($"{label}-worker-then"); });
        StepCollector.BypassStep(Id, "by hand");
    }

    [Fact]
    public async Task X96_a_worker_outlives_a_Given_that_bypassed_itself()
    {
        GivenForksAWorkerThenBypassesItself("x96");
        await GivenPlain("x96-given2");
        _gate.SetResult();
        await _worker!;
        await WhenPlain("x96-when");
        await ThenPlain("x96-then");
    }

    // The same with a Given that passed, to read X96 against.
    [GivenStep]
    public void GivenForksAWorker(string label)
    {
        _worker = Task.Run(async () => { await _gate.Task; await ThenPlain($"{label}-worker-then"); });
    }

    [Fact]
    public async Task X97_a_worker_outlives_a_Given_that_passed()
    {
        GivenForksAWorker("x97");
        await GivenPlain("x97-given2");
        _gate.SetResult();
        await _worker!;
        await WhenPlain("x97-when");
        await ThenPlain("x97-then");
    }

    // A hand bypass on the test's flow takes the step left running on another: the bypass pops the stack's top, whoever's it is.
    [GivenStep]
    public async Task GivenHeldAtAGate(string label)
    {
        await _gate.Task;
        await WhenPlain($"{label}-when-label");
        await _calls.Make($"{label}-last");
    }

    [Fact]
    public async Task X98_a_hand_bypass_takes_a_Given_left_running_on_another_flow()
    {
        await GivenPlain("x98-seed");
        var left = GivenHeldAtAGate("x98-left");
        StepCollector.BypassStep(Id, "by hand");
        _gate.SetResult();
        await left;
        await WhenPlain("x98-when");
        await ThenPlain("x98-then");
    }
}

/// <summary>
/// R2's TrackingIgnore edit in PlantUmlCreator: every report path filters the ignored calls out first, so only the public method called directly can show it.
/// Both routes for the same test go to their own file.
/// </summary>
public class DirectCall : DiagrammedComponentTest
{
    private readonly Calls _calls = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    private readonly HttpClient _noSetup =
        new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = false }) { InnerHandler = new AnswerEverything() });

    [GivenStep] public async Task GivenOnlyADroppedCall(string label) => (await _noSetup.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    [WhenStep] public async Task WhenPlain(string label) => await _calls.Make(label);

    [Fact]
    public async Task X89_the_public_method_called_directly_with_the_dropped_call_in_its_logs()
    {
        await GivenOnlyADroppedCall("x89-given-dropped");
        await WhenPlain("x89-when");

        var id = TestContext.Current.Test!.UniqueID;
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l is not null && l.TestId == id).ToList();

        var text = new System.Text.StringBuilder();
        text.AppendLine("## X89 the method called directly, every log");
        text.AppendLine(Draw(logs));
        text.AppendLine("## X89 as every report path calls it, the ignored calls filtered out");
        text.AppendLine(Draw(logs.Where(l => !l.TrackingIgnore).ToList()));

        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mut-extra.direct.plantuml.txt"), text.ToString());
    }

    private static string Draw(List<RequestResponseLog> logs) =>
        string.Join("\n", PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true, clientSideSplitting: true, maxEncodedDiagramLength: int.MaxValue, excludeAllHeaders: true, lazyLoadImages: false)
            .SelectMany(test => test.PlantUmls.Select(diagram => diagram.PlainText)));
}
