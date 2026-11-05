using Kronikol;
using Kronikol.Tracking;
using PhaseProbe;
using Xunit;

namespace PhaseProbe.X3;

/// <summary>Hand-made steps closed on a flow other than the one they opened on, and steps left open.</summary>
public class R4Hand : Steps
{
    private static void Step(string id, string? keyword, string text) => StepCollector.StartStep(id, keyword, text, null, null);
    private static async Task Finish(string id) { await Task.Yield(); StepCollector.CompleteStep(id, true); }
    private static async Task Begin(string id, string keyword, string text) { Step(id, keyword, text); await Task.Yield(); }

    private static void Read(string id, string label) => Dump.Note(id, $"{label}: flow reads {TestPhaseContext.Current}");

    [Fact]
    public async Task H11_hand_given_left_open_then_when_with_TrackDuringSetup_off()
    {
        var id = Id;
        var off = Env.NewCalls(trackDuringSetup: false);
        Step(id, "Given", "g"); await C.Make("h11-g"); await off.Make("h11-g-off"); Read(id, "in given");
        Step(id, "When", "w"); await C.Make("h11-w"); await off.Make("h11-w-off"); Read(id, "in when"); StepCollector.CompleteStep(id, true);
        Step(id, "Then", "t"); await C.Make("h11-t"); await off.Make("h11-t-off"); Read(id, "in then");
    }

    [Fact]
    public async Task H12_hand_given_closed_in_an_async_helper_then_hand_when()
    {
        var id = Id;
        Step(id, "Given", "g"); await C.Make("h12-g");
        await Finish(id);
        Step(id, "When", "w"); await C.Make("h12-w"); Read(id, "in when"); StepCollector.CompleteStep(id, true);
        Step(id, "Then", "t"); await C.Make("h12-t"); StepCollector.CompleteStep(id, true);
    }

    [Fact]
    public async Task H13_hand_given_closed_by_Task_Run_then_woven_when()
    {
        var id = Id;
        Step(id, "Given", "g"); await C.Make("h13-g");
        await Task.Run(() => StepCollector.CompleteStep(id, true));
        await WhenCall("h13-w"); Read(id, "after when");
        await ThenCall("h13-t");
    }

    [Fact]
    public async Task H14_hand_given_opened_in_an_async_helper_closed_on_main()
    {
        var id = Id;
        await Begin(id, "Given", "g"); await C.Make("h14-g");
        StepCollector.CompleteStep(id, true);
        Step(id, "When", "w"); await C.Make("h14-w"); Read(id, "in when"); StepCollector.CompleteStep(id, true);
    }

    [Fact]
    public async Task H15_hand_given_closed_by_a_continuation()
    {
        var id = Id;
        Step(id, "Given", "g");
        await C.Make("h15-g").ContinueWith(_ => StepCollector.CompleteStep(id, true));
        Step(id, "When", "w"); await C.Make("h15-w"); Read(id, "in when"); StepCollector.CompleteStep(id, true);
        await ThenCall("h15-t");
    }

    [Fact]
    public async Task H16_hand_steps_each_inside_an_async_helper()
    {
        var id = Id;
        async Task Run(string keyword, string l) { Step(id, keyword, l); try { await C.Make(l); } finally { StepCollector.CompleteStep(id, true); } }
        await Run("Given", "h16-g"); await Run("When", "h16-w"); await Run("Then", "h16-t");
    }

    [Fact]
    public async Task H17_hand_given_left_open_with_a_woven_when_under_it()
    {
        var id = Id;
        var off = Env.NewCalls(trackDuringSetup: false);
        Step(id, "Given", "g"); await C.Make("h17-g");
        await off.Make("h17-off-in-given");
        await WhenOff(off, "h17-w");
        await off.Make("h17-off-after-when");
    }

    [WhenStep] public async Task WhenOff(Calls off, string l) { await C.Make(l); await off.Make(l + "-off"); }

