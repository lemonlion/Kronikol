using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe.Rev3;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Dump2.Write("rev3-plain");
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime,
            new ReportConfigurationOptions { SpecificationsTitle = "Rev3 probe", SeparateSetup = true });
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public abstract class Base : DiagrammedComponentTest
{
    protected readonly Calls C = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    /// <summary>TrackDuringSetup = false, as the wiki's fixture recipe has it: any call read as Setup is dropped from the report.</summary>
    protected readonly Calls NoSetup = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = false }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    protected static string Id => TestContext.Current.Test!.UniqueID;

    protected Base() { Dump2.Names[Id] = TestContext.Current.Test!.TestDisplayName; }

    public bool Always => true;
    public bool Throws => throw new InvalidOperationException("skipif threw");

    // ---- leaf steps ----
    [Step] public async Task PlainAsync(string label) => await C.Make(label);
    [Step] public void PlainSync(string label) => C.Make(label).GetAwaiter().GetResult();
    [GivenStep] public async Task GivenAsync(string label) => await C.Make(label);
    [GivenStep] public void GivenSync(string label) => C.Make(label).GetAwaiter().GetResult();
    [GivenStep] public void GivenNoCall() { }
    [WhenStep] public async Task WhenAsync(string label) => await C.Make(label);
    [WhenStep] public void WhenSync(string label) => C.Make(label).GetAwaiter().GetResult();
    [ThenStep] public async Task ThenAsync(string label) => await C.Make(label);
    [ThenStep] public void ThenSync(string label) => C.Make(label).GetAwaiter().GetResult();
    [ButStep] public void ButSync(string label) => C.Make(label).GetAwaiter().GetResult();
    [GivenStep] public async Task NsGivenAsync(string label) => await NoSetup.Make(label);
    [Step] public async Task NsPlainAsync(string label) => await NoSetup.Make(label);
    [Step] public void NsPlainSync(string label) => NoSetup.Make(label).GetAwaiter().GetResult();
    [WhenStep] public async Task NsWhenAsync(string label) => await NoSetup.Make(label);
    [ThenStep] public async Task NsThenAsync(string label) => await NoSetup.Make(label);

    [Step(SkipIf = nameof(Always), SkipReason = "skip")] public void SkippedSync() { }
    [Step(SkipIf = nameof(Always), SkipReason = "skip")] public Task SkippedTask() => Task.CompletedTask;
    [Step(SkipIf = nameof(Always), SkipReason = "skip")] public async Task SkippedAsync() { await Task.Yield(); }
    [Step(SkipIf = nameof(Always), SkipReason = "skip")] public async Task<int> SkippedAsyncOfT() { await Task.Yield(); return 1; }
}

// ===================== A: bypass ======================
public class A_Bypass : Base
{
    // Manual pair inside a woven step, the documented meaning of the public API: "the step is popped and recorded as bypassed".
    [GivenStep]
    public void GivenSync_with_manual_bypassed_sub(string label)
    {
        C.Make(label).GetAwaiter().GetResult();
        StepCollector.StartStep(Id, null, $"{label}-manual-sub", null, null);
        StepCollector.BypassStep(Id, "manual");
    }

    [GivenStep]
    public Task GivenTask_with_manual_bypassed_sub(string label)
    {
        StepCollector.StartStep(Id, null, $"{label}-manual-sub", null, null);
        StepCollector.BypassStep(Id, "manual");
        return C.Make(label);
    }

    [GivenStep]
    public async Task GivenAsync_with_manual_bypassed_sub(string label)
    {
        await C.Make(label);
        StepCollector.StartStep(Id, null, $"{label}-manual-sub", null, null);
        StepCollector.BypassStep(Id, "manual");
    }

    [GivenStep]
    public void GivenSync_throws_after_manual_bypassed_sub(string label)
    {
        StepCollector.StartStep(Id, null, $"{label}-manual-sub", null, null);
        StepCollector.BypassStep(Id, "manual");
        throw new InvalidOperationException("after manual bypass");
    }

