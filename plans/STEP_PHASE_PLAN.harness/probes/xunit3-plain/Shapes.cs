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
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Dump.Write("xunit3-plain");
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime,
            new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public class Shapes : DiagrammedComponentTest
{
    private readonly Calls _calls = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

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
    [Fact]
    public async Task X01_When_with_nested_Given_async()
    {
        await GivenAsync("x01-given");
        await WhenWithGivenSub("x01");
        await _calls.Make("x01-after-when-step");
        await ThenAsync("x01-then");
    }

    [Fact]
    public async Task X02_Given_with_nested_When_async()
    {
        await GivenWithWhenSub("x02");
        await _calls.Make("x02-after-given-step");
        await WhenAsync("x02-when");
    }

    [Fact]
    public void X03_When_with_nested_Given_sync()
    {
        GivenSync("x03-given");
        WhenSyncWithGivenSyncSub("x03");
        _calls.Make("x03-after-when-step").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task X04_keyword_free_wrapper_async()
    {
        await WrapperAsync("x04");
        await _calls.Make("x04-after-wrapper");
    }

    [Fact]
    public void X05_keyword_free_wrapper_sync()
    {
        WrapperSync("x05");
        _calls.Make("x05-after-wrapper").GetAwaiter().GetResult();
    }

    [Fact]
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

    [Fact]
    public async Task X07_explicit_StartAction_inside_a_Given_step()
    {
        await GivenStartsActionExplicitly("x07");
        await _calls.Make("x07-after-step");
    }

    [Fact]
    public async Task X08_explicit_StartAction_and_StartSetup_in_the_test()
    {
        await _calls.Make("x08-first");
        TrackingDiagramOverride.StartSetup();
        await _calls.Make("x08-after-startsetup");
        TrackingDiagramOverride.StartAction();
        await _calls.Make("x08-after-startaction");
    }

    [Fact]
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

    [Fact]
    public async Task X10_WhenAll_of_a_Given_and_a_When()
    {
        await Task.WhenAll(GivenAsync("x10-given"), WhenAsync("x10-when"));
        await _calls.Make("x10-after");
    }

    [Fact]
    public async Task X11_a_When_step_in_Task_Run()
    {
        await GivenAsync("x11-given");
        await Task.Run(() => WhenAsync("x11-when-in-run"));
        await _calls.Make("x11-after-run");
    }

    [Fact]
    public async Task X12_steps_in_an_ordinary_async_helper()
    {
        await Arrange("x12");
        await _calls.Make("x12-after-helper");
        await WhenAsync("x12-when");
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task X22_a_When_step_with_a_call_before_an_explicit_StartAction()
    {
        await GivenAsync("x22-given");
        await WhenCallThenStartAction("x22");
    }

    [Step] public async Task WrapperHoldingWhen(string label) => await WhenAsync($"{label}-wrapped-when");

    [Fact]
    public async Task X23_a_step_and_a_call_after_an_async_wrapper_holding_a_When()
    {
        await GivenAsync("x23-given");
        await WrapperHoldingWhen("x23");
        await _calls.Make("x23-after-wrapper");
        await PlainAsync("x23-next-step");
        await _calls.Make("x23-after-next-step");
    }

    [Fact]
    public async Task X24_a_skipped_When_then_a_real_Given_and_When()
    {
        await GivenAsync("x24-first-given");
        await WhenSkipped("x24-skipped");
        await GivenAsync("x24-second-given");
        await WhenAsync("x24-when");
    }

    [GivenStep] public Task GivenThrowsBeforeReturning(string label) => throw new InvalidOperationException(label);

    [Fact]
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

    [Fact]
    public async Task X26_a_When_running_with_a_sub_step_after_a_Given_starts()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var when = WhenWithLaterKeywordFreeSub("x26", gate);
        await GivenAsync("x26-given");
        gate.SetResult();
        await when;
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task X31_a_skipped_step_in_a_wrapper_in_a_Given_then_a_When()
    {
        await GivenHoldingWrapperOfSkipped("x31");
        await WhenAsync("x31-when");
    }

    [GivenStep]
    public Task GivenEndingInHandler(string label)
    {
        try { throw new InvalidOperationException(label); }
        catch (InvalidOperationException) { throw; }
    }

    [Fact]
    public async Task X32_a_Task_step_whose_body_ends_in_a_handler()
    {
        await _calls.Make("x32-first");
        try { await GivenEndingInHandler("x32"); }
        catch (Exception e) { await _calls.Make($"x32-caught-{e.GetType().Name}"); }
        await WhenAsync("x32-when");
    }

    [GivenStep]
    public Task GivenGuarded(string label)
    {
        if (label.Length > 0)
            throw new ArgumentException(label);
        return _calls.Make(label);
    }

    [Fact]
    public async Task X33_a_guard_clause_under_ThrowsAsync_then_a_When()
    {
        await GivenAsync("x33-given");
        await Assert.ThrowsAsync<ArgumentException>(() => GivenGuarded("x33-guarded"));
        await _calls.Make("x33-after-guard");
        await WhenAsync("x33-when");
        await _calls.Make("x33-after-when");
    }

    [GivenStep]
    public Task GivenReturningInTry(string label)
    {
        try { return _calls.Make(label); }
        finally { Console.Write(string.Empty); }
    }

    [Fact]
    public async Task X34_a_Task_step_that_returns_inside_a_try()
    {
        await GivenReturningInTry("x34-given");
        await WhenAsync("x34-when");
    }

    [GivenStep]
    public Task<T> GivenGeneric<T>(T value, string label)
    {
        if (value is null)
            throw new ArgumentNullException(label);
        return Task.FromResult(value);
    }

    [Fact]
    public async Task X35_a_generic_Task_of_T_step_that_is_not_async()
    {
        await _calls.Make("x35-first");
        var got = await GivenGeneric(1, "x35");
        await Assert.ThrowsAsync<ArgumentNullException>(() => GivenGeneric<string>(null!, "x35-null"));
        await WhenAsync($"x35-when-{got}");
    }

    [WhenStep(SkipIf = nameof(Always), SkipReason = "skipped")]
    public void SyncWhenSkipped(string label) => Console.Write(label);

    [GivenStep]
    public void SyncGivenEndingInASkippedSub(string label) => SyncWhenSkipped($"{label}-skipped");

    [Fact]
    public async Task X36_a_sync_parent_whose_last_act_is_a_skipped_sub_step()
    {
        SyncGivenEndingInASkippedSub("x36");
        await _calls.Make("x36-after-parent");
        await WhenAsync("x36-when");
    }

    [GivenStep]
    public Task TaskGivenEndingInASkippedSub(string label)
    {
        SyncWhenSkipped($"{label}-skipped");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task X37_a_Task_parent_not_async_whose_last_act_is_a_skipped_sub_step()
    {
        await TaskGivenEndingInASkippedSub("x37");
        await _calls.Make("x37-after-parent");
        await WhenAsync("x37-when");
    }

    [Fact]
    public async Task X15_a_skipped_When_step()
    {
        await GivenAsync("x15-given");
        await WhenSkipped("x15-skipped");
        await _calls.Make("x15-after-skipped");
    }

    [Fact]
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

    [Fact]
    public async Task X17_explicit_StartSetup_after_a_nested_When()
    {
        await GivenWithStartSetupAfterNestedWhen("x17");
        await _calls.Make("x17-after-step");
    }

    [Fact]
    public async Task X19_explicit_StartSetup_then_a_When_step()
    {
        TrackingDiagramOverride.StartSetup();
        await _calls.Make("x19-after-startsetup");
        await WhenAsync("x19-when");
        await _calls.Make("x19-after-when");
    }

    [Fact]
    public async Task X20_WhenTriggersAction_false()
    {
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

    [Fact]
    public async Task X18_a_failed_top_level_Given_then_a_call()
    {
        try { await GivenThrows("x18-given"); } catch (InvalidOperationException) { }
        await _calls.Make("x18-after-failed-given");
        await WhenAsync("x18-when");
    }
}
