using Kronikol.Tracking;
using Kronikol.xUnit3;

namespace Probe;

public class Flows : DiagrammedComponentTest
{
    public bool Skip => true;
    public bool NoSkip => false;

    // ---- baseline: nothing overlaps
    [Fact] public async Task F01_Sequential_baseline()
    {
        await GivenA(); await WhenB(); await ThenC();
    }
    [GivenStep] async Task GivenA() { await Api.Call("in-GivenA"); }
    [WhenStep] async Task WhenB() { await Api.Call("in-WhenB-before-sub"); await SubB(); await Api.Call("in-WhenB-after-sub"); }
    [Step] async Task SubB() { await Api.Call("in-SubB"); }
    [ThenStep] async Task ThenC() { await Api.Call("in-ThenC"); }

    // ---- WhenAll under a parent
    [Fact] public async Task F02_WhenAll_under_parent() { await Parent2(); await After2(); }
    [Step] async Task Parent2() { await Task.WhenAll(Kid2a(), Kid2b()); await Api.Call("in-Parent2-after-kids"); }
    [Step] async Task Kid2a() { await Task.Delay(20); await Api.Call("in-Kid2a"); await Task.Delay(40); await Api.Call("in-Kid2a-2"); }
    [Step] async Task Kid2b() { await Task.Delay(40); await Api.Call("in-Kid2b"); await Task.Delay(40); await Api.Call("in-Kid2b-2"); }
    [Step] async Task After2() { await Api.Call("in-After2"); }

    // ---- WhenAll at top level + a call from a service thread (no flow)
    [Fact] public async Task F03_WhenAll_top_level_and_foreign_call()
    {
        var me = Api.Me;
        var gate = new TaskCompletionSource();
        var t = new Thread(() => { gate.Task.Wait(); Api.FromElsewhere(me.Name, me.Id, "from-service-thread-during-both"); });
        t.Start();
        var a = Top3a(gate); var b = Top3b();
        await Task.WhenAll(a, b);
        t.Join();
        await Api.Call("after-both");
    }
    [Step] async Task Top3a(TaskCompletionSource gate) { await Task.Delay(50); await Api.Call("in-Top3a"); gate.SetResult(); await Task.Delay(50); }
    [Step] async Task Top3b() { await Task.Delay(30); await Api.Call("in-Top3b"); await Task.Delay(100); }

    // ---- detached task outlives its step
    [Fact] public async Task F04_Detached_call_outlives_step()
    {
        await Starter4(); await Next4();
    }
    Task? _detached4;
    readonly TaskCompletionSource _go4 = new();
    [Step] async Task Starter4() { _detached4 = Task.Run(async () => { await _go4.Task; await Api.Call("detached-from-Starter4"); }); await Api.Call("in-Starter4"); }
    [Step] async Task Next4() { _go4.SetResult(); await _detached4!; await Api.Call("in-Next4"); }

    // ---- Parallel.ForEachAsync with a step body
    [Fact] public async Task F05_ParallelForEachAsync_in_parent() { await Parent5(); }
    [Step] async Task Parent5() { await Parallel.ForEachAsync(Enumerable.Range(0, 4), new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (i, _) => await Body5(i)); }
    [Step] async Task Body5(int i) { await Task.Delay(10); await Api.Call("in-Body5-" + i); }

    // ---- sync steps across threads, in a parent and at top level
    [Fact] public void F06_Parallel_For_sync_steps_in_parent() { Parent6(); }
    [Step] void Parent6() { Parallel.For(0, 4, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => Body6(i)); }
    [Step] void Body6(int i) { Thread.Sleep(30); Api.Call("in-Body6-" + i).GetAwaiter().GetResult(); Thread.Sleep(30); }

    [Fact] public void F07_Parallel_For_sync_steps_top_level() { Parallel.For(0, 4, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => Body7(i)); }
    [Step] void Body7(int i) { Thread.Sleep(30); Api.Call("in-Body7-" + i).GetAwaiter().GetResult(); Thread.Sleep(30); }

