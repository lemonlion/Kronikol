// Stamped by templates/stamp.py from xunit3-plain/Shapes.cs and templates/Threads.fragment.cs: edit those.
using Kronikol;
using Kronikol.Tracking;
using Kronikol.TUnit;
using PhaseProbe;
using TUnit.Core;

namespace PhaseProbe.Suite;

public class Shapes : DiagrammedComponentTest
{
    private readonly Calls _calls = ProbeEnv.NewCalls();

    public bool Always => true;

    // ---- leaf steps ----
    [Step] public async Task PlainAsync(string label) => await _calls.Make(label);
    [GivenStep] public async Task GivenAsync(string label) => await _calls.Make(label);
    [GivenStep] public void GivenSync(string label) => _calls.Make(label).GetAwaiter().GetResult();
    [GivenStep] public Task GivenTaskNoAsync(string label) => _calls.Make(label);
    [GivenStep] public async Task GivenThrows(string label) { await _calls.Make(label); throw new InvalidOperationException("caught"); }
    [WhenStep] public async Task WhenAsync(string label) => await _calls.Make(label);
    [WhenStep] public void WhenSync(string label) => _calls.Make(label).GetAwaiter().GetResult();
    [ThenStep] public async Task ThenAsync(string label) => await _calls.Make(label);
    [ButStep] public async Task ButAsync(string label) => await _calls.Make(label);
    [ButWhenStep] public async Task ButWhenAsync(string label) => await _calls.Make(label);
    [WhenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public async Task WhenSkipped(string label) => await _calls.Make(label);

    // ---- composite steps ----
    [WhenStep]
    public async Task WhenWithGivenSub(string label)
    {
        await _calls.Make($"{label}-before-sub");
        await GivenAsync($"{label}-in-sub");
        await _calls.Make($"{label}-after-sub");
    }

    [GivenStep]
    public async Task GivenWithWhenSub(string label)
    {
        await _calls.Make($"{label}-before-sub");
        await WhenAsync($"{label}-in-sub");
        await _calls.Make($"{label}-after-sub");
    }

    [WhenStep]
    public void WhenSyncWithGivenSyncSub(string label)
    {
        _calls.Make($"{label}-before-sub").GetAwaiter().GetResult();
        GivenSync($"{label}-in-sub");
        _calls.Make($"{label}-after-sub").GetAwaiter().GetResult();
    }

    [Step]
    public async Task WrapperAsync(string label)
    {
        await _calls.Make($"{label}-wrapper-first");
        await GivenAsync($"{label}-wrapped-given");
        await _calls.Make($"{label}-wrapper-after-given");
        await WhenAsync($"{label}-wrapped-when");
        await _calls.Make($"{label}-wrapper-after-when");
    }

    [Step]
    public void WrapperSync(string label)
    {
        _calls.Make($"{label}-wrapper-first").GetAwaiter().GetResult();
        GivenSync($"{label}-wrapped-given");
        _calls.Make($"{label}-wrapper-after-given").GetAwaiter().GetResult();
        WhenSync($"{label}-wrapped-when");
        _calls.Make($"{label}-wrapper-after-when").GetAwaiter().GetResult();
    }

    [GivenStep]
    public async Task GivenStartsActionExplicitly(string label)
    {
        await _calls.Make($"{label}-before-explicit");
        TrackingDiagramOverride.StartAction();
        await _calls.Make($"{label}-after-explicit");
    }

    [WhenStep]
    public async Task<int> WhenTaskOfT(string label)
    {
        await _calls.Make(label);
        return 1;
    }

    [GivenStep]
    public async Task GivenOpen(string label, TaskCompletionSource gate)
    {
        await _calls.Make(label);
        await gate.Task;
    }

    // Not a step: an ordinary async helper.
    private async Task Arrange(string label)
    {
        await GivenAsync($"{label}-in-helper");
        await _calls.Make($"{label}-helper-after-given");
    }

    // ---- tests ----
    [Test]
    public async Task X01_When_with_nested_Given_async()
    {
        await GivenAsync("x01-given");
        await WhenWithGivenSub("x01");
        await _calls.Make("x01-after-when-step");
        await ThenAsync("x01-then");
    }

    [Test]
    public async Task X02_Given_with_nested_When_async()
    {
        await GivenWithWhenSub("x02");
        await _calls.Make("x02-after-given-step");
        await WhenAsync("x02-when");
    }

    [Test]
    public void X03_When_with_nested_Given_sync()
    {
        GivenSync("x03-given");
        WhenSyncWithGivenSyncSub("x03");
        _calls.Make("x03-after-when-step").GetAwaiter().GetResult();
    }

    [Test]
    public async Task X04_keyword_free_wrapper_async()
    {
        await WrapperAsync("x04");
        await _calls.Make("x04-after-wrapper");
    }

    [Test]
    public void X05_keyword_free_wrapper_sync()
    {
        WrapperSync("x05");
        _calls.Make("x05-after-wrapper").GetAwaiter().GetResult();
    }

    [Test]
    public async Task X06_every_top_level_keyword_in_turn()
    {
        await _calls.Make("x06-before-any-step");
        await GivenAsync("x06-given");
        await _calls.Make("x06-after-given");
        await ButAsync("x06-but");
        await _calls.Make("x06-after-but");
        await WhenAsync("x06-when");
        await _calls.Make("x06-after-when");
        await ButAsync("x06-but-after-when");
        await _calls.Make("x06-after-but-after-when");
        await ButWhenAsync("x06-butwhen");
        await _calls.Make("x06-after-butwhen");
        await ThenAsync("x06-then");
        await _calls.Make("x06-after-then");
        await PlainAsync("x06-plain");
        await _calls.Make("x06-after-plain");
    }

    [Test]
    public async Task X07_explicit_StartAction_inside_a_Given_step()
    {
        await GivenStartsActionExplicitly("x07");
        await _calls.Make("x07-after-step");
    }

    [Test]
    public async Task X08_explicit_StartAction_and_StartSetup_in_the_test()
    {
        await _calls.Make("x08-first");
        TrackingDiagramOverride.StartSetup();
        await _calls.Make("x08-after-startsetup");
        TrackingDiagramOverride.StartAction();
        await _calls.Make("x08-after-startaction");
    }

    [Test]
    public async Task X09_a_step_left_open_then_a_When()
    {
        var gate = new TaskCompletionSource();
        var open = GivenOpen("x09-open-given", gate);
        await _calls.Make("x09-while-open");
        await WhenAsync("x09-when-while-open");
        await _calls.Make("x09-after-when-while-open");
        gate.SetResult();
        await open;
        await _calls.Make("x09-after-closed");
    }

    [Test]
    public async Task X10_WhenAll_of_a_Given_and_a_When()
    {
        await Task.WhenAll(GivenAsync("x10-given"), WhenAsync("x10-when"));
        await _calls.Make("x10-after");
    }

    [Test]
    public async Task X11_a_When_step_in_Task_Run()
    {
        await GivenAsync("x11-given");
        await Task.Run(() => WhenAsync("x11-when-in-run"));
        await _calls.Make("x11-after-run");
    }

    [Test]
    public async Task X12_steps_in_an_ordinary_async_helper()
    {
        await Arrange("x12");
        await _calls.Make("x12-after-helper");
        await WhenAsync("x12-when");
    }

    [Test]
    public async Task X13_caught_failure_of_a_nested_Given()
    {
        await WhenCatches("x13");
        await _calls.Make("x13-after-when-step");
    }

    [WhenStep]
    public async Task WhenCatches(string label)
    {
        try { await GivenThrows($"{label}-in-sub"); } catch (InvalidOperationException) { }
        await _calls.Make($"{label}-after-sub");
    }

    [Test]
    public async Task X21_a_Given_left_running_then_a_When()
    {
        var background = GivenAsync("x21-background");
        await WhenAsync("x21-when");
        await _calls.Make("x21-after");
        await background;
    }

    [WhenStep]
    public async Task WhenCallThenStartAction(string label)
    {
        await _calls.Make($"{label}-before-explicit");
        TrackingDiagramOverride.StartAction();
        await _calls.Make($"{label}-after-explicit");
    }

    [Test]
    public async Task X22_a_When_step_with_a_call_before_an_explicit_StartAction()
    {
        await GivenAsync("x22-given");
        await WhenCallThenStartAction("x22");
    }

    [Step] public async Task WrapperHoldingWhen(string label) => await WhenAsync($"{label}-wrapped-when");

    [Test]
    public async Task X23_a_step_and_a_call_after_an_async_wrapper_holding_a_When()
    {
        await GivenAsync("x23-given");
        await WrapperHoldingWhen("x23");
        await _calls.Make("x23-after-wrapper");
        await PlainAsync("x23-next-step");
        await _calls.Make("x23-after-next-step");
    }

    [Test]
    public async Task X24_a_skipped_When_then_a_real_Given_and_When()
    {
        await GivenAsync("x24-first-given");
        await WhenSkipped("x24-skipped");
        await GivenAsync("x24-second-given");
        await WhenAsync("x24-when");
    }

    [GivenStep] public Task GivenThrowsBeforeReturning(string label) => throw new InvalidOperationException(label);

    [Test]
    public async Task X25_a_Task_step_that_throws_before_returning_then_a_When()
    {
        await _calls.Make("x25-first");
        try { await GivenThrowsBeforeReturning("x25-throws"); }
        catch (InvalidOperationException) { }
        await WhenAsync("x25-when");
        await ThenAsync("x25-then");
    }

    [WhenStep]
    public async Task WhenWithLaterKeywordFreeSub(string label, TaskCompletionSource gate)
    {
        await gate.Task;
        await PlainAsync($"{label}-sub-step");
    }

    [GivenStep]
    public async Task GivenWithLaterKeywordFreeSub(string label, TaskCompletionSource gate)
    {
        await gate.Task;
        await PlainAsync($"{label}-sub-step");
    }

    [Test]
    public async Task X26_a_When_running_with_a_sub_step_after_a_Given_starts()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var when = WhenWithLaterKeywordFreeSub("x26", gate);
        await GivenAsync("x26-given");
        gate.SetResult();
        await when;
    }

