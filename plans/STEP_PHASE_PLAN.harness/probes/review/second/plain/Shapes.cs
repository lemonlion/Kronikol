using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe.Plain;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose() { Dump.Write("plain"); }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public class Shapes : DiagrammedComponentTest
{
    private readonly Calls _calls = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerAfterAWait() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    // The handler the plan's section 9 has in mind: StartSetup() to hide a bit, with TrackDuringSetup = false.
    private readonly Calls _hiding = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = false }) { InnerHandler = new AnswerAfterAWait() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    public bool Always => true;

    private static readonly AsyncLocal<bool> NoId = new();

    [Step] public async Task PlainAsync(string label) => await _calls.Make(label);
    [GivenStep] public async Task GivenAsync(string label) => await _calls.Make(label);
    [GivenStep] public void GivenSync(string label) => _calls.Make(label).GetAwaiter().GetResult();
    [WhenStep] public async Task WhenAsync(string label) => await _calls.Make(label);
    [ThenStep] public async Task ThenAsync(string label) => await _calls.Make(label);

    // ---- skipped ----
    [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public async Task GivenSkippedAsync(string label) => await _calls.Make(label);
    [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public void GivenSkippedSync(string label) => _calls.Make(label).GetAwaiter().GetResult();

    [WhenStep]
    public async Task WhenWithSkippedAsyncSub(string l)
    {
        await _calls.Make($"{l}-before");
        await GivenSkippedAsync($"{l}-skipped");
        await GivenAsync($"{l}-real-sub");
        await _calls.Make($"{l}-after");
    }

    [WhenStep]
    public void WhenWithSkippedSyncSub(string l)
    {
        _calls.Make($"{l}-before").GetAwaiter().GetResult();
        GivenSkippedSync($"{l}-skipped");
        GivenSync($"{l}-real-sub");
        _calls.Make($"{l}-after").GetAwaiter().GetResult();
    }

    [Step]
    public async Task WrapperSkipsFirst(string l)
    {
        await GivenSkippedAsync($"{l}-skipped");
        await _calls.Make($"{l}-wrapper-body");
    }

    [GivenStep]
    public async Task GivenOverAWrapperThatSkips(string l)
    {
        await WrapperSkipsFirst(l);
        await _calls.Make($"{l}-after-wrapper");
        await WhenAsync($"{l}-when-sub");
        await _calls.Make($"{l}-after-when-sub");
    }

    [Fact]
    public async Task A1_a_When_with_a_skipped_async_sub_step_then_a_real_Given_sub_step()
    {
        await GivenAsync("a1-given");
        await WhenWithSkippedAsyncSub("a1");
        await ThenAsync("a1-then");
    }

    [Fact]
    public async Task A2_the_same_sync()
    {
        await GivenAsync("a2-given");
        WhenWithSkippedSyncSub("a2");
        await ThenAsync("a2-then");
    }

    [Fact]
    public async Task A3_a_Given_over_a_wrapper_that_skips_then_a_When_sub_step()
    {
        await GivenOverAWrapperThatSkips("a3");
        await WhenAsync("a3-real-when");
    }

    // ---- a stub that throws before it has a task ----
    [GivenStep]
    public Task GivenThatThrowsBeforeItsTask(string l)
    {
        if (l.Length > 0)
            throw new InvalidOperationException("bad argument");
        return _calls.Make(l);
    }

    [Fact]
    public async Task B1_a_caught_throw_from_a_non_async_Given_stub()
    {
        try { await GivenThatThrowsBeforeItsTask("b1-never"); } catch (InvalidOperationException) { }
        await _calls.Make("b1-after-caught");
        await WhenAsync("b1-when");
        await _calls.Make("b1-after-when");
        await ThenAsync("b1-then");
    }

    // ---- the carry ----
    [GivenStep]
    public async Task GivenWithKeywordFreeSub(string l)
    {
        await _calls.Make($"{l}-first");
        await Task.Delay(60);
        await PlainAsync($"{l}-sub-step");
        await _calls.Make($"{l}-after-sub");
    }

    [GivenStep]
    public async Task GivenWithGivenSub(string l)
    {
        await _calls.Make($"{l}-first");
        await Task.Delay(60);
        await GivenAsync($"{l}-sub-step");
        await _calls.Make($"{l}-after-sub");
    }

    [WhenStep]
    public async Task WhenWithKeywordFreeSub(string l)
    {
        await _calls.Make($"{l}-first");
        await Task.Delay(60);
        await PlainAsync($"{l}-sub-step");
        await _calls.Make($"{l}-after-sub");
    }

    private async Task HiddenHelper(string label)
    {
        TrackingDiagramOverride.StartSetup();
        await _hiding.Make(label);
    }

    [WhenStep]
    public async Task WhenWithAHiddenHelper(string l)
    {
        await _hiding.Make($"{l}-before");
        await HiddenHelper($"{l}-hidden");
        await _hiding.Make($"{l}-after-helper");
        await PlainHiding($"{l}-step-after");
        await _hiding.Make($"{l}-after-step");
    }

    [Step] public async Task PlainHiding(string label) => await _hiding.Make(label);

    [Step]
    public async Task ArrangeWithStartSetup(string l)
    {
        TrackingDiagramOverride.StartSetup();
        await _hiding.Make($"{l}-arrange");
    }

    [Fact]
    public async Task C1_a_Given_with_a_keyword_free_sub_step_overlapped_by_a_When()
    {
        await Task.WhenAll(GivenWithKeywordFreeSub("c1"), WhenAsync("c1-when"));
    }

    [Fact]
    public async Task C1b_a_Given_with_a_Given_sub_step_overlapped_by_a_When()
    {
        await Task.WhenAll(GivenWithGivenSub("c1b"), WhenAsync("c1b-when"));
    }

    [Fact]
    public async Task C2_a_When_with_a_keyword_free_sub_step_overlapped_by_a_Given()
    {
        var when = WhenWithKeywordFreeSub("c2");
        await GivenAsync("c2-given");
        await when;
    }

    [Fact]
    public async Task C3_StartSetup_in_an_async_helper_then_a_keyword_free_sub_step()
    {
        await GivenAsync("c3-given");
        await WhenWithAHiddenHelper("c3");
        await ThenAsync("c3-then");
    }

    [Fact]
    public async Task C4_keyword_free_steps_with_StartSetup_in_the_first()
    {
        await ArrangeWithStartSetup("c4");
        await PlainHiding("c4-act");
        await _hiding.Make("c4-after-act");
    }

    // ---- a step closed by another flow's completion ----
    [GivenStep]
    public async Task GivenOpen(string l, TaskCompletionSource gate)
    {
        await _calls.Make(l);
        await gate.Task;
    }

    [WhenStep]
    public async Task WhenReleasesThenNests(string l, TaskCompletionSource gate)
    {
        await _calls.Make($"{l}-before");
        gate.SetResult();
        await Task.Delay(30);
        await GivenAsync($"{l}-sub");
        await _calls.Make($"{l}-after");
    }

    [Fact]
    public async Task E1_a_Given_left_running_ends_during_a_When_that_then_nests_a_Given()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var open = GivenOpen("e1-open", gate);
        await WhenReleasesThenNests("e1", gate);
        await open;
        await _calls.Make("e1-end");
    }

    // ---- shapes the chain should get right ----
    [GivenStep] public Task<int> GivenNonAsyncOfT(string l) => GivenOfTBody(l);
    private async Task<int> GivenOfTBody(string l) { await _calls.Make(l); return 1; }
    [WhenStep] public Task WhenNonAsync(string l) => WhenNonAsyncBody(l);
    private async Task WhenNonAsyncBody(string l)
    {
        await _calls.Make($"{l}-first");
        var one = await GivenNonAsyncOfT($"{l}-sub");
        await _calls.Make($"{l}-after-{one}");
    }

    [WhenStep]
    public async Task WhenRunsSubInTaskRunAndThread(string l)
    {
        await Task.Run(() => GivenAsync($"{l}-in-run"));
        await _calls.Make($"{l}-after-run").ConfigureAwait(false);
        var thread = new Thread(() => GivenSync($"{l}-in-thread"));
        thread.Start();
        thread.Join();
        await _calls.Make($"{l}-after-thread").ConfigureAwait(false);
        await GivenAsync($"{l}-resumed-elsewhere").ConfigureAwait(false);
        await _calls.Make($"{l}-end");
    }

    [Fact]
    public async Task G1_nested_non_async_Task_and_Task_of_T()
    {
        await GivenAsync("g1-given");
        await WhenNonAsync("g1");
        await ThenAsync("g1-then");
    }

    [Fact]
    public async Task H1_sub_steps_in_Task_Run_a_thread_and_resumed_elsewhere()
    {
        await GivenAsync("h1-given");
        await WhenRunsSubInTaskRunAndThread("h1");
        await ThenAsync("h1-then");
    }

    // ---- a nested step whose test id resolves to null ----
    [Step] public async Task NoIdStep(string label) => await _calls.Make(label);

    [WhenStep]
    public async Task WhenWithANoIdSub(string l)
    {
        await _calls.Make($"{l}-before");
        NoId.Value = true;
        await NoIdStep($"{l}-no-id");
        NoId.Value = false;
        await GivenAsync($"{l}-real-sub");
        await _calls.Make($"{l}-after");
    }

    [Fact]
    public async Task I1_a_nested_step_with_no_test_id()
    {
        var saved = Track.TestIdResolver;
        var id = saved!();
        Track.TestIdResolver = () => NoId.Value ? null : id;
        try
        {
            await GivenAsync("i1-given");
            await WhenWithANoIdSub("i1");
            await ThenAsync("i1-then");
        }
        finally { Track.TestIdResolver = saved; }
    }
}