    // ---- fire-and-forget step still open at test end
    [Fact] public async Task F08_Step_left_running_at_test_end() { _ = Forever8(); await Task.Delay(20); await Api.Call("after-Forever8-started"); }
    [Step] async Task Forever8() { await Api.Call("in-Forever8"); await Task.Delay(Timeout.Infinite); }

    // ---- fire-and-forget step overlapping a following step
    [Fact] public async Task F09_FireForget_step_then_next() { var t = Bg9(); await Next9(); await t; await Api.Call("after-all"); }
    [Step] async Task Bg9() { await Task.Delay(30); await Api.Call("in-Bg9"); await Task.Delay(40); await Api.Call("in-Bg9-2"); }
    [Step] async Task Next9() { await Task.Delay(50); await Api.Call("in-Next9"); }

    // ---- SuppressFlow
    [Fact] public async Task F10_SuppressFlow_in_step() { await Step10(); }
    [Step] async Task Step10() { var me = Api.Me; Task t; using (ExecutionContext.SuppressFlow()) { t = Task.Run(() => Api.FromElsewhere(me.Name, me.Id, "suppressed-flow-from-Step10")); } await t; await Api.Call("in-Step10"); }

    // ---- forwarders
    [Fact] public async Task F11_Non_async_forwarder() { await Fwd11(); await Next11(); }
    [Step] Task Fwd11() => Inner11();
    [Step] async Task Inner11() { await Task.Delay(5); await Api.Call("in-Inner11"); }
    [Step] async Task Next11() { await Api.Call("in-Next11"); }

    Task Plain12() => Stepped12();
    [Fact] public async Task F12_Plain_forwarder_to_step() { await Plain12(); await Api.Call("after-Stepped12"); }
    [Step] async Task Stepped12() { await Task.Delay(5); await Api.Call("in-Stepped12"); }

    // ---- sync over async
    [Fact] public void F13_Sync_over_async() { Sync13(); Api.Call("after-Sync13").GetAwaiter().GetResult(); }
    [Step] void Sync13() { Async13().GetAwaiter().GetResult(); Api.Call("in-Sync13-after").GetAwaiter().GetResult(); }
    [Step] async Task Async13() { await Task.Delay(5); await Api.Call("in-Async13"); }

    // ---- ValueTask
    [Fact] public async Task F14_ValueTask_step() { await VT14(); await Next14(); }
    [Step] async ValueTask VT14() { await Api.Call("in-VT14-first"); await Task.Delay(5); await Api.Call("in-VT14-second"); await SubVT14(); }
    [Step] async Task SubVT14() { await Api.Call("in-SubVT14"); }
    [Step] async Task Next14() { await Api.Call("in-Next14"); }

    [Fact] public async Task F14b_ValueTask_overlaps_next() { var t = VT14b(); await Next14b(); await t; }
    [Step] async ValueTask VT14b() { await Task.Delay(10); await Api.Call("in-VT14b"); await SubVT14b(); }
    [Step] async Task SubVT14b() { await Task.Delay(30); await Api.Call("in-SubVT14b"); }
    [Step] async Task Next14b() { await Task.Delay(100); await Api.Call("in-Next14b"); }

    // ---- throws before returning a task
    [Fact] public async Task F15_Throws_before_task_returned()
    {
        try { await Boom15(null); } catch (ArgumentNullException) { }
        await Next15();
        await Api.Call("after-Next15");
    }
    [Step] Task Boom15(string? x) { ArgumentNullException.ThrowIfNull(x); return Task.CompletedTask; }
    [Step] async Task Next15() { await Api.Call("in-Next15"); }

    // ---- SkipIf
    [Fact] public async Task F16_Async_SkipIf_under_parent() { await Parent16(); await After16(); }
    [Step] async Task Parent16() { await Api.Call("in-Parent16-before"); await Skipped16(); await Api.Call("in-Parent16-after-skip"); await Kid16(); await Api.Call("in-Parent16-end"); }
    [Step(SkipIf = nameof(Skip), SkipReason = "nope")] async Task Skipped16() { await Api.Call("never"); }
    [Step] async Task Kid16() { await Api.Call("in-Kid16"); }
    [Step] async Task After16() { await Api.Call("in-After16"); }

