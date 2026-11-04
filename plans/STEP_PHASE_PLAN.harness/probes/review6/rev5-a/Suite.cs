using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Rev5;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rev5.A;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write("rev5-a");
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public abstract class Base : DiagrammedComponentTest
{
    protected static HttpClient NewClient(bool trackDuringSetup = true, bool trackDuringAction = true) =>
        new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            TrackDuringSetup = trackDuringSetup,
            TrackDuringAction = trackDuringAction
        }) { InnerHandler = new Answer() });

    protected readonly HttpClient Http = NewClient();
    protected readonly HttpClient NoSetup = NewClient(trackDuringSetup: false);
    protected readonly HttpClient NoAction = NewClient(trackDuringAction: false);

    protected string Id => TestContext.Current.Test!.UniqueID;

    protected async Task Call(string label) => (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    protected async Task Quiet(string label) => (await NoSetup.GetAsync($"http://probe.local/{label}-nosetup")).EnsureSuccessStatusCode();
    protected async Task Loud(string label) => (await NoAction.GetAsync($"http://probe.local/{label}-noaction")).EnsureSuccessStatusCode();

    [GivenStep] public async Task GivenAsync(string label) => await Call(label);
    [GivenStep] public void GivenSync(string label) => Call(label).GetAwaiter().GetResult();
    [GivenStep] public void GivenNoCall(string label) { }
    [WhenStep] public async Task WhenAsync(string label) => await Call(label);
    [WhenStep] public void WhenSync(string label) => Call(label).GetAwaiter().GetResult();
    [WhenStep] public void WhenNoCall(string label) { }
    [ThenStep] public async Task ThenAsync(string label) => await Call(label);
    public async Task AndAsync(string label)
    {
        StepCollector.StartStep("And", label, null, null);
        try { await Call(label); await Quiet(label); await Loud(label); }
        finally { StepCollector.CompleteStep(true); }
    }
    [Step] public async Task PlainAsync(string label) => await Call(label);
}

/// <summary>An And reads the test's phase where its flow holds nothing.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Ands : Base
{
    [Fact]
    public async Task A01_And_after_Reset_on_the_same_flow()
    {
        await GivenAsync("a01-seed");
        TestPhaseContext.Reset();
        await Call("a01-after-reset");
        await AndAsync("a01-and");
        await Call("a01-after-and");
        await Quiet("a01-after-and");
    }

    [Fact]
    public async Task A02_And_after_Unknown_direct_write_in_a_Then()
    {
        await GivenAsync("a02-seed");
        await WhenAsync("a02-act");
        TestPhaseContext.Current = TestPhase.Unknown;
        await AndAsync("a02-and");
        await Loud("a02-after-and");
    }

    [Fact]
    public async Task A03_And_on_a_flow_forked_before_the_Given()
    {
        var gate = new TaskCompletionSource();
        var background = Task.Run(async () =>
        {
            await gate.Task;
            await AndAsync("a03-bg-and");
            await Quiet("a03-bg-after");
            await Loud("a03-bg-after");
        });
        await GivenAsync("a03-seed");
        gate.SetResult();
        await background;
        await WhenAsync("a03-act");
    }

    [Fact]
    public async Task A04_And_on_a_flow_forked_before_the_When_and_running_after_it()
    {
        var gate = new TaskCompletionSource();
        var background = Task.Run(async () =>
        {
            await gate.Task;
            await AndAsync("a04-bg-and");
            await Quiet("a04-bg-after");
            await Loud("a04-bg-after");
        });
        await GivenAsync("a04-seed");
        await WhenAsync("a04-act");
        gate.SetResult();
        await background;
        await ThenAsync("a04-then");
    }

    [Fact]
    public async Task A05_And_with_a_scope_on_an_unnamed_flow()
    {
        var name = TestContext.Current.Test!.TestDisplayName;
        var id = Id;
        var gate = new TaskCompletionSource();
        Task background;
        using (ExecutionContext.SuppressFlow())
        {
            background = Task.Run(async () =>
            {
                await gate.Task;
                using (TestIdentityScope.Begin(name, id))
                {
                    await AndAsync("a05-service-and");
                    await Loud("a05-service-after");
                    await Quiet("a05-service-after");
                }
            });
        }
        await GivenAsync("a05-seed");
        await WhenAsync("a05-act");
        gate.SetResult();
        await background;
    }
}