    [Fact]
    public async Task H18_step_opened_and_left_by_a_lifecycle_like_helper_then_real_steps()
    {
        var id = Id;
        // A consumer's "test scope" step, opened once with a keyword, closed at the end.
        Step(id, "Given", "the whole test runs in a Given wrapper");
        await GivenCall("h18-g"); await WhenCall("h18-w"); await ThenCall("h18-t");
        StepCollector.CompleteStep(id, true);
    }
}

/// <summary>Bypassed from a flow that does not hold the step, then the stub of that step leaves.</summary>
public class R4Bypass : Steps
{
    private void BypassFromARawThread(string id)
    {
        Exception? thrown = null;
        var thread = new Thread(() => { try { StepCollector.BypassStep(id, "elsewhere"); } catch (Exception e) { thrown = e; } });
        thread.UnsafeStart();
        thread.Join();
        if (thrown is not null) throw thrown;
    }

    [Step] public async Task Parent(string id) { await Child(id); await C.Make("h20-after-child"); await Sibling(); await C.Make("h20-after-sibling"); }
    [Step] public async Task Child(string id) { BypassFromARawThread(id); await Task.Yield(); await C.Make("h20-in-child"); }
    [Step] public async Task Sibling() { await C.Make("h20-in-sibling"); }

    [Fact]
    public async Task H20_child_bypassed_from_a_raw_thread_under_a_keyword_free_parent()
    {
        var id = Id;
        await GivenCall("h20-g");
        await Parent(id);
        await WhenCall("h20-w");
    }

    [GivenStep] public async Task GivenParent(string id) { await Child(id); await C.Make("h21-after-child"); await Sibling(); }

    [Fact]
    public async Task H21_child_bypassed_from_a_raw_thread_under_a_given_parent()
    {
        var id = Id;
        await GivenParent(id);
        await WhenCall("h21-w");
        await ThenCall("h21-t");
    }

    [Fact]
    public async Task H22_hand_step_bypassed_from_a_raw_thread_then_when()
    {
        var id = Id;
        StepCollector.StartStep(id, "Given", "by hand", null, null);
        await C.Make("h22-g");
        BypassFromARawThread(id);
        await WhenCall("h22-w");
    }
}

/// <summary>A flow forked inside a step that goes on after it closes, with keywords of its own.</summary>
public class R4Fork : Steps
{
    private async Task Loop(CancellationToken stop, string label)
    {
        var i = 0;
        while (!stop.IsCancellationRequested)
        {
            await WhenTick($"{label}-{i++:00}");
            await Task.Delay(10);
        }
    }

    [WhenStep] public async Task WhenTick(string l) => await C.Make(l);
    [ThenStep] public async Task ThenTick(string l) => await C.Make(l);

    private Task _left = Task.CompletedTask;

    [GivenStep]
    public async Task GivenStartsATickingLoop(CancellationToken stop, string label)
    {
        _left = Task.Run(() => Loop(stop, label));
        await Task.Yield();
    }

    [GivenStep] public async Task GivenSlowCall(string l) { await Task.Delay(40); await C.Make(l); }

    [Fact]
    public async Task B05_a_loop_forked_in_a_closed_given_ticks_keyword_steps_while_the_test_is_still_in_setup()
    {
        var id = Id;
        using var stop = new CancellationTokenSource();
        await GivenStartsATickingLoop(stop.Token, "b05-tick");
        await GivenSlowCall("b05-g2");
        await Task.Delay(30);
        Dump.Note(id, "main flow still in setup, reads " + TestPhaseContext.Current);
        await GivenSlowCall("b05-g3");
        stop.Cancel();
        await _left;
        await WhenCall("b05-w");
        await ThenCall("b05-t");
    }

    [Fact]
    public async Task B06_a_loop_forked_with_no_step_open_ticks_a_when_during_the_given()
    {
        using var stop = new CancellationTokenSource();
        var loop = Task.Run(() => Loop(stop.Token, "b06-tick"));
        await GivenSlowCall("b06-g1");
        await GivenSlowCall("b06-g2");
        stop.Cancel();
        await loop;
        await WhenCall("b06-w");
    }

    [Step] public async Task KeywordFreeStartsLoop(CancellationToken stop, string label)
    {
        _left = Task.Run(() => Loop(stop, label));
        await Task.Yield();
    }