    [Fact] public void F17_Sync_SkipIf_under_parent() { Parent17(); After17(); }
    [Step] void Parent17() { Api.Call("in-Parent17-before").GetAwaiter().GetResult(); Skipped17(); Api.Call("in-Parent17-after-skip").GetAwaiter().GetResult(); Kid17(); }
    [Step(SkipIf = nameof(Skip), SkipReason = "nope")] void Skipped17() { }
    [Step] void Kid17() { Api.Call("in-Kid17").GetAwaiter().GetResult(); }
    [Step] void After17() { Api.Call("in-After17").GetAwaiter().GetResult(); }

    [Fact] public async Task F18_Async_SkipIf_top_level_with_sibling_running() { var bg = Bg18(); await Skipped18(); await Api.Call("after-skip"); await bg; }
    [Step] async Task Bg18() { await Task.Delay(100); await Api.Call("in-Bg18"); }
    [Step(SkipIf = nameof(Skip))] async Task Skipped18() { await Task.Delay(1); }

    // ---- sub-step fails and is caught; a sub-step fails and is not
    [Fact] public async Task F19_Sub_step_fails_caught() { await Parent19(); }
    [Step] async Task Parent19() { try { await Bad19(); } catch { } await Api.Call("in-Parent19-after-bad"); }
    [Step] async Task Bad19() { await Api.Call("in-Bad19"); await Task.Yield(); throw new InvalidOperationException("Bad19 went wrong"); }

    [Fact] public async Task F20_Sub_step_fails_and_propagates() { await Parent20(); }
    [Step] async Task Parent20() { await Api.Call("in-Parent20"); await Ok20(); await Bad20(); }
    [Step] async Task Ok20() { await Api.Call("in-Ok20"); }
    [Step] async Task Bad20() { await Api.Call("in-Bad20"); throw new InvalidOperationException("Bad20 went wrong"); }

    // ---- one of two parallel kids fails
    [Fact] public async Task F21_Parallel_kid_fails() { await Parent21(); }
    [Step] async Task Parent21() { await Task.WhenAll(Good21(), Bad21()); }
    [Step] async Task Good21() { await Task.Delay(50); await Api.Call("in-Good21"); await Task.Delay(50); }
    [Step] async Task Bad21() { await Task.Delay(20); await Api.Call("in-Bad21"); throw new InvalidOperationException("Bad21 went wrong"); }

    // ---- assertions and attachments from other flows
    [Fact] public async Task F22_Assertion_from_sibling_flows() { await Parent22(); }
    [Step] async Task Parent22() { await Task.WhenAll(A22(), B22()); }
    [Step] async Task A22() { await Task.Delay(20); Track.That(() => Assert.True(true), "assert-in-A22"); await Task.Delay(40); }
    [Step] async Task B22() { await Task.Delay(40); Track.That(() => Assert.True(true), "assert-in-B22"); await Task.Delay(40); }

    [Fact] public async Task F23_Assertion_from_detached_after_step_ended() { await Starter23(); await Next23(); await Api.Call("x"); }
    Task? _d23; readonly TaskCompletionSource _g23 = new();
    [Step] async Task Starter23() { _d23 = Task.Run(async () => { await _g23.Task; Track.That(() => Assert.True(true), "assert-from-Starter23-detached"); Track.Attachment(Path.GetTempFileName(), "attach-from-Starter23-detached"); }); await Task.Yield(); }
    [Step] async Task Next23() { _g23.SetResult(); await _d23!; }

    [Fact] public async Task F24_Attachment_from_sibling_flows() { await Parent24(); }
    [Step] async Task Parent24() { await Task.WhenAll(A24(), B24()); }
    [Step] async Task A24() { await Task.Delay(20); Track.Attachment(Path.GetTempFileName(), "attach-in-A24"); await Task.Delay(40); }
    [Step] async Task B24() { await Task.Delay(40); Track.Attachment(Path.GetTempFileName(), "attach-in-B24"); await Task.Delay(40); }