    [Fact] public async Task A01_sync_Given_with_a_manual_bypassed_sub_step()
    {
        GivenSync_with_manual_bypassed_sub("a01-given");
        await WhenAsync("a01-when");
        await ThenAsync("a01-then");
    }

    [Fact] public async Task A02_nonasync_Task_Given_with_a_manual_bypassed_sub_step()
    {
        await GivenTask_with_manual_bypassed_sub("a02-given");
        await WhenAsync("a02-when");
        await ThenAsync("a02-then");
    }

    [Fact] public async Task A03_async_Given_with_a_manual_bypassed_sub_step()
    {
        await GivenAsync_with_manual_bypassed_sub("a03-given");
        await WhenAsync("a03-when");
        await ThenAsync("a03-then");
    }

    [Fact] public async Task A04_sync_Given_throws_after_a_manual_bypassed_sub_step_caught()
    {
        try { GivenSync_throws_after_manual_bypassed_sub("a04-given"); } catch (InvalidOperationException) { }
        await WhenAsync("a04-when");
        await ThenAsync("a04-then");
    }

    // A manual pair at the test's top level, then a sync woven parent and a skipped child
    [Fact] public async Task A05_manual_pair_at_top_level_then_woven_steps()
    {
        StepCollector.StartStep(Id, null, "a05-manual", null, null);
        StepCollector.BypassStep(Id, "manual");
        await GivenAsync("a05-given");
        WhenSync("a05-when");
        await ThenAsync("a05-then");
    }

    // ---- woven SkipIf in every parent shape, skipped child last in the parent ----
    [GivenStep] public void SyncParent_skipped_sync_last(string l) { C.Make(l).GetAwaiter().GetResult(); SkippedSync(); }
    [GivenStep] public void SyncParent_skipped_task_last(string l) { C.Make(l).GetAwaiter().GetResult(); SkippedTask().GetAwaiter().GetResult(); }
    [GivenStep] public void SyncParent_skipped_async_last(string l) { C.Make(l).GetAwaiter().GetResult(); SkippedAsync().GetAwaiter().GetResult(); }
    [GivenStep] public Task TaskParent_skipped_sync_last(string l) { SkippedSync(); return C.Make(l); }
    [GivenStep] public Task TaskParent_returns_skipped_task(string l) => SkippedTask();
    [GivenStep] public async Task<int> OfTParent_returns_skipped(string l) { var r = await SkippedAsyncOfT(); return r; }
    [GivenStep] public Task<int> NonAsyncOfTParent_returns_skipped(string l) => SkippedAsyncOfT();

    [Fact] public async Task A10_sync_parent_skipped_sync_last() { SyncParent_skipped_sync_last("a10"); await WhenAsync("a10-when"); await ThenAsync("a10-then"); }
    [Fact] public async Task A11_sync_parent_skipped_task_last() { SyncParent_skipped_task_last("a11"); await WhenAsync("a11-when"); await ThenAsync("a11-then"); }
    [Fact] public async Task A12_sync_parent_skipped_async_last() { SyncParent_skipped_async_last("a12"); await WhenAsync("a12-when"); await ThenAsync("a12-then"); }
    [Fact] public async Task A13_task_parent_skipped_sync() { await TaskParent_skipped_sync_last("a13"); await WhenAsync("a13-when"); await ThenAsync("a13-then"); }
    [Fact] public async Task A14_task_parent_returns_skipped() { await TaskParent_returns_skipped_task("a14"); await WhenAsync("a14-when"); await ThenAsync("a14-then"); }
    [Fact] public async Task A15_ofT_parent_returns_skipped() { await OfTParent_returns_skipped("a15"); await NonAsyncOfTParent_returns_skipped("a15b"); await WhenAsync("a15-when"); await ThenAsync("a15-then"); }