/// <summary>A keyword on a flow forked outside any step decides for the whole test.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Pollers : Base
{
    [Fact]
    public async Task P01_Then_poller_forked_at_the_start_fires_during_the_Given()
    {
        var gate = new TaskCompletionSource();
        var done = new TaskCompletionSource();
        var background = Task.Run(async () =>
        {
            await gate.Task;
            await ThenAsync("p01-poll");
            done.SetResult();
        });
        await GivenAsync("p01-seed1");
        gate.SetResult();
        await done.Task;
        await GivenAsync("p01-seed2");
        await WhenAsync("p01-act");
        await background;
    }

    [Fact]
    public async Task P02_Then_poller_fires_before_any_step()
    {
        var background = Task.Run(() => ThenAsync("p02-poll"));
        await background;
        await GivenAsync("p02-seed");
        await WhenAsync("p02-act");
    }

    [Fact]
    public async Task P03_Given_poller_fires_after_the_When_on_a_forked_flow_then_And_elsewhere()
    {
        var gate = new TaskCompletionSource();
        var background = Task.Run(async () =>
        {
            await gate.Task;
            await GivenAsync("p03-bg-given");
        });
        await GivenAsync("p03-seed");
        await WhenAsync("p03-act");
        gate.SetResult();
        await background;
        var other = Task.Run(() => AndAsync("p03-and-on-a-new-flow"));
        await other;
        await ThenAsync("p03-then");
    }
}