    // ---- deep nesting and many steps
    [Fact] public async Task F25_Deep_nesting() { await Deep(0); }
    [Step] async Task Deep(int d) { await Api.Call("depth-" + d); if (d < 11) await Deep(d + 1); await Api.Call("depth-" + d + "-back"); }

    [Fact] public async Task F26_Many_steps() { for (var i = 0; i < 300; i++) await Many(i); }
    [Step] async Task Many(int i) { if (i % 100 == 0) await Api.Call("many-" + i); }

    // ---- a sub-step left open while its parent finishes
    [Fact] public async Task F27_Child_outlives_parent() { await Parent27(); await Next27(); await _t27!; await Api.Call("after-all"); }
    Task? _t27;
    [Step] async Task Parent27() { _t27 = Child27(); await Api.Call("in-Parent27"); }
    [Step] async Task Child27() { await Task.Delay(50); await Api.Call("in-Child27"); }
    [Step] async Task Next27() { await Task.Delay(100); await Api.Call("in-Next27"); }

    // ---- Task.Run around a top-level async step, three times (race on the stub)
    [Fact] public async Task F28_TaskRun_around_steps_top_level() { await Task.WhenAll(Task.Run(() => R28("a")), Task.Run(() => R28("b")), Task.Run(() => R28("c"))); await Api.Call("after-all"); }
    [Step] async Task R28(string n) { await Task.Delay(30); await Api.Call("in-R28-" + n); }

    // ---- a call from a thread the test started, while one chain is open
    [Fact] public async Task F29_Foreign_call_while_one_chain_open() { await P29(); }
    [Step] async Task P29() { var me = Api.Me; await K29(me); }
    [Step] async Task K29((string Name, string Id) me) { var th = new Thread(() => Api.FromElsewhere(me.Name, me.Id, "foreign-while-K29")); th.Start(); th.Join(); await Api.Call("in-K29"); }

    // ---- foreign call while no step open, between steps
    [Fact] public async Task F30_Foreign_call_between_steps() { await A30(); var me = Api.Me; var th = new Thread(() => Api.FromElsewhere(me.Name, me.Id, "foreign-between")); th.Start(); th.Join(); await Api.Call("between"); await B30(); }
    [Step] async Task A30() { await Api.Call("in-A30"); }
    [Step] async Task B30() { await Api.Call("in-B30"); }

    // ---- a passing test whose calls run before any step, then a step, then after
    [Fact] public async Task F31_Call_before_any_step() { await Api.Call("before-any-step"); await A31(); await Api.Call("after-A31"); }
    [Step] async Task A31() { await Api.Call("in-A31"); }

    // ---- SkipIf false still runs
    [Fact] public async Task F32_SkipIf_false() { await Maybe32(); await Next32(); }
    [Step(SkipIf = nameof(NoSkip))] async Task Maybe32() { await Api.Call("in-Maybe32"); }
    [Step] async Task Next32() { await Api.Call("in-Next32"); }

    // ---- generic returns
    [Fact] public async Task F33_Task_of_T_steps() { var n = await Gen33(); Assert.Equal(1, n); await Next33(); }
    [Step] async Task<int> Gen33() { await Api.Call("in-Gen33"); return await SubGen33(); }
    [Step] async Task<int> SubGen33() { await Task.Delay(1); await Api.Call("in-SubGen33"); return 1; }
    [Step] async Task Next33() { await Api.Call("in-Next33"); }

    // ---- failure outside any step with a left running step
    [Fact] public async Task F34_Failure_with_step_left_running() { _ = Forever34(); await Done34(); throw new InvalidOperationException("test body failed"); }
    [Step] async Task Forever34() { await Api.Call("in-Forever34"); await Task.Delay(Timeout.Infinite); }
    [Step] async Task Done34() { await Api.Call("in-Done34"); }
}