    // ---- BypassStep() called from a step's own body: "bypass the current active step" ----
    [GivenStep] public void Self_bypass_sync(string l) { StepCollector.BypassStep("self"); }
    [GivenStep] public Task Self_bypass_task(string l) { StepCollector.BypassStep("self"); return Task.CompletedTask; }
    [GivenStep] public async Task Self_bypass_async_first(string l) { StepCollector.BypassStep("self"); await Task.Yield(); }
    [GivenStep] public async Task Self_bypass_async_later(string l) { await Task.Yield(); StepCollector.BypassStep("self"); }
    [Step] public void Outer_self_sync(string l) { Self_bypass_sync(l); C.Make(l + "-after").GetAwaiter().GetResult(); }
    [Step] public async Task Outer_self_task(string l) { await Self_bypass_task(l); await C.Make(l + "-after"); }
    [Step] public async Task Outer_self_first(string l) { await Self_bypass_async_first(l); await C.Make(l + "-after"); }
    [Step] public async Task Outer_self_later(string l) { await Self_bypass_async_later(l); await C.Make(l + "-after"); }

    [Fact] public async Task A20_self_bypass_sync_in_a_parent() { Outer_self_sync("a20"); await WhenAsync("a20-when"); await ThenAsync("a20-then"); }
    [Fact] public async Task A21_self_bypass_task_in_a_parent() { await Outer_self_task("a21"); await WhenAsync("a21-when"); await ThenAsync("a21-then"); }
    [Fact] public async Task A22_self_bypass_async_before_await_in_a_parent() { await Outer_self_first("a22"); await WhenAsync("a22-when"); await ThenAsync("a22-then"); }
    [Fact] public async Task A23_self_bypass_async_after_await_in_a_parent() { await Outer_self_later("a23"); await WhenAsync("a23-when"); await ThenAsync("a23-then"); }

    // A skipped step run on a child flow, its parent completing on its own flow.
    [GivenStep] public void Parent_skips_in_task_run(string l) { Task.Run(() => SkippedSync()).GetAwaiter().GetResult(); C.Make(l).GetAwaiter().GetResult(); }
    [Fact] public async Task A30_skip_in_task_run_in_a_sync_parent() { Parent_skips_in_task_run("a30"); await WhenAsync("a30-when"); await ThenAsync("a30-then"); }

    // SkipIf's getter throwing: preamble is outside the try.
    [WhenStep(SkipIf = nameof(Throws))] public void When_skipif_throws() { }
    [Fact] public async Task A31_a_SkipIf_getter_that_throws_is_caught()
    {
        await GivenAsync("a31-given");
        try { When_skipif_throws(); } catch (InvalidOperationException) { }
        await C.Make("a31-after");
        await WhenAsync("a31-when");
        await ThenAsync("a31-then");
    }
}

