using Kronikol.Tracking;
using Kronikol.xUnit3;

namespace Probe;

public class Flows2 : DiagrammedComponentTest
{
    public bool Skip => true;

    // ---- a step method on a thread that shares no flow with the test, named by an identity scope
    [Fact] public async Task G01_Step_on_suppressed_flow_during_a_parent()
    {
        await Parent1();
    }
    [Step] async Task Parent1()
    {
        var me = Api.Me;
        Task t;
        using (ExecutionContext.SuppressFlow())
            t = Task.Run(() => { using var _ = TestIdentityScope.Begin(me.Name, me.Id); OnOther1(); });
        await t;
        await Api.Call("in-Parent1");
    }
    [Step] void OnOther1() { RequestResponseLogger.LogPair(Api.Me0.Name, Api.Me0.Id, HttpMethod.Get, new Uri("http://svc.local/in-OnOther1"), "Svc", "Svc2"); }

    // ---- an assertion and an attachment from a flow that is in no step, while two kids run
    [Fact] public async Task G02_Foreign_assertion_and_attachment_while_two_kids_run() { await Parent2(); }
    [Step] async Task Parent2() { var me = Api.Me; Api.Me0 = me; await Task.WhenAll(K2a(), K2b(me)); }
    [Step] async Task K2a() { await Task.Delay(150); }
    [Step] async Task K2b((string Name, string Id) me)
    {
        Task t;
        using (ExecutionContext.SuppressFlow())
            t = Task.Run(() =>
            {
                using var _ = TestIdentityScope.Begin(me.Name, me.Id);
                Track.That(() => Assert.True(true), "assert-from-foreign-flow");
                Track.Attachment(Tmp("foreign"), "attach-from-foreign-flow");
                RequestResponseLogger.LogPair(me.Name, me.Id, HttpMethod.Get, new Uri("http://svc.local/call-from-foreign-flow"), "Svc", "Svc2");
            });
        await t;
        await Task.Delay(50);
    }
    static string Tmp(string n) { var p = Path.Combine(Path.GetTempPath(), "k225-" + n + ".txt"); File.WriteAllText(p, "x"); return p; }

    // ---- ValueTask-shaped code around a step
    [Fact] public async Task G03_ValueTask_around_a_step() { await Around3(); await Wrap3(); await Api.Call("after-all"); }
    async ValueTask Around3() { await Stepped3("around"); await Api.Call("in-Around3-after-step"); }
    ValueTask Wrap3() => new(Stepped3("wrap"));
    [Step] async Task Stepped3(string w) { await Task.Yield(); await Api.Call("in-Stepped3-" + w); }

    // ---- a step running on after its test ended, into the next test
    public static Task? Lingering;
    [Fact] public async Task G04a_Step_runs_into_the_next_test() { Lingering = Linger4(); await Task.Delay(1); await Api.Call("in-G04a-body"); }
    [Step] async Task Linger4() { await Task.Delay(100); await Api.Call("in-Linger4-late"); await LingerSub4(); }
    [Step] async Task LingerSub4() { await Api.Call("in-LingerSub4-late"); }
    [Fact] public async Task G04b_The_next_test() { await Next4(); await Task.Delay(200); await Next4b(); }
    [Step] async Task Next4() { await Api.Call("in-Next4"); }
    [Step] async Task Next4b() { await Api.Call("in-Next4b"); }

    // ---- WhenAny, the loser runs on
    [Fact] public async Task G05_WhenAny_loser() { await Parent5(); await After5(); await Task.Delay(120); await Api.Call("after-loser-ended"); }
    [Step] async Task Parent5() { await Task.WhenAny(Fast5(), Slow5()); await Api.Call("in-Parent5-after-any"); }
    [Step] async Task Fast5() { await Task.Delay(5); await Api.Call("in-Fast5"); }
    [Step] async Task Slow5() { await Task.Delay(100); await Api.Call("in-Slow5"); }
    [Step] async Task After5() { await Api.Call("in-After5"); }

    // ---- nested WhenAll
    [Fact] public async Task G06_Nested_when_all() { await Top6(); }
    [Step] async Task Top6() { await Task.WhenAll(Mid6("a"), Mid6("b")); }
    [Step] async Task Mid6(string m) { await Task.WhenAll(Leaf6(m + "1"), Leaf6(m + "2")); await Api.Call("in-Mid6-" + m); }
    [Step] async Task Leaf6(string l) { await Task.Delay(l.EndsWith('1') ? 5 : 15); await Api.Call("in-Leaf6-" + l); }

    // ---- parent fails while a kid it did not await runs on
    [Fact] public async Task G07_Parent_fails_kid_runs_on() { try { await Parent7(); } catch { } await Next7(); await Task.Delay(60); }
    [Step] async Task Parent7() { _ = Kid7(); await Api.Call("in-Parent7"); throw new InvalidOperationException("Parent7 failed"); }
    [Step] async Task Kid7() { await Task.Delay(40); await Api.Call("in-Kid7"); }
    [Step] async Task Next7() { await Api.Call("in-Next7"); }