    [Test]
    public async Task X27_a_Given_running_with_a_sub_step_after_a_When_starts()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var given = GivenWithLaterKeywordFreeSub("x27", gate);
        var when = WhenAsync("x27-when");
        gate.SetResult();
        await Task.WhenAll(given, when);
    }

    private async Task ScopedStartSetup(string label)
    {
        TrackingDiagramOverride.StartSetup();
        await _calls.Make($"{label}-in-helper");
    }

    [WhenStep]
    public async Task WhenWithScopedStartSetup(string label)
    {
        await ScopedStartSetup(label);
        await PlainAsync($"{label}-step-after");
    }

    [Test]
    public async Task X28_a_StartSetup_scoped_to_a_helper_in_a_When_then_a_step()
    {
        await GivenAsync("x28-given");
        await WhenWithScopedStartSetup("x28");
    }

    [Step]
    public async Task PlainWithStartSetup(string label)
    {
        TrackingDiagramOverride.StartSetup();
        await _calls.Make(label);
    }

    [Test]
    public async Task X29_a_StartSetup_in_a_first_keyword_free_step_then_another()
    {
        await PlainWithStartSetup("x29-first");
        await PlainAsync("x29-next");
    }

    [WhenStep]
    public async Task WhenWithSkippedSub(string label)
    {
        await WhenSkipped($"{label}-skipped");
        await GivenAsync($"{label}-real-sub");
        await _calls.Make($"{label}-after");
    }

    [Test]
    public async Task X30_a_skipped_sub_step_then_a_real_one_in_a_When()
    {
        await GivenAsync("x30-given");
        await WhenWithSkippedSub("x30");
        await ThenAsync("x30-then");
    }

    [Step] public async Task WrapperOfSkipped(string label) => await WhenSkipped($"{label}-skipped");

    [GivenStep]
    public async Task GivenHoldingWrapperOfSkipped(string label)
    {
        await WrapperOfSkipped(label);
        await _calls.Make($"{label}-after-wrapper");
    }

    [Test]
    public async Task X31_a_skipped_step_in_a_wrapper_in_a_Given_then_a_When()
    {
        await GivenHoldingWrapperOfSkipped("x31");
        await WhenAsync("x31-when");
    }

    [Test]
    public async Task X15_a_skipped_When_step()
    {
        await GivenAsync("x15-given");
        await WhenSkipped("x15-skipped");
        await _calls.Make("x15-after-skipped");
    }

    [Test]
    public async Task X16_a_Task_of_T_step_and_a_non_async_Task_step()
    {
        await GivenTaskNoAsync("x16-given");
        await _calls.Make("x16-after-given");
        await WhenTaskOfT("x16-when");
        await _calls.Make("x16-after-when");
    }

    [GivenStep]
    public async Task GivenWithStartSetupAfterNestedWhen(string label)
    {
        await WhenAsync($"{label}-in-sub");
        TrackingDiagramOverride.StartSetup();
        await _calls.Make($"{label}-after-explicit-startsetup");
    }

    [Test]
    public async Task X17_explicit_StartSetup_after_a_nested_When()
    {
        await GivenWithStartSetupAfterNestedWhen("x17");
        await _calls.Make("x17-after-step");
    }

    [Test]
    public async Task X19_explicit_StartSetup_then_a_When_step()
    {
        TrackingDiagramOverride.StartSetup();
        await _calls.Make("x19-after-startsetup");
        await WhenAsync("x19-when");
        await _calls.Make("x19-after-when");
    }

    [Test]
    public async Task X20_WhenTriggersAction_false()
    {
        if (Environment.GetEnvironmentVariable("PHASE_PROBE_PARALLEL") is not null)
            return; // It sets a process-wide option, which would reach the tests running beside it.
        var before = StepCollector.Options;
        StepCollector.Options = new StepTrackingOptions { WhenTriggersAction = false };
        try
        {
            await GivenAsync("x20-given");
            await _calls.Make("x20-after-given");
            await WhenAsync("x20-when");
            await _calls.Make("x20-after-when");
        }
        finally
        {
            StepCollector.Options = before;
        }
    }

    [Test]
    public async Task X18_a_failed_top_level_Given_then_a_call()
    {
        try { await GivenThrows("x18-given"); } catch (InvalidOperationException) { }
        await _calls.Make("x18-after-failed-given");
        await WhenAsync("x18-when");
    }

    // ---- a step on a flow with no chain ----

    /// <summary>Runs work on a thread of its own under a TestIdentityScope set by hand. UnsafeStart flows no execution context, so the thread has no step chain and no ambient phase; Start flows both.</summary>
    private void OnThread(bool flowContext, bool withScope, Action work)
    {
        var (name, id) = ProbeEnv.Fetcher();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (withScope)
                    using (TestIdentityScope.Begin(name, id))
                        work();
                else
                    work();
            }
            catch (Exception e) { failure = e; }
        });
        if (flowContext) thread.Start(); else thread.UnsafeStart();
        thread.Join();
        if (failure is not null) throw failure;
    }

    [GivenStep]
    public async Task GivenWithWhenOnThread(string label, bool flowContext)
    {
        await _calls.Make($"{label}-before-thread");
        OnThread(flowContext, withScope: true, () =>
        {
            WhenSync($"{label}-when-on-thread");
            _calls.Make($"{label}-after-when-on-thread").GetAwaiter().GetResult();
        });
        await _calls.Make($"{label}-after-thread");
    }

    [GivenStep]
    public async Task GivenWithAsyncWhenOnThread(string label)
    {
        await _calls.Make($"{label}-before-thread");
        OnThread(flowContext: false, withScope: true, () => WhenAsync($"{label}-when-on-thread").GetAwaiter().GetResult());
        await _calls.Make($"{label}-after-thread");
    }

    [WhenStep]
    public async Task WhenWithGivenOnThread(string label)
    {
        await _calls.Make($"{label}-before-thread");
        OnThread(flowContext: false, withScope: true, () =>
        {
            GivenSync($"{label}-given-on-thread");
            _calls.Make($"{label}-after-given-on-thread").GetAwaiter().GetResult();
        });
        await _calls.Make($"{label}-after-thread");
    }

    [Test]
    public async Task T01_a_When_on_an_unflowed_thread_inside_a_Given()
    {
        await GivenWithWhenOnThread("t01", flowContext: false);
        await _calls.Make("t01-after-given");
        await WhenAsync("t01-when");
        await _calls.Make("t01-after-when");
    }

    [Test]
    public async Task T02_a_When_on_a_flowed_thread_inside_a_Given()
    {
        await GivenWithWhenOnThread("t02", flowContext: true);
        await _calls.Make("t02-after-given");
        await WhenAsync("t02-when");
        await _calls.Make("t02-after-when");
    }

    [Test]
    public async Task T03_a_When_on_an_unflowed_thread_while_a_Given_is_left_running()
    {
        var gate = new TaskCompletionSource();
        var open = GivenOpen("t03-open-given", gate);
        OnThread(flowContext: false, withScope: true, () =>
        {
            WhenSync("t03-when-on-thread");
            _calls.Make("t03-after-when-on-thread").GetAwaiter().GetResult();
        });
        await _calls.Make("t03-while-open");
        gate.SetResult();
        await open;
        await _calls.Make("t03-after-closed");
        await WhenAsync("t03-when");
        await _calls.Make("t03-after-when");
    }

    [Test]
    public async Task T04_an_async_When_on_an_unflowed_thread_inside_a_Given()
    {
        await GivenWithAsyncWhenOnThread("t04");
        await _calls.Make("t04-after-given");
        await WhenAsync("t04-when");
        await _calls.Make("t04-after-when");
    }

    [Test]
    public async Task T05_a_Given_on_an_unflowed_thread_inside_a_When_after_a_Given()
    {
        await GivenAsync("t05-given");
        await WhenWithGivenOnThread("t05");
        await _calls.Make("t05-after-when");
        await ThenAsync("t05-then");
    }

    [Test]
    public async Task T06_a_Given_on_an_unflowed_thread_inside_a_first_When_then_a_Then()
    {
        await WhenWithGivenOnThread("t06");
        await _calls.Make("t06-after-when");
        await ThenAsync("t06-then");
        await _calls.Make("t06-after-then");
    }

    [Test]
    public async Task T07_a_When_on_an_unflowed_thread_with_no_identity_inside_a_Given()
    {
        await GivenWithNoIdentityWhenOnThread("t07");
        await _calls.Make("t07-after-given");
    }

    [GivenStep]
    public async Task GivenWithNoIdentityWhenOnThread(string label)
    {
        await _calls.Make($"{label}-before-thread");
        OnThread(flowContext: false, withScope: false, () => WhenSync($"{label}-when-on-thread"));
        await _calls.Make($"{label}-after-thread");
    }

    // ---- Task steps that are not async, in shapes other than X16's and X25's: the patch gives each stub a catch ----

    public bool Never => false;

    private async Task<int> MakeInt(string label)
    {
        await _calls.Make(label);
        return 1;
    }

    private async Task<T> MakeGeneric<T>(string label, T value)
    {
        await _calls.Make(label);
        return value;
    }

    [GivenStep] public Task<int> GivenTaskOfTNoAsync(string label) => MakeInt(label);

    [GivenStep]
    public Task GivenTaskWithBranches(string label, bool first)
    {
        if (first)
            return _calls.Make($"{label}-first");
        return _calls.Make($"{label}-second");
    }

    [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public Task GivenTaskSkipped(string label) => _calls.Make(label);
    [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public Task<int> GivenTaskOfTSkipped(string label) => MakeInt(label);
    [WhenStep(SkipIf = nameof(Never), SkipReason = "skipped")] public Task WhenTaskNotSkipped(string label) => _calls.Make(label);
    [GivenStep] public Task<T> GivenTaskGeneric<T>(string label, T value) => MakeGeneric(label, value);

    [WhenStep]
    public Task WhenTaskInTryFinally(string label)
    {
        try
        {
            return _calls.Make(label);
        }
        finally
        {
            _ = label.Length;
        }
    }

    [WhenStep]
    public Task WhenTaskWithUsing(string label)
    {
        using var gate = new SemaphoreSlim(1);
        return _calls.Make(label);
    }

    [GivenStep] public Task<int> GivenTaskOfTThrows(string label) => throw new InvalidOperationException(label);
    [GivenStep] public Task GivenTaskReturnsFaulted(string label) => Task.FromException(new InvalidOperationException(label));

    [WhenStep]
    public Task WhenTaskCatchesInside(string label)
    {
        try
        {
            throw new InvalidOperationException(label);
        }
        catch (InvalidOperationException)
        {
            return _calls.Make(label);
        }
    }

    [Test]
    public async Task T10_Task_steps_that_are_not_async_in_their_other_shapes()
    {
        await GivenTaskOfTNoAsync("t10-of-t");
        await GivenTaskWithBranches("t10-branch", first: false);
        await GivenTaskSkipped("t10-skipped");
        await GivenTaskOfTSkipped("t10-of-t-skipped");
        await _calls.Make("t10-after-skipped");
        await GivenTaskGeneric("t10-generic", 1);
        await WhenTaskNotSkipped("t10-not-skipped");
        await WhenTaskInTryFinally("t10-try-finally");
        await WhenTaskWithUsing("t10-using");
        await WhenTaskCatchesInside("t10-catches-inside");
        await _calls.Make("t10-after");
    }

    [Test]
    public async Task T11_a_Task_of_T_step_that_throws_before_returning_then_a_When()
    {
        try { await GivenTaskOfTThrows("t11-throws"); }
        catch (InvalidOperationException) { }
        await _calls.Make("t11-after-throw");
        await WhenAsync("t11-when");
    }

    [Test]
    public async Task T12_a_Task_step_returning_a_faulted_task_then_a_When()
    {
        try { await GivenTaskReturnsFaulted("t12-faulted"); }
        catch (InvalidOperationException) { }
        await _calls.Make("t12-after-fault");
        await WhenAsync("t12-when");
    }

    [Test]
    public void T13_a_Task_step_that_throws_before_returning_never_awaited()
    {
        Assert_throws(() => GivenThrowsBeforeReturning("t13-throws"));
        WhenSync("t13-when");
    }

    private static void Assert_throws(Func<Task> action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("the step did not throw");
    }

    [GivenStep]
    public async Task GivenWithNestedThrowingTask(string label)
    {
        try { await GivenThrowsBeforeReturning($"{label}-nested-throws"); }
        catch (InvalidOperationException) { }
        await _calls.Make($"{label}-after-nested-throw");
        await WhenAsync($"{label}-nested-when");
    }

    [Test]
    public async Task T14_a_nested_Task_step_that_throws_before_returning_in_a_Given()
    {
        await GivenWithNestedThrowingTask("t14");
        await WhenAsync("t14-when");
    }

}