    [Fact]
    public async Task B07_a_loop_forked_in_a_closed_keyword_free_step()
    {
        using var stop = new CancellationTokenSource();
        await GivenSlowCall("b07-g0");
        await KeywordFreeStartsLoop(stop.Token, "b07-tick");
        await GivenSlowCall("b07-g1");
        stop.Cancel();
        await _left;
        await WhenCall("b07-w");
    }

    [Fact]
    public async Task B08_steps_on_a_flow_forked_in_an_open_when_which_then_ends()
    {
        var id = Id;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await GivenCall("b08-g");
        Task forked = Task.CompletedTask;
        await WhenForks(() => forked = Task.Run(async () =>
        {
            await gate.Task;
            await GivenCall("b08-fork-g1");
            await GivenCall("b08-fork-g2");
        }));
        gate.SetResult();
        await forked;
        await GivenCall("b08-g2");
        await ThenCall("b08-t");
        Dump.Note(id, "after: " + TestPhaseContext.Current);
    }

    [WhenStep] public async Task WhenForks(Action fork) { fork(); await C.Make("b08-w"); }
}

/// <summary>A worker a test started, on that test's flow, which other tests hand work to: their steps run on a flow that holds the first test's.</summary>
[Collection("R4Worker")]
public class R4WorkerOne : Steps
{
    [Fact] public async Task T20_starts_the_worker_in_a_given() { await StartsIt(); await C.Make("t20-after-given"); await WhenCall("t20-w"); await ThenCall("t20-t"); }
    [GivenStep] public async Task StartsIt() { R4Worker.Start(); await C.Make("t20-g"); }
}

[Collection("R4Worker")]
public class R4WorkerTwo : Steps
{
    [Fact] public async Task T21_hands_work_to_that_worker()
    {
        await GivenCall("t21-g");
        var id = Id;
        await R4Worker.Run(async () =>
        {
            StepCollector.StartStep(id, "When", "on the worker", null, null);
            Dump.Note(id, "on the worker the flow reads " + TestPhaseContext.Current);
            await C.Make("t21-on-worker");
            StepCollector.CompleteStep(id, true);
        });
        Dump.Note(id, "back on the test the flow reads " + TestPhaseContext.Current);
        await WhenCall("t21-w");
        await ThenCall("t21-t");
    }
}

public static class R4Worker
{
    private static readonly System.Threading.Channels.Channel<(Func<Task> Work, TaskCompletionSource Done)> Queue = System.Threading.Channels.Channel.CreateUnbounded<(Func<Task>, TaskCompletionSource)>();
    private static int _started;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await foreach (var (work, done) in Queue.Reader.ReadAllAsync())
            {
                try { await work(); done.SetResult(); }
                catch (Exception e) { done.SetException(e); }
            }
        });
    }

    public static Task Run(Func<Task> work)
    {
        Start();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Writer.TryWrite((work, done));
        return done.Task;
    }
}

/// <summary>Steps with no tracked call in them: the boundary is drawn all the same, and nothing is there to draw it on.</summary>
public class R4NoCalls : Steps
{
    [ThenStep] public Task ThenNothing() => Task.CompletedTask;

    [Fact] public async Task N01_steps_and_no_calls() { await GivenNothing(); await WhenNothing(); await ThenNothing(); }
    [Fact] public async Task N02_no_call_in_the_given_a_call_in_the_when() { await GivenNothing(); await WhenCall("n02-w"); await ThenNothing(); }
    [Fact] public async Task N03_a_call_in_the_given_and_none_after() { await GivenCall("n03-g"); await WhenNothing(); await ThenNothing(); }
    [Fact] public async Task N04_a_call_with_no_step_then_steps() { await C.Make("n04-loose"); await GivenNothing(); await WhenNothing(); await C.Make("n04-after"); }
    [Fact] public async Task N05_failing_with_no_calls() { await GivenNothing(); await WhenNothing(); Assert.Fail("n05 fails on purpose"); }
}

/// <summary>What a long test costs. Only with PROBE_COST=on, as it floods the dump.</summary>
public class R4Cost : ProbeBase
{
    private static bool Off => Environment.GetEnvironmentVariable("PROBE_COST") != "on";