// ===================== B: a step left running past its parent ======================
public class B_LeftRunning : Base
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _worker;

    [Step] public async Task WorkerStep(string label) { await C.Make(label + "-worker-start"); await _gate.Task; await C.Make(label + "-worker-end"); }

    // The everyday shape: a sync Given that starts a worker it does not await.
    [GivenStep] public void Given_the_worker_runs(string label) { _worker = WorkerStep(label); }
    [GivenStep] public async Task GivenAsync_the_worker_runs(string label) { _worker = WorkerStep(label); await C.Make(label + "-given-call"); }
    [GivenStep] public void Given_timeout_wait(string label) { Task.WhenAny(WorkerStep(label), Task.Delay(20)).GetAwaiter().GetResult(); }

    [Fact] public async Task B01_sync_Given_leaves_a_worker_step_running_then_When_Then()
    {
        Given_the_worker_runs("b01");
        await C.Make("b01-after-given");
        await WhenAsync("b01-when");
        await NoSetup.Make("b01-after-when-nosetup");
        await ThenAsync("b01-then");
        await NsThenAsync("b01-then-nosetup");
        _gate.SetResult(); await _worker!;
    }

    [Fact] public async Task B02_async_Given_leaves_a_worker_step_running_then_When_Then()
    {
        await GivenAsync_the_worker_runs("b02");
        await WhenAsync("b02-when");
        await ThenAsync("b02-then");
        _gate.SetResult(); await _worker!;
    }

    [Fact] public async Task B03_sync_Given_waits_with_a_timeout_on_a_step()
    {
        Given_timeout_wait("b03");
        await WhenAsync("b03-when");
        await NsThenAsync("b03-then-nosetup");
        _gate.SetResult();
        await Task.Delay(20);
    }

    // The same with a When left open: a later Given stays Action
    [WhenStep] public void When_the_worker_runs(string label) { _worker = WorkerStep(label); }
    [Fact] public async Task B04_sync_When_leaves_a_worker_running_then_a_Given_and_When()
    {
        When_the_worker_runs("b04");
        await GivenAsync("b04-given");
        await WhenAsync("b04-when");
        _gate.SetResult(); await _worker!;
    }

    // Two sync steps in a row: the left-open one finishes between them.
    [Fact] public async Task B05_sync_Given_worker_ends_before_the_When()
    {
        Given_the_worker_runs("b05");
        _gate.SetResult(); await _worker!;
        await WhenAsync("b05-when");
        await ThenAsync("b05-then");
    }

    // A sync Given whose background Task.Run step is still open when the Given returns.
    [GivenStep] public void Given_task_run_worker(string label)
    {
        var started = new TaskCompletionSource();
        _worker = Task.Run(async () => { var t = WorkerStep(label); started.SetResult(); await t; });
        started.Task.GetAwaiter().GetResult();
    }
    [Fact] public async Task B06_sync_Given_with_a_Task_Run_worker_step_then_When()
    {
        Given_task_run_worker("b06");
        await WhenAsync("b06-when");
        await NsThenAsync("b06-then-nosetup");
        _gate.SetResult(); await _worker!;
    }
}

// ===================== C: background loops ======================
public class C_Background : Base
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _bg;

    [ThenStep] public async Task Poll_then(string label) => await C.Make(label);
    [WhenStep] public async Task Poll_when(string label) => await C.Make(label);
    [GivenStep] public async Task Poll_given(string label) => await C.Make(label);
    [Step] public async Task Poll_plain(string label) => await NoSetup.Make(label);

    // Loop started in a Given; each pass is a [ThenStep] poll on the loop's flow.
    [GivenStep]
    public void Given_a_poller(string label)
    {
        _bg = Task.Run(async () =>
        {
            var i = 0;
            while (!_stop.IsCancellationRequested) { await Poll_then($"{label}-poll{i++}"); await Task.Delay(15); }
        });
    }

    [Fact]
    public async Task C01_a_Given_starts_a_loop_of_Then_steps_then_more_setup_then_When()
    {
        Given_a_poller("c01");
        await Task.Delay(80);
        await GivenAsync("c01-second-given");
        await NsGivenAsync("c01-second-given-nosetup");
        await WhenAsync("c01-when");
        await ThenAsync("c01-then");
        _stop.Cancel(); await _bg!;
    }

    // Loop started BEFORE any step; each pass is a keyword-free [Step]. On a TrackDuringSetup=false client.
    [Fact]
    public async Task C02_a_loop_of_keyword_free_steps_started_before_any_step()
    {
        var bg = Task.Run(async () =>
        {
            var i = 0;
            while (!_stop.IsCancellationRequested) { await Poll_plain($"c02-poll{i++}"); await Task.Delay(15); }
        });
        await Task.Delay(20);
        await GivenAsync("c02-given");
        await Task.Delay(60);
        await WhenAsync("c02-when");
        await Task.Delay(60);
        await ThenAsync("c02-then");
        await Task.Delay(30);
        _stop.Cancel(); await bg;
    }

    // Loop of keyword-free steps started in a Given: the flow inherits Setup at both versions.
    [GivenStep]
    public void Given_a_plain_poller(string label)
    {
        _bg = Task.Run(async () =>
        {
            var i = 0;
            while (!_stop.IsCancellationRequested) { await Poll_plain($"{label}-poll{i++}"); await Task.Delay(15); }
        });
    }

    [Fact]
    public async Task C03_a_loop_of_keyword_free_steps_started_in_a_Given()
    {
        Given_a_plain_poller("c03");
        await Task.Delay(30);
        await WhenAsync("c03-when");
        await Task.Delay(30);
        _stop.Cancel(); await _bg!;
    }

    // A Given step in the background while the test acts.
    [Fact]
    public async Task C04_a_background_Given_step_while_the_test_is_in_Action()
    {
        await GivenAsync("c04-given");
        await WhenAsync("c04-when");
        var bg = Task.Run(() => Poll_given("c04-bg-given"));
        await bg;
        await PlainAsync("c04-plain-after");  // has Action on its flow
        await C.Make("c04-raw-after");
        await Task.Run(() => PlainAsync("c04-plain-in-run"));
    }

    // A detached flow, an explicit identity and a When
    [Fact]
    public async Task C05_a_When_on_a_detached_flow_with_an_explicit_identity()
    {
        var id = Id; var name = TestContext.Current.Test!.TestDisplayName;
        await GivenAsync("c05-given");
        Task t;
        using (ExecutionContext.SuppressFlow())
            t = Task.Run(async () =>
            {
                using (TestIdentityScope.Begin(name, id))
                    await Poll_when("c05-detached-when");
            });
        await t;
        await GivenAsync("c05-second-given");
        await WhenAsync("c05-when");
    }
}