/// <summary>Hand-made steps through the public overloads.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Hand : Base
{
    [GivenStep]
    public void GivenWithAmbientBypass(string label)
    {
        StepCollector.StartStep(null, label + "-manual", null, null);
        StepCollector.BypassStep("manual");
    }

    [WhenStep]
    public void WhenWithAmbientBypass(string label)
    {
        StepCollector.StartStep(null, label + "-manual", null, null);
        StepCollector.BypassStep("manual");
        Call(label + "-after-bypass").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task H01_Ambient_bypass_in_a_sync_Given_then_When_Then()
    {
        GivenWithAmbientBypass("h01");
        await WhenAsync("h01-act");
        await ThenAsync("h01-then");
    }

    [Fact]
    public async Task H02_Ambient_bypass_in_a_sync_When_then_Given_Then()
    {
        await GivenAsync("h02-seed");
        WhenWithAmbientBypass("h02-act");
        await GivenAsync("h02-given2");
        await ThenAsync("h02-then");
    }

    [Fact]
    public async Task H03_Hand_Given_completed_on_another_flow_then_When_And()
    {
        StepCollector.StartStep(Id, "Given", "h03-hand", null, null);
        await Call("h03-seed");
        await Task.Run(() => StepCollector.CompleteStep(Id, true));
        await WhenAsync("h03-act");
        await AndAsync("h03-and");
        await ThenAsync("h03-then");
    }

    [Fact]
    public async Task H04_Hand_Given_never_completed_then_When_Then()
    {
        StepCollector.StartStep(Id, "Given", "h04-hand", null, null);
        await Call("h04-seed");
        await WhenAsync("h04-act");
        await ThenAsync("h04-then");
    }

    public bool Throws => throw new InvalidOperationException("skip getter");
    [GivenStep(SkipIf = nameof(Throws))] public async Task GivenSkipThrows(string label) => await Call(label);

    [Fact]
    public async Task H05_A_SkipIf_getter_that_throws_is_caught_then_When_Then()
    {
        try { await GivenSkipThrows("h05-given"); } catch (InvalidOperationException) { }
        await WhenAsync("h05-act");
        await ThenAsync("h05-then");
    }

    [Fact]
    public async Task H06_CompleteStepAsync_by_hand_in_a_When_with_no_step_of_its_own()
    {
        await GivenAsync("h06-seed");
        await WhenWithStrayComplete("h06");
        await ThenAsync("h06-then");
    }

    [WhenStep]
    public async Task WhenWithStrayComplete(string label)
    {
        await Call(label + "-before");
        await StepCollector.CompleteStepAsync(Task.CompletedTask);
        await GivenAsync(label + "-sub");
        await Call(label + "-after");
    }

    [Fact]
    public async Task H07_Hand_pair_on_the_test_flow_with_CompleteStepAsync()
    {
        StepCollector.StartStep("Given", "h07-hand-given", null, null);
        await StepCollector.CompleteStepAsync(Call("h07-seed"));
        await Call("h07-after-given");
        StepCollector.StartStep("When", "h07-hand-when", null, null);
        await StepCollector.CompleteStepAsync(Call("h07-act"));
        await Call("h07-after-when");
    }

    [Fact]
    public async Task H08_Hand_pair_nested_keyword_under_keyword()
    {
        await GivenAsync("h08-seed");
        StepCollector.StartStep(Id, "When", "h08-hand-when", null, null);
        await Call("h08-act");
        StepCollector.StartStep(Id, "Given", "h08-hand-sub", null, null);
        await Call("h08-sub");
        StepCollector.CompleteStep(Id, true);
        await Call("h08-after-sub");
        StepCollector.CompleteStep(Id, true);
        await Call("h08-after-when");
    }

    [Fact]
    public async Task H09_Hand_And_on_the_test_flow_first_of_all()
    {
        StepCollector.StartStep(Id, "And", "h09-and", null, null);
        await Call("h09-in-and");
        StepCollector.CompleteStep(Id, true);
        await GivenAsync("h09-given");
        await WhenAsync("h09-act");
    }

    [Fact]
    public async Task H10_Hand_Given_on_a_forked_flow_inside_a_When()
    {
        await GivenAsync("h10-seed");
        await WhenWithCallbackStep("h10");
        await ThenAsync("h10-then");
    }

    [WhenStep]
    public async Task WhenWithCallbackStep(string label)
    {
        var done = new TaskCompletionSource();
        var id = Id;
        _ = Task.Run(async () =>
        {
            StepCollector.StartStep(id, "Given", label + "-callback", null, null);
            await Call(label + "-in-callback");
            StepCollector.CompleteStep(id, true);
            await Call(label + "-after-callback");
            done.SetResult();
        });
        await done.Task;
        await Call(label + "-after");
    }

    [Fact]
    public async Task H11_Bypass_by_id_of_a_step_on_the_test_flow_then_ambient_complete()
    {
        await GivenAsync("h11-seed");
        StepCollector.StartStep(Id, "When", "h11-hand", null, null);
        StepCollector.BypassStep(Id, "by id");
        await Call("h11-after-bypass");
        StepCollector.CompleteStep(true);
        await WhenAsync("h11-act");
        await ThenAsync("h11-then");
    }

    [GivenStep]
    public async Task GivenCallsOtherIdThenWhen(string label)
    {
        await Call($"{label}-seed");
        StepCollector.StartStep("some-other-test", "Given", "other", null, null);
        StepCollector.CompleteStep("some-other-test", true);
        await WhenAsync($"{label}-label-under-given");
        await Call($"{label}-after-label");
    }

    [GivenStep]
    public Task GivenTaskWithAmbientBypass(string label)
    {
        StepCollector.StartStep(null, label + "-manual", null, null);
        StepCollector.BypassStep("manual");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task H13_Ambient_bypass_in_a_non_async_Task_Given_then_When_Then()
    {
        await GivenTaskWithAmbientBypass("h13");
        await WhenAsync("h13-act");
        await ThenAsync("h13-then");
    }

    [Fact]
    public async Task H12_A_hand_step_of_another_id_inside_a_Given()
    {
        await GivenCallsOtherIdThenWhen("h12");
        await WhenAsync("h12-act");
    }
}

/// <summary>Steps in parallel inside a test.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Parallel_ : Base
{
    [WhenStep]
    public async Task WhenWhenAllGivens(string label)
    {
        await Task.WhenAll(GivenAsync(label + "-x"), GivenAsync(label + "-y"), GivenAsync(label + "-z"));
        await Call(label + "-after");
    }

    [WhenStep]
    public void WhenParallelForEachGivens(string label)
    {
        Parallel.ForEach(new[] { "a", "b", "c", "d" }, new ParallelOptions { MaxDegreeOfParallelism = 4 }, l => GivenSync($"{label}-{l}"));
        Call(label + "-after").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task L01_WhenAll_of_Givens_inside_a_When()
    {
        await GivenAsync("l01-seed");
        await WhenWhenAllGivens("l01");
        await ThenAsync("l01-then");
    }

    [Fact]
    public async Task L02_Parallel_ForEach_of_sync_Givens_inside_a_When()
    {
        await GivenAsync("l02-seed");
        WhenParallelForEachGivens("l02");
        await ThenAsync("l02-then");
    }

    [Fact]
    public async Task L03_WhenAll_of_a_Given_and_a_When_at_the_top()
    {
        await Task.WhenAll(GivenAsync("l03-given"), WhenAsync("l03-when"));
        await Call("l03-after");
        await ThenAsync("l03-then");
    }

    [Fact]
    public async Task L04_Parallel_ForEach_of_sync_Givens_at_the_top_then_When_with_nested_Given()
    {
        Parallel.ForEach(new[] { "a", "b", "c", "d" }, new ParallelOptions { MaxDegreeOfParallelism = 4 }, l => GivenSync($"l04-{l}"));
        await Call("l04-after-givens");
        await WhenWithSub("l04-when");
        await ThenAsync("l04-then");
    }

    [WhenStep]
    public async Task WhenWithSub(string label)
    {
        await Call(label + "-before");
        await GivenAsync(label + "-sub");
        await Call(label + "-after");
    }

    [GivenStep]
    public async Task GivenSlow(string label, Task gate)
    {
        await gate;
        await Call(label);
    }

    [Fact]
    public async Task L05_A_Given_left_running_past_a_timeout_inside_a_When()
    {
        var gate = new TaskCompletionSource();
        await GivenAsync("l05-seed");
        Task? slow = null;
        await WhenLeavingGiven("l05", gate.Task, t => slow = t);
        await ThenAsync("l05-then");
        gate.SetResult();
        await slow!;
        await ThenAsync("l05-then2");
    }

    [WhenStep]
    public async Task WhenLeavingGiven(string label, Task gate, Action<Task> keep)
    {
        var slow = GivenSlow(label + "-slow", gate);
        keep(slow);
        await Task.WhenAny(slow, Task.Delay(1));
        await GivenAsync(label + "-sub");
        await Call(label + "-after");
    }

    [Fact]
    public async Task L06_Sync_over_async_When_with_nested_async_Given()
    {
        await GivenAsync("l06-seed");
        WhenBlocking("l06");
        await ThenAsync("l06-then");
    }

    [WhenStep]
    public void WhenBlocking(string label)
    {
        Call(label + "-before").GetAwaiter().GetResult();
        GivenAsync(label + "-sub").GetAwaiter().GetResult();
        Call(label + "-after").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task L07_Task_Run_blocking_Given_at_the_top_then_call_then_When()
    {
        Task.Run(() => GivenAsync("l07-given")).GetAwaiter().GetResult();
        await Call("l07-after-given");
        await WhenAsync("l07-act");
    }

    [WhenStep]
    public async Task<int> WhenGeneric(string label)
    {
        await Call(label + "-before");
        var n = await GivenGeneric(label + "-sub");
        await Call(label + "-after");
        return n;
    }

    [GivenStep]
    public async Task<int> GivenGeneric(string label)
    {
        await Call(label);
        return 1;
    }

    [Fact]
    public async Task L08_Generic_Task_of_T_nesting()
    {
        var a = await GivenGeneric("l08-seed");
        var b = await WhenGeneric("l08-when");
        await ThenAsync("l08-then");
        Assert.Equal(2, a + b);
    }

    [WhenStep]
    public async void WhenAsyncVoid(string label, TaskCompletionSource done)
    {
        await Call(label + "-before");
        await GivenAsync(label + "-sub");
        await Call(label + "-after");
        done.SetResult();
    }

    [Fact]
    public async Task L10_Async_void_When_with_nested_Given()
    {
        await GivenAsync("l10-seed");
        var done = new TaskCompletionSource();
        WhenAsyncVoid("l10-when", done);
        await done.Task;
        await Call("l10-after");
        await ThenAsync("l10-then");
    }

    [WhenStep]
    public Task WhenForksAndReturnsItsTask(string label) => Task.Run(async () =>
    {
        await Call(label + "-before");
        await GivenAsync(label + "-sub");
        await Call(label + "-after");
    });

    [WhenStep]
    public Task WhenForksAndReturnsDone(string label, Action<Task> keep)
    {
        keep(Task.Run(async () =>
        {
            await Task.Delay(5);
            await Call(label + "-before");
            await GivenAsync(label + "-sub");
            await Call(label + "-after");
        }));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task L11_Non_async_When_that_returns_the_task_it_forked()
    {
        await GivenAsync("l11-seed");
        await WhenForksAndReturnsItsTask("l11-when");
        await Call("l11-after");
        await ThenAsync("l11-then");
    }

    [Fact]
    public async Task L12_Non_async_When_that_forks_and_returns_at_once()
    {
        await GivenAsync("l12-seed");
        Task? fork = null;
        await WhenForksAndReturnsDone("l12-when", t => fork = t);
        await Call("l12-after");
        await fork!;
        await ThenAsync("l12-then");
    }

    [Fact]
    public async Task L13_Steps_in_a_lazy_task_shared_by_two_flows()
    {
        var lazy = new Lazy<Task>(() => GivenAsync("l13-lazy-given"));
        await Task.WhenAll(Task.Run(() => lazy.Value), Task.Run(() => lazy.Value));
        await lazy.Value;
        await Call("l13-after");
        await WhenAsync("l13-act");
    }

    [Fact]
    public async Task L14_A_When_on_a_ContinueWith_after_the_Given_closed()
    {
        Task? cont = null;
        await GivenWithContinuation("l14", t => cont = t);
        await Call("l14-after-given");
        await cont!;
        await Call("l14-after-cont");
        await WhenAsync("l14-act");
    }

    [GivenStep]
    public async Task GivenWithContinuation(string label, Action<Task> keep)
    {
        await Call(label + "-seed");
        keep(Task.Delay(5).ContinueWith(_ => WhenAsync(label + "-in-cont")).Unwrap());
    }
}