    private static void Timed(string label, Action body)
    {
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        body();
        var ms = clock.ElapsedMilliseconds;
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("PHASE_PROBE_OUT")!, "cost.txt"),
            $"{label}: {ms} ms, {(GC.GetAllocatedBytesForCurrentThread() - before) / 1024 / 1024} MB on this thread\n");
    }

    [Fact]
    public void Z01_many_top_level_keyword_steps()
    {
        if (Off) return;
        var id = "cost-" + Guid.NewGuid().ToString("N");
        Timed("Z01 200k hand Given/When closed, in turn", () =>
        {
            for (var i = 0; i < 200_000; i++)
            {
                StepCollector.StartStep(id, i % 2 == 0 ? "Given" : "When", "s", null, null);
                StepCollector.CompleteStep(id, true);
            }
        });
        StepCollector.ClearSteps(id);
    }

    [Fact]
    public void Z02_many_sub_steps_under_one_open_keyword_step()
    {
        if (Off) return;
        var id = "cost-" + Guid.NewGuid().ToString("N");
        StepCollector.StartStep(id, "When", "top", null, null);
        Timed("Z02 200k hand Given closed, under one open When", () =>
        {
            for (var i = 0; i < 200_000; i++)
            {
                StepCollector.StartStep(id, "Given", "s", null, null);
                StepCollector.CompleteStep(id, true);
            }
        });
        StepCollector.CompleteStep(id, true);
        StepCollector.ClearSteps(id);
    }

    [Fact]
    public void Z03_deep_chain_of_keyword_free_steps_left_open_then_keyword_steps()
    {
        if (Off) return;
        var id = "cost-" + Guid.NewGuid().ToString("N");
        Timed("Z03 20k hand keyword-free left open", () =>
        {
            for (var i = 0; i < 20_000; i++)
                StepCollector.StartStep(id, null, "s", null, null);
        });
        Timed("Z03 then 5k keyword steps closed under that chain", () =>
        {
            for (var i = 0; i < 5_000; i++)
            {
                StepCollector.StartStep(id, "When", "k", null, null);
                StepCollector.CompleteStep(id, true);
            }
        });
        StepCollector.ClearSteps(id);
    }

    [Fact]
    public void Z04_deep_chain_of_keyword_steps_left_open()
    {
        if (Off) return;
        var id = "cost-" + Guid.NewGuid().ToString("N");
        Timed("Z04 50k hand keyword steps left open, each under the last", () =>
        {
            for (var i = 0; i < 50_000; i++)
                StepCollector.StartStep(id, "Given", "s", null, null);
        });
        StepCollector.ClearSteps(id);
    }

    [Fact]
    public async Task Z05_a_step_that_forks_the_next_and_ends_keywords()
    {
        if (Off) return;
        var id = "cost-" + Guid.NewGuid().ToString("N");
        const int n = 20_000;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Next(int i)
        {
            StepCollector.StartStep(id, "When", "k", null, null);
            if (i < n)
            {
                var ctx = ExecutionContext.Capture()!;
                ThreadPool.UnsafeQueueUserWorkItem(_ => ExecutionContext.Run(ctx, _ => Next(i + 1), null), null);
                StepCollector.CompleteStep(id, true);
            }
            else
            {
                StepCollector.CompleteStep(id, true);
                done.SetResult();
            }
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Task.Run(() => Next(0));
        await done.Task;
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("PHASE_PROBE_OUT")!, "cost.txt"), $"Z05 20k steps each forking the next and closing: {clock.ElapsedMilliseconds} ms\n");
        StepCollector.ClearSteps(id);
    }

    [Fact]
    public void Z06_many_tests_each_leaving_a_step_open_on_one_long_flow()
    {
        if (Off) return;
        Timed("Z06 20k tests, each opens one keyword step and leaves it, on one flow", () =>
        {
            for (var i = 0; i < 20_000; i++)
                StepCollector.StartStep("cost-many-" + i, "Given", "s", null, null);
        });
        for (var i = 0; i < 20_000; i++)
            StepCollector.ClearSteps("cost-many-" + i);
    }
}