// ===================== D: the fill ======================
public class D_Fill : Base
{
    [Fact] public async Task D01_reset_with_TestPhaseContext_Unknown_as_the_wiki_does_then_a_keyword_free_step()
    {
        await GivenAsync("d01-given");
        TestPhaseContext.Current = TestPhase.Unknown; // the wiki's "Reset for tests"
        await NoSetup.Make("d01-raw-after-reset");
        await NsPlainAsync("d01-plain-after-reset");
        await NoSetup.Make("d01-raw-after-plain");
    }

    [Fact] public async Task D02_reset_with_Reset_after_a_When_then_a_keyword_free_step()
    {
        await GivenAsync("d02-given");
        await WhenAsync("d02-when");
        TestPhaseContext.Reset();
        await C.Make("d02-raw-after-reset");
        await PlainAsync("d02-plain-after-reset");
        await C.Make("d02-raw-after-plain");
    }

    // Direct set, no StartSetup: then a keyword-free step, flow has it: no fill.
    [Fact] public async Task D03_a_phase_set_directly_is_kept_by_a_keyword_free_step()
    {
        await GivenAsync("d03-given");
        TestPhaseContext.Current = TestPhase.Action;
        await PlainAsync("d03-plain");
        await C.Make("d03-raw");
    }

    // StartSetup after a When: carried stays Action
    [Fact] public async Task D04_StartSetup_after_a_When_then_a_step_in_a_flow_without_a_phase()
    {
        await GivenAsync("d04-given");
        await WhenAsync("d04-when");
        TrackingDiagramOverride.StartSetup();
        await C.Make("d04-raw-in-setup");
        await Task.Run(async () => { TestPhaseContext.Reset(); await PlainAsync("d04-plain-in-a-flow-with-no-phase"); });
    }
}