    // ---- lifecycle: a step in the constructor, in InitializeAsync, in DisposeAsync
    public class WithLifetime : DiagrammedComponentTest, IAsyncLifetime
    {
        public WithLifetime() { InCtor(); }
        [Step] void InCtor() { Api.Call("in-ctor").GetAwaiter().GetResult(); }
        public async ValueTask InitializeAsync() { await InInit(); }
        [Step] async Task InInit() { await Task.Delay(1); await Api.Call("in-init"); }
        public async ValueTask DisposeAsync() { await InDisposeAsync(); }
        [Step] async Task InDisposeAsync() { await Api.Call("in-dispose"); }
        [Fact] public async Task G08_Lifecycle() { await Body(); }
        [Step] async Task Body() { await Api.Call("in-body"); }
    }

    // ---- the public API by hand, across async helpers
    [Fact] public async Task G09_Manual_api_across_helpers()
    {
        var id = Api.Me.Id;
        await Begin9(id);
        await Api.Call("in-manual");
        await Nested9();
        await End9(id);
        await Api.Call("after-manual");
        StepCollector.StartStep(id, "Then", "manual-sync", null, null);
        await Api.Call("in-manual-sync");
        StepCollector.CompleteStep(id, false, "manual failure");
        await Api.Call("after-manual-sync");
    }
    static async Task Begin9(string id) { StepCollector.StartStep(id, "Given", "manual-async", null, null); await Task.Yield(); }
    static async Task End9(string id) { await Task.Yield(); StepCollector.CompleteStep(id, true); }
    [Step] async Task Nested9() { await Api.Call("in-Nested9"); }

    // ---- two manual steps overlapping
    [Fact] public async Task G10_Manual_overlap()
    {
        var id = Api.Me.Id;
        StepCollector.StartStep(id, "Given", "manual-one", null, null);
        StepCollector.StartStep(id, "When", "manual-two", null, null);
        StepCollector.CompleteStep(id, true); // closes two
        await Api.Call("in-one-after-two");
        StepCollector.CompleteStep(id, true); // closes one
        await Api.Call("after-both");
        StepCollector.CompleteStep(id, true); // a spare: nothing open
    }

    // ---- a straggler starts a step while the next is open: where does it land
    [Fact] public async Task G11_Straggler_starts_a_step() { await Start11(); await Next11(); }
    Task? _t11; readonly TaskCompletionSource _go11 = new();
    [Step] async Task Start11() { _t11 = Task.Run(async () => { await _go11.Task; await Late11(); }); await Task.Yield(); }
    [Step] async Task Late11() { await Api.Call("in-Late11-started-by-Start11"); }
    [Step] async Task Next11() { _go11.SetResult(); await _t11!; await Api.Call("in-Next11"); }

    // ---- a sync step run on Task.Run while the test's own sync step is open
    [Fact] public async Task G12_Parallel_sync_top_level_two() { await Task.WhenAll(Task.Run(() => S12("a")), Task.Run(() => S12("b"))); }
    [Step] void S12(string n) { Thread.Sleep(50); Api.Call("in-S12-" + n).GetAwaiter().GetResult(); Thread.Sleep(50); }

    // ---- keywords over concurrent top-level steps
    [Fact] public async Task G13_Keywords_with_overlap() { var a = GivenA13(); var b = GivenB13(); await Task.WhenAll(a, b); await WhenC13(); await ThenD13(); }
    [GivenStep] async Task GivenA13() { await Task.Delay(10); await Api.Call("in-GivenA13"); }
    [GivenStep] async Task GivenB13() { await Task.Delay(10); await Api.Call("in-GivenB13"); }
    [WhenStep] async Task WhenC13() { await Api.Call("in-WhenC13"); }
    [ThenStep] async Task ThenD13() { await Api.Call("in-ThenD13"); }

    // ---- sync bypass with a sibling left running at top level
    [Fact] public async Task G14_Sync_SkipIf_with_sibling_running() { var bg = Bg14(); Skipped14(); await Api.Call("after-skip"); await bg; }
    [Step] async Task Bg14() { await Task.Delay(50); await Api.Call("in-Bg14"); }
    [Step(SkipIf = nameof(Skip))] void Skipped14() { }

    // ---- a sub-step fails on a flow that the parent never joins
    [Fact] public async Task G15_Unobserved_sub_step_failure() { await Parent15(); await Task.Delay(40); }
    [Step] async Task Parent15() { _ = Bad15(); await Api.Call("in-Parent15"); }
    [Step] async Task Bad15() { await Task.Delay(10); await Api.Call("in-Bad15"); throw new InvalidOperationException("unobserved"); }

    // ---- attachment and assertion made after the flow's own step ended and nothing else open
    [Fact] public async Task G16_Assertion_after_own_step_ended_nothing_open() { await Start16(); _go16.SetResult(); await _t16!; }
    Task? _t16; readonly TaskCompletionSource _go16 = new();
    [Step] async Task Start16() { _t16 = Task.Run(async () => { await _go16.Task; Track.That(() => Assert.True(true), "assert-after-own-step-ended"); Track.Attachment(Tmp("late"), "attach-after-own-step-ended"); }); await Task.Yield(); }
}
