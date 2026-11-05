using System.Reflection;
using Kronikol.Reports;
using Kronikol.StepTracking;
using Kronikol.Tracking;
using Microsoft.CodeAnalysis;

namespace Kronikol.Tests.StepTracking;

/// <summary>
/// #224 on real woven IL (plans/STEP_PHASE_PLAN.md 6). How far a step's write goes is a property of where the weaver puts the
/// call, so these are the facts the unit level cannot be. Each shape records what its flow holds at each labelled point, which is
/// what a plain call there reads, and every async body really yields. The names in the comments are the harness's tests.
/// </summary>
public class StepPhaseWeavingTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Kronikol.Tracking;

        [assembly: TrackSteps]

        public class Shapes
        {
            public readonly List<string> Reads = new();
            public string Id;
            private Task _left;

            private void At(string label) { lock (Reads) Reads.Add(label + "=" + TestPhaseContext.Current); }

            [GivenStep] public void SyncGiven() => At("in-given");
            [WhenStep] public void SyncWhen() => At("in-when");
            [ThenStep] public void SyncThen() => At("in-then");
            [GivenStep] public async Task AsyncGiven() { await Task.Yield(); At("in-given"); }
            [WhenStep] public async Task AsyncWhen() { await Task.Yield(); At("in-when"); }
            [ThenStep] public async Task AsyncThen() { await Task.Yield(); At("in-then"); }

            // ---- one keyword under another, each way of sync and async ----
            [WhenStep] public void WhenOverSyncGiven() { At("when-start"); SyncGiven(); At("when-after-given"); }
            public void SyncInSync() { WhenOverSyncGiven(); At("after-when"); }

            [WhenStep] public async Task WhenOverAsyncGiven() { await Task.Yield(); At("when-start"); await AsyncGiven(); At("when-after-given"); }
            public async Task AsyncInAsync() { await WhenOverAsyncGiven(); At("after-when"); }

            [WhenStep] public async Task AsyncWhenOverSyncGiven() { await Task.Yield(); At("when-start"); SyncGiven(); At("when-after-given"); await Task.Yield(); At("when-end"); }
            public async Task SyncInAsync() { await AsyncWhenOverSyncGiven(); At("after-when"); }

            [WhenStep] public void SyncWhenOverAsyncGiven() { At("when-start"); AsyncGiven().GetAwaiter().GetResult(); At("when-after-given"); }
            public void AsyncInSync() { SyncWhenOverAsyncGiven(); At("after-when"); }

            [GivenStep] public async Task GivenOverAsyncWhen() { await AsyncWhen(); At("given-after-when"); }
            public async Task WhenInGiven() { await GivenOverAsyncWhen(); At("after-given"); }

            [GivenStep] public async Task GivenThrows() { await Task.Yield(); throw new InvalidOperationException("caught"); }
            [WhenStep] public async Task WhenCatchesNestedGiven() { try { await GivenThrows(); } catch (InvalidOperationException) { } At("when-after-sub"); }
            public async Task CaughtFailureOfANestedGiven() { await WhenCatchesNestedGiven(); At("after-when"); }

            // ---- a step with no keyword says nothing ----
            [Step] public void SyncWrapper() { SyncGiven(); At("wrapper-after-given"); SyncWhen(); At("wrapper-after-when"); }
            public void WrapperSync() { SyncWrapper(); At("after"); }

            [Step] public async Task AsyncWrapper() { await AsyncGiven(); At("wrapper-after-given"); await AsyncWhen(); At("wrapper-after-when"); }
            public async Task WrapperAsync() { await AsyncWrapper(); At("after"); }

            [Step] public async Task WrapperOverWhen() { await AsyncWhen(); At("wrapper-after-when"); }
            [GivenStep] public async Task GivenOverWrapperOverWhen() { await WrapperOverWhen(); At("given-end"); }
            public async Task WhenUnderWrapperUnderGiven() { await GivenOverWrapperOverWhen(); At("after-given"); }

            // ---- a step left running is above nothing its caller starts next ----
            [GivenStep] public async Task GivenWaiting(Task gate) { At("given-start"); await gate; At("given-after-gate"); }
            public async Task GivenLeftRunningThenAWhen()
            {
                var gate = new TaskCompletionSource();
                var open = GivenWaiting(gate.Task);
                At("while-open");
                await AsyncWhen();
                At("after-when");
                gate.SetResult();
                await open;
                At("after-closed");
            }

            [GivenStep] public async Task GivenUntil(Task gate) { await gate; At("in-given"); }
            [WhenStep] public async Task WhenUntil(Task gate) { await gate; At("in-when"); }
            public async Task WhenAllOfAGivenAndAWhen()
            {
                var gate = new TaskCompletionSource();
                var both = Task.WhenAll(GivenUntil(gate.Task), WhenUntil(gate.Task));
                gate.SetResult();
                await both;
                At("after");
            }

            public async Task GivenAwaitedAfterTheWhen()
            {
                var background = AsyncGiven();
                await AsyncWhen();
                At("after");
                await background;
            }

            [GivenStep] public async Task<int> GivenOfIntWaiting(Task gate) { await gate; At("in-given"); return 1; }
            public async Task TaskOfTLeftRunningThenAWhen()
            {
                var gate = new TaskCompletionSource();
                var open = GivenOfIntWaiting(gate.Task);
                await AsyncWhen();
                At("after-when");
                gate.SetResult();
                await open;
            }

            [Step] public async Task WorkerStep(Task gate) { await gate; At("in-worker"); }
            [GivenStep] public void GivenLeavingAWorker(Task gate) { _left = WorkerStep(gate); }
            public async Task SyncGivenLeavingAWorkerThenAWhen()
            {
                var gate = new TaskCompletionSource();
                GivenLeavingAWorker(gate.Task);
                At("after-given");
                await AsyncWhen();
                At("after-when");
                gate.SetResult();
                await _left;
            }

            [GivenStep] public void GivenForking(Task gate) { _left = Task.Run(async () => { await gate; SyncThen(); At("fork-after-then"); }); }
            public async Task KeywordOnAFlowForkedInAClosedGiven()
            {
                var gate = new TaskCompletionSource();
                GivenForking(gate.Task);
                At("after-given");
                gate.SetResult();
                await _left;
                At("after-fork");
            }

            // ---- a skipped step closes itself alone ----
            public bool Always => true;
            [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public async Task GivenSkipped() { await Task.Yield(); At("never"); }
            [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public void SyncGivenSkipped() => At("never");
            [WhenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public void SyncWhenSkipped() => At("never");

            [WhenStep] public async Task WhenOverSkippedThenReal() { await GivenSkipped(); At("when-after-skip"); await AsyncGiven(); At("when-after-given"); }
            public async Task SkippedSubStepThenARealOne() { await WhenOverSkippedThenReal(); await AsyncThen(); }

            [WhenStep] public async Task AsyncWhenOverSyncSkipped() { await Task.Yield(); SyncGivenSkipped(); At("when-after-skip"); await AsyncGiven(); }
            public async Task SyncSkippedInAnAsyncParent() { await AsyncWhenOverSyncSkipped(); await AsyncThen(); }

            [WhenStep] public void SyncParentLastSkipped() => SyncGivenSkipped();
            public async Task SyncParentWhoseLastActIsASkip() { SyncParentLastSkipped(); await AsyncThen(); }

            [WhenStep] public Task TaskParentLastSkipped() => GivenSkipped();
            public async Task TaskParentWhoseLastActIsASkip() { await TaskParentLastSkipped(); await AsyncThen(); }

            [GivenStep] public void SyncGivenSkipThenMore() { SyncWhenSkipped(); SyncThen(); At("given-after"); }
            public async Task SkipThenMoreInASyncGiven() { SyncGivenSkipThenMore(); await AsyncWhen(); }

            [GivenStep] public async Task AsyncGivenSkipThenMore() { SyncWhenSkipped(); SyncThen(); await Task.Yield(); At("given-after"); }
            public async Task SkipThenMoreInAnAsyncGiven() { await AsyncGivenSkipThenMore(); await AsyncWhen(); }

            // ---- a step bypassed by hand under a woven parent leaves the parent to close ----
            [GivenStep] public void GivenHandBypassById() { StepCollector.StartStep(Id, null, "by hand", null, null); StepCollector.BypassStep(Id, "by hand"); }
            public async Task HandBypassById() { GivenHandBypassById(); await AsyncWhen(); await AsyncThen(); }

            [GivenStep] public void GivenHandBypassAmbient() { StepCollector.StartStep(null, "by hand", null, null); StepCollector.BypassStep("by hand"); }
            public async Task HandBypassAmbient() { GivenHandBypassAmbient(); await AsyncWhen(); await AsyncThen(); }

            [GivenStep] public Task TaskGivenHandBypassAmbient() { StepCollector.StartStep(null, "by hand", null, null); StepCollector.BypassStep("by hand"); return Task.CompletedTask; }
            public async Task HandBypassAmbientInATaskParent() { await TaskGivenHandBypassAmbient(); await AsyncWhen(); await AsyncThen(); }

            [WhenStep] public void WhenHandBypassAmbient() { StepCollector.StartStep(null, "by hand", null, null); StepCollector.BypassStep("by hand"); }
            public async Task HandBypassInAWhenThenARealGiven() { await AsyncGiven(); WhenHandBypassAmbient(); await AsyncThen(); }

            // ---- a SkipIf member's read belongs to its step ----
            public bool Throws => throw new InvalidOperationException("skip getter");
            [GivenStep(SkipIf = nameof(Throws))] public async Task GivenSkipThrows() { await Task.Yield(); At("never"); }
            public async Task AGetterThatThrows() { try { await GivenSkipThrows(); } catch (InvalidOperationException) { At("caught"); } await AsyncWhen(); await AsyncThen(); }

            public bool NotInAction => TestPhaseContext.Current != TestPhase.Action;
            [WhenStep(SkipIf = nameof(NotInAction))] public async Task WhenIfInAction() { await Task.Yield(); At("act-ran"); }
            public async Task AGetterThatReadsThePhase() { await AsyncGiven(); await WhenIfInAction(); At("after"); }

            public bool ReadsAndSaysNo { get { At("in-getter"); return false; } }
            [WhenStep(SkipIf = nameof(ReadsAndSaysNo))] public async Task WhenWithAReadingGetter() { await Task.Yield(); At("in-when"); }
            public async Task AGettersReadIsInItsStep() { await AsyncGiven(); await WhenWithAReadingGetter(); }

            public bool RunsAThen { get { SyncThen(); return false; } }
            [GivenStep(SkipIf = nameof(RunsAThen))] public async Task GivenGetterRunsAThen() { await Task.Yield(); At("in-given"); }
            public async Task AStepAGetterRunsIsASubStep() { await GivenGetterRunsAThen(); await AsyncWhen(); }

            // ---- a Task step that throws before returning ----
            private async Task Nothing() => await Task.Yield();
            [GivenStep] public Task GivenGuard(bool fail) { if (fail) throw new ArgumentException("guard"); return Nothing(); }
            public async Task ThrowsBeforeReturningWithAReturnInItsBody() { try { await GivenGuard(true); } catch (ArgumentException) { At("caught"); } await AsyncWhen(); await AsyncThen(); }

            [GivenStep] public Task GivenOnlyThrows() => throw new InvalidOperationException("no return");
            public async Task ThrowsBeforeReturningWithNoReturn() { try { await GivenOnlyThrows(); } catch (InvalidOperationException) { At("caught"); } await AsyncWhen(); await AsyncThen(); }

            [GivenStep] public Task GivenEndingInAHandler() { try { throw new InvalidOperationException("inner"); } catch (InvalidOperationException) { throw; } }
            public async Task ThrowsBeforeReturningEndingInAHandler() { try { await GivenEndingInAHandler(); } catch (InvalidOperationException) { At("caught"); } await AsyncWhen(); await AsyncThen(); }

            [GivenStep] public void SyncAllPathsThrow() { try { throw new InvalidOperationException("inner"); } catch (InvalidOperationException e) { throw new ArgumentException("outer", e); } }
            public async Task ASyncStepEndingInAHandler() { try { SyncAllPathsThrow(); } catch (ArgumentException) { At("caught"); } await AsyncWhen(); await AsyncThen(); }
        }
        """;

    private static Assembly WeaveShapes(string name, OptimizationLevel optimization)
    {
        var path = TestAssemblyBuilder.Build(name, Source, optimization);
        new StepWeaver().Weave(path, Path.ChangeExtension(path, ".pdb"));
        return Assembly.LoadFrom(path);
    }

    private static readonly Lazy<Assembly> Debug = new(() => WeaveShapes("StepPhaseShapesDebug", OptimizationLevel.Debug));
    private static readonly Lazy<Assembly> Release = new(() => WeaveShapes("StepPhaseShapesRelease", OptimizationLevel.Release));

    private static string Printed(IEnumerable<ScenarioStep> steps) => string.Join(" ", steps.Select(step =>
    {
        var status = step.Status switch { ExecutionResult.Bypassed => "!", ExecutionResult.Failed => "?", _ => "" };
        var subSteps = step.SubSteps is { Length: > 0 } nested ? $"[{Printed(nested)}]" : "";
        return $"{step.Keyword ?? "Step"}{status}{subSteps}";
    }));

    /// <summary>Runs a shape as its own test: what its flows held, in label order, and the step tree it left.</summary>
    private static async Task<(string Reads, string Tree)> Run(string shape, Assembly? assembly = null)
    {
        var id = $"woven-{shape}-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(id, id);
        TestPhaseContext.Reset();

        var type = (assembly ?? Debug.Value).GetType("Shapes")!;
        var shapes = Activator.CreateInstance(type)!;
        type.GetField("Id")!.SetValue(shapes, id);

        if (type.GetMethod(shape)!.Invoke(shapes, null) is Task task)
            await task;

        var reads = (List<string>)type.GetField("Reads")!.GetValue(shapes)!;
        return (string.Join(" ", reads.Order(StringComparer.Ordinal)), Printed(StepCollector.GetSteps(id)));
    }

    private static string Sorted(string reads) => string.Join(" ", reads.Split(' ').Order(StringComparer.Ordinal));

    private static async Task Reads(string shape, string expectedReads, string? expectedTree = null)
    {
        var (reads, tree) = await Run(shape);
        Assert.Equal(Sorted(expectedReads), reads);
        if (expectedTree is not null)
            Assert.Equal(expectedTree, tree);
    }

    // ---- One keyword under another (X01, X02, X03) ----

    [Fact]
    public Task A_sync_Given_in_a_sync_When_is_a_label_and_stays_one_after_the_When_ended() =>
        Reads("SyncInSync", "when-start=Action in-given=Action when-after-given=Action after-when=Action", "When[Given]");

    [Fact]
    public Task An_async_Given_in_an_async_When_is_a_label() =>
        Reads("AsyncInAsync", "when-start=Action in-given=Action when-after-given=Action after-when=Action", "When[Given]");

    [Fact]
    public Task A_sync_Given_in_an_async_When_is_a_label() =>
        Reads("SyncInAsync", "when-start=Action in-given=Action when-after-given=Action when-end=Action after-when=Action", "When[Given]");

    [Fact]
    public Task An_async_Given_in_a_sync_When_is_a_label() =>
        Reads("AsyncInSync", "when-start=Action in-given=Action when-after-given=Action after-when=Action", "When[Given]");

    [Fact]
    public Task A_When_in_a_Given_is_a_label() =>
        Reads("WhenInGiven", "in-when=Setup given-after-when=Setup after-given=Setup", "Given[When]");

    [Fact]
    public Task A_caught_failure_of_a_nested_Given_leaves_the_When_alone() =>
        Reads("CaughtFailureOfANestedGiven", "when-after-sub=Action after-when=Action", "When[Given?]");

    // ---- A step with no keyword says nothing (X04, X05) ----

    [Fact]
    public Task A_keyword_free_sync_wrapper_leaves_the_decision_to_the_keywords_under_it() =>
        Reads("WrapperSync", "in-given=Setup wrapper-after-given=Setup in-when=Action wrapper-after-when=Action after=Action", "Step[Given When]");

    // The write of the When reaches the wrapper's own frame and no further (plans/STEP_PHASE_PLAN.md 11).
    [Fact]
    public Task A_keyword_free_async_wrapper_leaves_the_decision_to_the_keywords_under_it() =>
        Reads("WrapperAsync", "in-given=Setup wrapper-after-given=Setup in-when=Action wrapper-after-when=Action after=Unknown", "Step[Given When]");

    [Fact]
    public Task A_When_under_a_wrapper_under_a_Given_is_a_label() =>
        Reads("WhenUnderWrapperUnderGiven", "in-when=Setup wrapper-after-when=Setup given-end=Setup after-given=Setup", "Given[Step[When]]");

    // ---- A step left running is above nothing its caller starts next (X09, X10, X21, review/B01) ----

    [Fact]
    public Task A_Given_left_running_is_not_above_a_When() =>
        Reads("GivenLeftRunningThenAWhen", "given-start=Setup while-open=Setup in-when=Action after-when=Action given-after-gate=Setup after-closed=Action");

    [Fact]
    public Task Task_WhenAll_of_a_Given_and_a_When_reads_each_as_its_own() =>
        Reads("WhenAllOfAGivenAndAWhen", "in-given=Setup in-when=Action after=Action");

    [Fact]
    public Task A_Given_awaited_after_the_When_is_not_above_it() =>
        Reads("GivenAwaitedAfterTheWhen", "in-given=Setup in-when=Action after=Action");

    [Fact]
    public Task A_Task_of_T_step_left_running_is_not_above_a_When() =>
        Reads("TaskOfTLeftRunningThenAWhen", "in-given=Setup in-when=Action after-when=Action");

    [Fact]
    public Task A_sync_Given_that_leaves_a_worker_step_running_is_not_above_a_When() =>
        Reads("SyncGivenLeavingAWorkerThenAWhen", "after-given=Setup in-when=Action after-when=Action in-worker=Setup");

    [Fact]
    public Task A_keyword_on_a_flow_forked_in_a_closed_Given_reads_as_its_own_step_there() =>
        Reads("KeywordOnAFlowForkedInAClosedGiven", "after-given=Setup in-then=Action fork-after-then=Action after-fork=Setup");

    // ---- A skipped step closes itself alone (X30, X36, X37, mut/X90, X91) ----

    [Fact]
    public Task A_skipped_sub_step_then_a_real_one_stay_under_their_When() =>
        Reads("SkippedSubStepThenARealOne", "when-after-skip=Action in-given=Action when-after-given=Action in-then=Action", "When[Given! Given] Then");

    [Fact]
    public Task A_sync_skipped_sub_step_in_an_async_parent_stays_under_it() =>
        Reads("SyncSkippedInAnAsyncParent", "when-after-skip=Action in-given=Action in-then=Action", "When[Given! Given] Then");

    [Fact]
    public Task A_sync_parent_whose_last_act_is_a_skip_closes_itself() =>
        Reads("SyncParentWhoseLastActIsASkip", "in-then=Action", "When[Given!] Then");

    [Fact]
    public Task A_Task_parent_whose_last_act_is_a_skip_closes_itself() =>
        Reads("TaskParentWhoseLastActIsASkip", "in-then=Action", "When[Given!] Then");

    [Fact]
    public Task A_skip_then_more_of_a_sync_Given_stays_under_it() =>
        Reads("SkipThenMoreInASyncGiven", "in-then=Setup given-after=Setup in-when=Action", "Given[When! Then] When");

    [Fact]
    public Task A_skip_then_more_of_an_async_Given_stays_under_it() =>
        Reads("SkipThenMoreInAnAsyncGiven", "in-then=Setup given-after=Setup in-when=Action", "Given[When! Then] When");

    // ---- A step bypassed by hand under a woven parent leaves the parent to close (review/A01, review5/H01, H02, H13) ----

    [Fact]
    public Task A_step_bypassed_by_hand_by_id_leaves_its_parent_to_close() =>
        Reads("HandBypassById", "in-when=Action in-then=Action", "Given[Step!] When Then");

    [Fact]
    public Task A_step_bypassed_by_hand_through_the_ambient_overloads_leaves_its_parent_to_close() =>
        Reads("HandBypassAmbient", "in-when=Action in-then=Action", "Given[Step!] When Then");

    [Fact]
    public Task A_step_bypassed_by_hand_in_a_Task_parent_leaves_the_parent_to_close() =>
        Reads("HandBypassAmbientInATaskParent", "in-when=Action in-then=Action", "Given[Step!] When Then");

    [Fact]
    public Task A_step_bypassed_by_hand_in_a_When_leaves_the_When_to_close() =>
        Reads("HandBypassInAWhenThenARealGiven", "in-given=Setup in-then=Action", "Given When[Step!] Then");

    // ---- A SkipIf member's read belongs to its step (review5/H05, review6/S01 to S11) ----

    [Fact]
    public Task A_SkipIf_getter_that_throws_fails_its_step_and_leaves_none_open() =>
        Reads("AGetterThatThrows", "caught=Setup in-when=Action in-then=Action", "Given? When Then");

    [Fact]
    public Task A_getter_reading_the_phase_sees_its_steps() =>
        Reads("AGetterThatReadsThePhase", "in-given=Setup act-ran=Action after=Action", "Given When");

    [Fact]
    public Task A_getters_read_is_in_its_steps_phase() =>
        Reads("AGettersReadIsInItsStep", "in-given=Setup in-getter=Action in-when=Action", "Given When");

    [Fact]
    public Task A_step_a_getter_runs_is_a_sub_step_and_a_label() =>
        Reads("AStepAGetterRunsIsASubStep", "in-then=Setup in-given=Setup in-when=Action", "Given[Then] When");

    // ---- A Task step that throws before returning, and a method ending in a handler (X25, X32, X33, review4/S02) ----

    [Fact]
    public Task A_Task_step_that_throws_before_returning_closes_as_failed_with_a_return_in_its_body() =>
        Reads("ThrowsBeforeReturningWithAReturnInItsBody", "caught=Setup in-when=Action in-then=Action", "Given? When Then");

    [Fact]
    public Task A_Task_step_that_throws_before_returning_closes_as_failed_with_no_return() =>
        Reads("ThrowsBeforeReturningWithNoReturn", "caught=Setup in-when=Action in-then=Action", "Given? When Then");

    [Fact]
    public Task A_Task_step_ending_in_a_handler_closes_as_failed() =>
        Reads("ThrowsBeforeReturningEndingInAHandler", "caught=Setup in-when=Action in-then=Action", "Given? When Then");

    [Fact]
    public Task A_sync_step_ending_in_a_handler_closes_as_failed() =>
        Reads("ASyncStepEndingInAHandler", "caught=Setup in-when=Action in-then=Action", "Given? When Then");

    [Theory]
    [InlineData("SyncInSync", "when-start=Action in-given=Action when-after-given=Action after-when=Action", "When[Given]")]
    [InlineData("AsyncInAsync", "when-start=Action in-given=Action when-after-given=Action after-when=Action", "When[Given]")]
    [InlineData("GivenLeftRunningThenAWhen", "given-start=Setup while-open=Setup in-when=Action after-when=Action given-after-gate=Setup after-closed=Action", null)]
    [InlineData("ASyncStepEndingInAHandler", "caught=Setup in-when=Action in-then=Action", "Given? When Then")]
    [InlineData("ThrowsBeforeReturningEndingInAHandler", "caught=Setup in-when=Action in-then=Action", "Given? When Then")]
    [InlineData("SkippedSubStepThenARealOne", "when-after-skip=Action in-given=Action when-after-given=Action in-then=Action", "When[Given! Given] Then")]
    public async Task The_same_shapes_read_the_same_in_Release(string shape, string expectedReads, string? expectedTree)
    {
        var (reads, tree) = await Run(shape, Release.Value);

        Assert.Equal(Sorted(expectedReads), reads);
        if (expectedTree is not null)
            Assert.Equal(expectedTree, tree);
    }
}