public class D_FixtureGiven : Base, IAsyncLifetime
{
    // A base class's arrange step, the woven [GivenStep] run from InitializeAsync. Whether its phase reaches the test depends on the flow.
    public async ValueTask InitializeAsync() { await SeedAsync("d10-seed"); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    [GivenStep] public async Task SeedAsync(string label) => await C.Make(label);

    [Fact] public async Task D10_a_Given_in_InitializeAsync_then_keyword_free_steps_on_a_TrackDuringSetup_false_client()
    {
        await NoSetup.Make("d10-raw-before");
        await NsPlainAsync("d10-plain-1");
        await NoSetup.Make("d10-raw-after");
        NsPlainSync("d10-plain-2");
        await NoSetup.Make("d10-raw-last");
    }

    [Fact] public async Task D11_a_Given_in_InitializeAsync_then_When_then_keyword_free()
    {
        await NsWhenAsync("d11-when");
        await NsPlainAsync("d11-plain");
    }
}

public class D_CtorGiven : Base
{
    public D_CtorGiven() { SeedSync("d20-ctor-seed"); }
    [GivenStep] public void SeedSync(string label) => C.Make(label).GetAwaiter().GetResult();

    [Fact] public async Task D20_a_Given_in_the_constructor_then_keyword_free_steps_on_a_TrackDuringSetup_false_client()
    {
        await NoSetup.Make("d20-raw-before");
        await NsPlainAsync("d20-plain-1");
        await NoSetup.Make("d20-raw-after");
    }
}

public class D_DisposeStep : Base, IAsyncLifetime
{
    private readonly Calls _trackNoAction = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringAction = false }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public async ValueTask DisposeAsync() { await CleanupAsync("d30-cleanup"); }
    [Step] public async Task CleanupAsync(string label) => await _trackNoAction.Make(label);

    [Fact] public async Task D30_a_keyword_free_step_in_DisposeAsync_after_a_When()
    {
        await GivenAsync("d30-given");
        await WhenAsync("d30-when");
    }
}

// ===================== E: the marker ======================
public class E_Marker : Base
{
    [Fact] public async Task E01_Given_with_no_tracked_calls_then_When()
    {
        GivenNoCall();
        await WhenAsync("e01-when");
        await ThenAsync("e01-then");
    }

    [Fact] public async Task E02_Given_with_only_dropped_calls_then_When()
    {
        await NsGivenAsync("e02-given-dropped");
        await WhenAsync("e02-when");
    }

    [GivenStep] public async Task GivenOnlyADroppedCall(string label) => await NoSetup.HttpOnly(label);

    [Fact] public async Task E90_Given_whose_only_call_is_dropped_then_When()
    {
        await GivenOnlyADroppedCall("e90-given-dropped");
        await WhenAsync("e90-when");
    }

    [Fact] public async Task E03_Given_with_calls_then_When()
    {
        await GivenAsync("e03-given");
        await WhenAsync("e03-when");
    }

    [WhenStep] public async Task When_with_arrange_then_explicit_boundary(string l)
    {
        await C.Make(l + "-arrange-inside-when");
        TrackingDiagramOverride.StartAction();
        await C.Make(l + "-act");
    }

    [Fact] public async Task E04_explicit_StartAction_later_than_the_When_bar()
    {
        GivenNoCall();
        await When_with_arrange_then_explicit_boundary("e04");
    }

    [Fact] public async Task E05_When_then_Given_then_When_a_second_act()
    {
        await WhenAsync("e05-first-when");
        await ThenAsync("e05-first-then");
        await GivenAsync("e05-given");
        await WhenAsync("e05-second-when");
        await ThenAsync("e05-second-then");
    }

    [Fact] public async Task E06_test_body_calls_then_Given_no_call_then_When()
    {
        await C.Make("e06-raw-before-any-step");
        GivenNoCall();
        await WhenAsync("e06-when");
    }

    [Fact] public async Task E07_StartSetup_then_When_step_no_StartAction()
    {
        TrackingDiagramOverride.StartSetup();
        await C.Make("e07-setup-call");
        await WhenAsync("e07-when");
        await ThenAsync("e07-then");
    }

    [WhenStep] public void When_no_call() { }

    [Fact] public async Task E08_no_calls_at_all_with_delimiters_off()
    {
        var saved = StepCollector.Options;
        StepCollector.Options = saved with { ShowStepDelimiters = false };
        try { GivenNoCall(); When_no_call(); } finally { StepCollector.Options = saved; }
        await Task.CompletedTask;
    }

    [Fact] public async Task E09_no_calls_at_all_with_delimiters_on()
    {
        GivenNoCall(); When_no_call();
        await Task.CompletedTask;
    }

    [Fact] public async Task E10_identical_calls_across_the_boundary_with_delimiters_off()
    {
        var saved = StepCollector.Options;
        StepCollector.Options = saved with { ShowStepDelimiters = false };
        try { await GivenAsync("e10-same"); await WhenAsync("e10-same"); } finally { StepCollector.Options = saved; }
    }

    [Fact] public async Task E11_delimiters_off_Given_no_call_then_When_first_log_is_the_marker()
    {
        var saved = StepCollector.Options;
        StepCollector.Options = saved with { ShowStepDelimiters = false };
        try { GivenNoCall(); await WhenAsync("e11-when"); } finally { StepCollector.Options = saved; }
    }

    [GivenStep] public void Given_arrange_with_custom_note(string l) { C.Make(l).GetAwaiter().GetResult(); TrackingDiagramOverride.InsertPlantUml("note over Shop: custom"); C.Make(l + "-2").GetAwaiter().GetResult(); }

    [Fact] public async Task E12_a_custom_fragment_in_setup()
    {
        Given_arrange_with_custom_note("e12");
        await WhenAsync("e12-when");
    }

    [Step] public async Task Wrapper(string l) { await GivenAsync(l + "-given"); await WhenAsync(l + "-when"); }

    [Fact] public async Task E13_wrapper_top_level_bar() { await Wrapper("e13"); await ThenAsync("e13-then"); }

    [Fact] public async Task E14_WhenTriggersAction_false()
    {
        var saved = StepCollector.Options;
        StepCollector.Options = saved with { WhenTriggersAction = false };
        try { await GivenAsync("e14-given"); await WhenAsync("e14-when"); await PlainAsync("e14-plain"); TrackingDiagramOverride.StartAction(); await PlainAsync("e14-plain-after-startaction"); } finally { StepCollector.Options = saved; }
    }
}

// ===================== F: concurrency inside a test ======================
public class F_Concurrency : Base
{
    private readonly TaskCompletionSource _gateB = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [WhenStep] public async Task WhenB_with_nested_Given(string l, TaskCompletionSource started, TaskCompletionSource waitFor)
    {
        started.SetResult();
        await waitFor.Task;
        await C.Make(l + "-before-nested");
        await GivenAsync(l + "-nested-given");
        await C.Make(l + "-after-nested");
    }
    [WhenStep] public async Task WhenAWait(string l, TaskCompletionSource started) { await started.Task; await C.Make(l); }

    // A ends first and pops B's record; B's nested Given then has no open step above it.
    [Fact] public async Task F01_WhenAll_of_two_Whens_the_first_ends_first_the_second_nests_a_Given()
    {
        await GivenAsync("f01-given");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = WhenAWait("f01-when-a", started);
        var b = WhenB_with_nested_Given("f01-when-b", started, _gateB);
        await a;
        _gateB.SetResult();
        await b;
        await C.Make("f01-after");
        await PlainAsync("f01-plain");
    }

    // A When holding a WhenAll of Given sub-steps, finishing out of order.
    [WhenStep] public async Task When_holding_WhenAll(string l)
    {
        var slowGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = SubGiven(l + "-sub-slow", slowGate);
        var fast = SubGiven(l + "-sub-fast", null);
        await fast;
        slowGate.SetResult();
        await slow;
        await C.Make(l + "-after-subs");
    }
    [GivenStep] public async Task SubGiven(string l, TaskCompletionSource? gate) { if (gate is not null) await gate.Task; await C.Make(l); }

    [Fact] public async Task F02_a_When_holding_a_WhenAll_of_Given_sub_steps_out_of_order()
    {
        await GivenAsync("f02-given");
        await When_holding_WhenAll("f02");
        await ThenAsync("f02-then");
    }

    // Reverse: a Given holding When sub-steps in a WhenAll, ends with sub-steps out of order.
    [GivenStep] public async Task Given_holding_WhenAll_of_When(string l)
    {
        var slowGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = SubWhen(l + "-sub-slow", slowGate);
        var fast = SubWhen(l + "-sub-fast", null);
        await fast;
        slowGate.SetResult();
        await slow;
        await C.Make(l + "-after-subs");
    }
    [WhenStep] public async Task SubWhen(string l, TaskCompletionSource? gate) { if (gate is not null) await gate.Task; await C.Make(l); }

    [Fact] public async Task F03_a_Given_holding_a_WhenAll_of_When_sub_steps_out_of_order()
    {
        await Given_holding_WhenAll_of_When("f03");
        await WhenAsync("f03-when");
    }

    // Parallel.Invoke of sync steps in a sync parent.
    [GivenStep] public void Given_parallel_invoke(string l)
    {
        Parallel.Invoke(() => SyncWhenSleep(l + "-p1"), () => SyncWhenSleep(l + "-p2"), () => SyncWhenSleep(l + "-p3"));
        C.Make(l + "-after-parallel").GetAwaiter().GetResult();
    }
    [WhenStep] public void SyncWhenSleep(string l) { C.Make(l).GetAwaiter().GetResult(); Thread.Sleep(5); }

    [Fact] public async Task F04_a_Given_with_Parallel_Invoke_of_When_sub_steps()
    {
        Given_parallel_invoke("f04");
        await C.Make("f04-raw");
        await WhenAsync("f04-when");
        await ThenAsync("f04-then");
    }

    // A step started on a new thread (execution context flows), joined.
    [Fact] public async Task F05_a_When_on_a_new_thread_joined_then_a_call()
    {
        await GivenAsync("f05-given");
        var t = new Thread(() => WhenSync("f05-when-on-thread"));
        t.Start(); t.Join();
        await C.Make("f05-raw-after-join");
        await PlainAsync("f05-plain-after-join");
        await C.Make("f05-raw-after-plain");
        await ThenAsync("f05-then");
    }

    // Exception caught by the test: sync and async, then what?
    [GivenStep] public void Given_throws_sync(string l) { C.Make(l).GetAwaiter().GetResult(); throw new InvalidOperationException("x"); }
    [GivenStep] public async Task Given_throws_async(string l) { await C.Make(l); throw new InvalidOperationException("x"); }
    [WhenStep] public void When_throws_sync(string l) { C.Make(l).GetAwaiter().GetResult(); throw new InvalidOperationException("x"); }

    [Fact] public async Task F06_exceptions_caught_by_the_test()
    {
        try { Given_throws_sync("f06-g1"); } catch (InvalidOperationException) { }
        await C.Make("f06-raw-1");
        try { await Given_throws_async("f06-g2"); } catch (InvalidOperationException) { }
        await C.Make("f06-raw-2");
        try { When_throws_sync("f06-w"); } catch (InvalidOperationException) { }
        await C.Make("f06-raw-3");
        await PlainAsync("f06-plain");
        await ThenAsync("f06-then");
    }

    // A failed sub-step in a Task.WhenAll, the parent catches.
    [GivenStep] public async Task Given_catching_WhenAll(string l)
    {
        var ok = SubGivenWait(l + "-ok");
        var bad = Given_throws_async(l + "-bad");
        try { await Task.WhenAll(ok, bad); } catch (InvalidOperationException) { }
        await C.Make(l + "-after");
    }
    [GivenStep] public async Task SubGivenWait(string l) { await Task.Delay(10); await C.Make(l); }

    [Fact] public async Task F07_WhenAll_with_a_failing_sub_step_caught_in_a_Given()
    {
        await Given_catching_WhenAll("f07");
        await WhenAsync("f07-when");
        await ThenAsync("f07-then");
    }
}
