using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Rev5;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rev6.A;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "rev6-a");
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
    protected void CallSync(string label) => Call(label).GetAwaiter().GetResult();
    protected async Task Three(string label)
    {
        await Call(label);
        await NoSetup.GetAsync($"http://probe.local/{label}-nosetup");
        await NoAction.GetAsync($"http://probe.local/{label}-noaction");
    }

    [GivenStep] public async Task GivenAsync(string label) => await Call(label);
    [GivenStep] public void GivenSync(string label) => CallSync(label);
    [WhenStep] public async Task WhenAsync(string label) => await Call(label);
    [WhenStep] public void WhenSync(string label) => CallSync(label);
    [ThenStep] public async Task ThenAsync(string label) => await Call(label);
    [Step] public async Task PlainAsync(string label) => await Call(label);
}

/// <summary>T7: the SkipIf member is read before StartStep. What does a getter see, and where do its calls land?</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Getters : Base
{
    private string _l = "";
    private void GetterCalls()
    {
        CallSync($"{_l}-getter");
        NoSetup.GetAsync($"http://probe.local/{_l}-getter-nosetup").GetAwaiter().GetResult();
        NoAction.GetAsync($"http://probe.local/{_l}-getter-noaction").GetAwaiter().GetResult();
    }

    public bool CallsThenRuns { get { GetterCalls(); return false; } }
    public bool CallsThenSkips { get { GetterCalls(); return true; } }
    public bool NotInAction => TestPhaseContext.Current != TestPhase.Action;
    public bool InSetup => TestPhaseContext.Current == TestPhase.Setup;
    public bool NoStepOpen => !StepCollector.HasActiveStep(Id);
    public bool RunsWovenWhen { get { WhenSync($"{_l}-when-in-getter"); return false; } }
    public bool RunsPlainStep { get { PlainSync($"{_l}-plain-in-getter"); return false; } }

    [Step] public void PlainSync(string label) => CallSync(label);

    [GivenStep(SkipIf = nameof(CallsThenRuns))] public async Task GivenGetter(string l) => await Call(l);
    [WhenStep(SkipIf = nameof(CallsThenRuns))] public async Task WhenGetter(string l) => await Call(l);
    [WhenStep(SkipIf = nameof(CallsThenRuns))] public void WhenGetterSync(string l) => CallSync(l);
    [ThenStep(SkipIf = nameof(CallsThenRuns))] public async Task ThenGetter(string l) => await Call(l);
    [WhenStep(SkipIf = nameof(CallsThenSkips))] public async Task WhenGetterSkips(string l) => await Call(l);
    [WhenStep(SkipIf = nameof(NotInAction))] public async Task WhenIfInAction(string l) => await Call(l);
    [ThenStep(SkipIf = nameof(NotInAction))] public void ThenIfInAction(string l) => CallSync(l);
    [GivenStep(SkipIf = nameof(InSetup))] public async Task GivenUnlessSetup(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(NoStepOpen))] public async Task GivenIfStepOpen(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(RunsWovenWhen))] public async Task GivenGetterRunsWhen(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(RunsPlainStep))] public async Task GivenGetterRunsPlain(string l) => await Call(l);

    [Fact]
    public async Task S01_A_Given_getter_makes_tracked_calls_first_of_the_test()
    {
        _l = "s01";
        await GivenGetter("s01-given");
        await WhenAsync("s01-act");
        await ThenAsync("s01-then");
    }

    [Fact]
    public async Task S02_A_When_getter_makes_tracked_calls_after_a_Given()
    {
        _l = "s02";
        await GivenAsync("s02-seed");
        await WhenGetter("s02-act");
        await ThenAsync("s02-then");
    }

    [Fact]
    public async Task S03_A_When_getter_calls_and_skips()
    {
        _l = "s03";
        await GivenAsync("s03-seed");
        await WhenGetterSkips("s03-act-not-run");
        await ThenAsync("s03-then");
    }

    [Fact]
    public async Task S04_A_getter_that_reads_the_phase_decides_whether_a_When_runs()
    {
        await GivenAsync("s04-seed");
        await WhenIfInAction("s04-act-runs-at-5282");
        await ThenAsync("s04-then");
    }

    [Fact]
    public async Task S05_A_Then_getter_reads_the_phase_after_a_Given_alone()
    {
        await GivenAsync("s05-seed");
        ThenIfInAction("s05-then-runs-at-5282");
    }

    [Fact]
    public async Task S06_A_Given_getter_reads_the_phase_two_Givens()
    {
        await GivenUnlessSetup("s06-given1-not-run-at-5282");
        await GivenUnlessSetup("s06-given2-not-run-at-5282");
        await WhenAsync("s06-act");
    }

    [Fact]
    public async Task S07_A_getter_reads_whether_a_step_is_open()
    {
        await GivenIfStepOpen("s07-given-runs-at-5282");
        await WhenAsync("s07-act");
    }

    [Fact]
    public async Task S08_A_getter_runs_a_woven_When()
    {
        _l = "s08";
        await GivenGetterRunsWhen("s08-given");
        await Call("s08-after-given");
        await WhenAsync("s08-act");
        await ThenAsync("s08-then");
    }

    [Fact]
    public async Task S09_A_getter_runs_a_keyword_free_step()
    {
        _l = "s09";
        await GivenGetterRunsPlain("s09-given");
        await WhenAsync("s09-act");
    }

    [Fact]
    public async Task S10_A_sync_When_getter_calls_after_a_Given()
    {
        _l = "s10";
        await GivenAsync("s10-seed");
        WhenGetterSync("s10-act");
        await ThenAsync("s10-then");
    }

    [Fact]
    public async Task S11_A_Then_getter_calls_after_a_When()
    {
        _l = "s11";
        await GivenAsync("s11-seed");
        await WhenAsync("s11-act");
        await ThenGetter("s11-then");
    }
}

/// <summary>T6: a bypass ends in a plain ret, every shape of stub under every shape of parent.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Bypass : Base
{
    public bool Yes => true;
    public static bool StaticYes => true;
    public bool YesField = true;

    [GivenStep(SkipIf = nameof(Yes))] public void SkipSync(string l) => CallSync(l);
    [GivenStep(SkipIf = nameof(Yes))] public async Task SkipAsync(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(Yes))] public Task SkipTask(string l) => Call(l);
    [GivenStep(SkipIf = nameof(StaticYes))] public async Task<int> SkipAsyncInt(string l) { await Call(l); return 1; }
    [GivenStep(SkipIf = nameof(YesField))] public Task<int> SkipTaskInt(string l) { CallSync(l); return Task.FromResult(1); }
    [GivenStep(SkipIf = nameof(Yes))] public async Task<string> SkipAsyncStr(string l) { await Call(l); return l; }
    [GivenStep(SkipIf = nameof(Yes))] public Task<string> SkipTaskStr(string l) { CallSync(l); return Task.FromResult(l); }
    [GivenStep(SkipIf = nameof(Yes))] public async Task<(int, string)> SkipAsyncTuple(string l) { await Call(l); return (1, l); }
    [WhenStep(SkipIf = nameof(Yes))] public async Task SkipWhenAsync(string l) => await Call(l);
    [ThenStep(SkipIf = nameof(Yes))] public void SkipThenSync(string l) => CallSync(l);

    private Task Kid(string kid, string l)
    {
        switch (kid)
        {
            case "sync": SkipSync(l); return Task.CompletedTask;
            case "async": return SkipAsync(l);
            case "task": return SkipTask(l);
            case "asyncint": return AssertInt(SkipAsyncInt(l));
            case "taskint": return AssertInt(SkipTaskInt(l));
            case "asyncstr": return AssertNull(SkipAsyncStr(l));
            case "taskstr": return AssertNull(SkipTaskStr(l));
            case "tuple": return SkipAsyncTuple(l);
            case "when": return SkipWhenAsync(l);
            case "then": SkipThenSync(l); return Task.CompletedTask;
            default: throw new ArgumentException(kid);
        }
    }

    private static async Task AssertInt(Task<int> t) => Assert.Equal(0, await t);
    private static async Task AssertNull(Task<string> t) => Assert.Null(await t);

    [WhenStep]
    public async Task ParentAsync(string l, string kid)
    {
        await Call(l + "-before");
        await Kid(kid, l + "-kid");
        await Three(l + "-after-kid");
        await GivenAsync(l + "-sub");
        await Three(l + "-after-sub");
    }

    [WhenStep]
    public Task ParentTask(string l, string kid)
    {
        Call(l + "-before").GetAwaiter().GetResult();
        Kid(kid, l + "-kid").GetAwaiter().GetResult();
        Three(l + "-after-kid").GetAwaiter().GetResult();
        GivenSync(l + "-sub");
        return Three(l + "-after-sub");
    }

    [WhenStep]
    public void ParentSync(string l, string kid)
    {
        Call(l + "-before").GetAwaiter().GetResult();
        Kid(kid, l + "-kid").GetAwaiter().GetResult();
        Three(l + "-after-kid").GetAwaiter().GetResult();
        GivenSync(l + "-sub");
        Three(l + "-after-sub").GetAwaiter().GetResult();
    }

    [WhenStep] public async Task ParentLastAsync(string l, string kid) { await Call(l + "-before"); await Kid(kid, l + "-kid"); }
    [WhenStep] public Task ParentLastTask(string l, string kid) { CallSync(l + "-before"); return Kid(kid, l + "-kid"); }
    [WhenStep] public void ParentLastSync(string l, string kid) { CallSync(l + "-before"); Kid(kid, l + "-kid").GetAwaiter().GetResult(); }

    public static TheoryData<string, string> Matrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var parent in new[] { "async", "task", "sync", "lastasync", "lasttask", "lastsync" })
            foreach (var kid in new[] { "sync", "async", "task", "asyncint", "taskint", "asyncstr", "taskstr", "tuple", "when", "then" })
                data.Add(parent, kid);
        return data;
    }

    [Theory, MemberData(nameof(Matrix))]
    public async Task B_matrix(string parent, string kid)
    {
        var l = $"m-{parent}-{kid}";
        await GivenAsync(l + "-seed");
        switch (parent)
        {
            case "async": await ParentAsync(l, kid); break;
            case "task": await ParentTask(l, kid); break;
            case "sync": ParentSync(l, kid); break;
            case "lastasync": await ParentLastAsync(l, kid); break;
            case "lasttask": await ParentLastTask(l, kid); break;
            case "lastsync": ParentLastSync(l, kid); break;
        }
        await Three(l + "-after-parent");
        await ThenAsync(l + "-then");
    }

    [WhenStep]
    public async Task Depth1(string l)
    {
        await Call(l + "-d1-before");
        await Depth2(l);
        await Three(l + "-d1-after");
    }

    [Step]
    public async Task Depth2(string l)
    {
        await Call(l + "-d2-before");
        Depth3(l);
        await Three(l + "-d2-after");
    }

    [ThenStep]
    public void Depth3(string l)
    {
        CallSync(l + "-d3-before");
        SkipTask(l + "-d4-skip").GetAwaiter().GetResult();
        SkipSync(l + "-d4-skip2");
        SkipAsyncInt(l + "-d4-skip3").GetAwaiter().GetResult();
        Three(l + "-d3-after").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task B02_Bypass_at_depth_three_of_each_stub()
    {
        await GivenAsync("b02-seed");
        await Depth1("b02");
        await Three("b02-after");
        await ThenAsync("b02-then");
    }

    [Fact]
    public async Task B03_WhenAll_of_a_real_and_a_skipped_Given_inside_a_When_both_orders()
    {
        await GivenAsync("b03-seed");
        await WhenAllKids("b03");
        await ThenAsync("b03-then");
    }

    [WhenStep]
    public async Task WhenAllKids(string l)
    {
        await Task.WhenAll(GivenAsync(l + "-real1"), SkipAsync(l + "-skip1"));
        await Three(l + "-mid");
        await Task.WhenAll(SkipTask(l + "-skip2"), GivenAsync(l + "-real2"));
        await Three(l + "-after");
    }

    [Fact]
    public async Task B04_A_skipped_Given_on_a_forked_flow_inside_a_When()
    {
        await GivenAsync("b04-seed");
        await WhenForksSkip("b04");
        await ThenAsync("b04-then");
    }

    [WhenStep]
    public async Task WhenForksSkip(string l)
    {
        await Task.Run(() => SkipAsync(l + "-skip"));
        await Three(l + "-after-skip");
        await Task.Run(async () => { SkipSync(l + "-skip2"); await GivenAsync(l + "-fork-sub"); await Three(l + "-fork-after"); });
        await Three(l + "-after");
    }

    [Fact]
    public async Task B05_A_skip_with_a_sibling_left_running_at_the_top()
    {
        var gate = new TaskCompletionSource();
        var slow = GivenSlow("b05-slow", gate.Task);
        await SkipAsync("b05-skip");
        await GivenAsync("b05-seed");
        await WhenAsync("b05-act");
        gate.SetResult();
        await slow;
        await Three("b05-after");
        await ThenAsync("b05-then");
    }

    [GivenStep] public async Task GivenSlow(string l, Task gate) { await gate; await Call(l); }

    [Fact]
    public async Task B06_A_Parallel_For_of_skipped_and_real_inside_a_When()
    {
        await GivenAsync("b06-seed");
        WhenParallel("b06");
        await Three("b06-after");
        await ThenAsync("b06-then");
    }

    [WhenStep]
    public void WhenParallel(string l)
    {
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            if (i % 2 == 0) SkipSync($"{l}-skip{i}");
            else GivenSync($"{l}-real{i}");
        });
        Three(l + "-in-when-after").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task B07_A_getter_false_keeps_the_result()
    {
        await GivenAsync("b07-seed");
        var n = await WhenInt("b07");
        Assert.Equal(7, n);
        await ThenAsync("b07-then");
    }

    public bool No => false;
    [WhenStep(SkipIf = nameof(No))] public async Task<int> WhenInt(string l) { await Call(l); return 7; }
}

/// <summary>Hand-made bypass under woven steps, through both public overloads.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class HandBypass : Base
{
    [WhenStep]
    public async Task WhenHandKeywordAmbient(string l)
    {
        await Call(l + "-before");
        StepCollector.StartStep("Given", l + "-manual", null, null);
        StepCollector.BypassStep("manual");
        await Three(l + "-after");
        await GivenAsync(l + "-sub");
        await Three(l + "-after-sub");
    }

    [WhenStep]
    public async Task WhenHandKeywordById(string l)
    {
        await Call(l + "-before");
        StepCollector.StartStep(Id, "Given", l + "-manual", null, null);
        StepCollector.BypassStep(Id, "manual");
        await Three(l + "-after");
        await GivenAsync(l + "-sub");
        await Three(l + "-after-sub");
    }

    [GivenStep] public void SelfBypassSync(string l) { StepCollector.BypassStep("self"); CallSync(l); }
    [GivenStep] public async Task SelfBypassAsync(string l) { StepCollector.BypassStep("self"); await Call(l); }
    [GivenStep] public Task SelfBypassTask(string l) { StepCollector.BypassStep(Id, "self"); return Call(l); }
    [GivenStep] public async Task LateSelfBypass(string l) { await Task.Yield(); StepCollector.BypassStep(Id, "late"); await Call(l); }

    [WhenStep] public async Task WhenSelfSync(string l) { await Call(l + "-before"); SelfBypassSync(l + "-self"); await Three(l + "-after"); await GivenAsync(l + "-sub"); await Three(l + "-after-sub"); }
    [WhenStep] public async Task WhenSelfAsync(string l) { await Call(l + "-before"); await SelfBypassAsync(l + "-self"); await Three(l + "-after"); await GivenAsync(l + "-sub"); await Three(l + "-after-sub"); }
    [WhenStep] public async Task WhenSelfTask(string l) { await Call(l + "-before"); await SelfBypassTask(l + "-self"); await Three(l + "-after"); await GivenAsync(l + "-sub"); await Three(l + "-after-sub"); }
    [WhenStep] public async Task WhenLateSelf(string l) { await Call(l + "-before"); await LateSelfBypass(l + "-self"); await Three(l + "-after"); await GivenAsync(l + "-sub"); await Three(l + "-after-sub"); }

    [Fact] public async Task K01_Hand_Given_ambient_bypass_in_a_When() { await GivenAsync("k01-seed"); await WhenHandKeywordAmbient("k01"); await Three("k01-after-when"); await ThenAsync("k01-then"); }
    [Fact] public async Task K02_Hand_Given_by_id_bypass_in_a_When() { await GivenAsync("k02-seed"); await WhenHandKeywordById("k02"); await Three("k02-after-when"); await ThenAsync("k02-then"); }
    [Fact] public async Task K03_A_woven_step_bypasses_itself_sync_in_a_When() { await GivenAsync("k03-seed"); await WhenSelfSync("k03"); await Three("k03-after-when"); await ThenAsync("k03-then"); }
    [Fact] public async Task K04_A_woven_step_bypasses_itself_async_in_a_When() { await GivenAsync("k04-seed"); await WhenSelfAsync("k04"); await Three("k04-after-when"); await ThenAsync("k04-then"); }
    [Fact] public async Task K05_A_woven_step_bypasses_itself_Task_in_a_When() { await GivenAsync("k05-seed"); await WhenSelfTask("k05"); await Three("k05-after-when"); await ThenAsync("k05-then"); }
    [Fact] public async Task K06_A_woven_step_bypasses_itself_after_its_first_await_in_a_When() { await GivenAsync("k06-seed"); await WhenLateSelf("k06"); await Three("k06-after-when"); await ThenAsync("k06-then"); }

    [Fact]
    public async Task K07_Self_bypass_of_a_top_level_Given_then_When()
    {
        SelfBypassSync("k07-self");
        await Three("k07-after-self");
        await SelfBypassAsync("k07-self2");
        await Three("k07-after-self2");
        await WhenAsync("k07-act");
        await Three("k07-after-act");
    }

    [Fact]
    public async Task K08_Hand_Given_bypassed_on_another_flow_then_When()
    {
        StepCollector.StartStep(Id, "Given", "k08-hand", null, null);
        await Call("k08-in-hand");
        var id = Id;
        await Task.Run(() => StepCollector.BypassStep(id, "elsewhere"));
        await Three("k08-after-bypass");
        await WhenAsync("k08-act");
        await Three("k08-after-act");
    }

    [WhenStep]
    public async Task WhenHandBypassedElsewhere(string l)
    {
        var id = Id;
        await Call(l + "-before");
        StepCollector.StartStep(id, "Given", l + "-hand", null, null);
        await Task.Run(() => StepCollector.BypassStep(id, "elsewhere"));
        await Three(l + "-after");
        await GivenAsync(l + "-sub");
        await Three(l + "-after-sub");
    }

    [Fact] public async Task K09_Hand_Given_bypassed_on_another_flow_in_a_When() { await GivenAsync("k09-seed"); await WhenHandBypassedElsewhere("k09"); await Three("k09-after-when"); await ThenAsync("k09-then"); }

    [WhenStep]
    public async Task WhenHandPairOnFork(string l)
    {
        await Call(l + "-before");
        await Task.Run(() => { StepCollector.StartStep(null, l + "-manual", null, null); StepCollector.BypassStep("manual"); });
        await Three(l + "-after");
    }

    [Fact] public async Task K10_Hand_pair_on_a_fork_inside_a_When() { await GivenAsync("k10-seed"); await WhenHandPairOnFork("k10"); await ThenAsync("k10-then"); }

    [WhenStep]
    public async Task WhenHandBypassComplete(string l)
    {
        await Call(l + "-before");
        StepCollector.StartStep(null, l + "-manual", null, null);
        StepCollector.BypassStep("manual");
        StepCollector.CompleteStep(true);
        await Three(l + "-after");
        await GivenAsync(l + "-sub");
        await Three(l + "-after-sub");
    }

    [Fact] public async Task K11_Hand_bypass_then_a_completion_as_the_old_stub_did() { await GivenAsync("k11-seed"); await WhenHandBypassComplete("k11"); await Three("k11-after-when"); await ThenAsync("k11-then"); }

    [Fact]
    public async Task K12_Hand_When_bypassed_at_the_top_after_a_Given_then_real_Given_When()
    {
        await GivenAsync("k12-seed");
        StepCollector.StartStep(Id, "When", "k12-hand", null, null);
        StepCollector.BypassStep(Id, "n/a");
        await Three("k12-after-bypass");
        await GivenAsync("k12-seed2");
        await WhenAsync("k12-act");
        await Three("k12-after-act");
    }
}

/// <summary>T8 and T9: who completes what, and two tests' ids on one flow.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Chains : Base
{
    private const string Other = "some-other-test";

    [GivenStep]
    public void GivenHandClosedElsewhereSync(string l)
    {
        var id = Id;
        StepCollector.StartStep(id, "Given", l + "-hand", null, null);
        CallSync(l + "-in-hand");
        Task.Run(() => StepCollector.CompleteStep(id, true)).GetAwaiter().GetResult();
        CallSync(l + "-after-hand");
    }

    [Fact]
    public async Task C01_Sync_Given_with_a_hand_step_closed_on_another_flow_then_When()
    {
        GivenHandClosedElsewhereSync("c01");
        await Three("c01-after-given");
        await WhenAsync("c01-act");
        await Three("c01-after-act");
        await ThenAsync("c01-then");
    }

    [Fact]
    public async Task C02_Hand_Given_closed_on_another_flow_sync_wait_at_the_top_then_When_Then()
    {
        var id = Id;
        StepCollector.StartStep(id, "Given", "c02-hand", null, null);
        await Call("c02-seed");
        Task.Run(() => StepCollector.CompleteStep(id, true)).GetAwaiter().GetResult();
        await Three("c02-after-hand");
        await WhenAsync("c02-act");
        await Three("c02-after-act");
        await ThenAsync("c02-then");
        await Three("c02-after-then");
    }

    [WhenStep]
    public async Task WhenStrayComplete(string l)
    {
        await Call(l + "-before");
        StepCollector.CompleteStep(Id, true);
        await Three(l + "-after-stray");
        await GivenAsync(l + "-sub");
        await Three(l + "-after-sub");
    }

    [Fact]
    public async Task C03_A_stray_completion_closes_the_When_it_runs_in()
    {
        await GivenAsync("c03-seed");
        await WhenStrayComplete("c03");
        await Three("c03-after-when");
        await ThenAsync("c03-then");
    }

    [GivenStep]
    public async Task GivenAboveInner(string l)
    {
        await Call(l + "-before");
        await WhenInner(l);
        await Three(l + "-after-inner");
        await ThenAsync(l + "-label-after-inner");
        await Three(l + "-after-label");
    }

    [WhenStep]
    public async Task WhenInner(string l)
    {
        var id = Id;
        await Call(l + "-inner-before");
        // Two completions from one flow: the second closes this step on the stack while its body goes on.
        StepCollector.StartStep(id, "Given", l + "-hand", null, null);
        StepCollector.CompleteStep(id, true);
        StepCollector.CompleteStep(id, true);
        await Three(l + "-inner-after");
        await ThenAsync(l + "-inner-then");
        await Three(l + "-inner-after-then");
    }

    [Fact]
    public async Task C04_Two_completions_inside_a_nested_step_close_the_one_above()
    {
        await GivenAboveInner("c04");
        await Three("c04-after");
        await WhenAsync("c04-act");
        await Three("c04-after-act");
    }

    [WhenStep]
    public void WhenLeavesWorker(string l, TaskCompletionSource gate, List<Task> keep)
    {
        CallSync(l + "-before");
        keep.Add(GivenAwaiting(l + "-worker", gate));
        CallSync(l + "-after-worker-started");
    }

    [GivenStep] public async Task GivenAwaiting(string l, TaskCompletionSource gate) { await gate.Task; await Call(l); }

    [Fact]
    public async Task C05_A_sync_When_leaves_a_worker_step_running_then_a_marking_Then()
    {
        await GivenAsync("c05-seed");
        var keep = new List<Task>();
        var gate = new TaskCompletionSource();
        WhenLeavesWorker("c05", gate, keep);
        await Three("c05-after-when");
        await ThenAsync("c05-then");
        gate.SetResult();
        await Task.WhenAll(keep);
        await Three("c05-after-then");
    }

    // T9
    [GivenStep]
    public async Task GivenWithOtherIdOpenAcrossAKeyword(string l)
    {
        await Call(l + "-before");
        StepCollector.StartStep(Other, "Given", "other", null, null);
        await WhenAsync(l + "-label");
        StepCollector.CompleteStep(Other, true);
        await Three(l + "-after");
    }

    [Fact]
    public async Task I01_Other_id_open_across_a_keyword_inside_a_Given()
    {
        await GivenWithOtherIdOpenAcrossAKeyword("i01");
        await WhenAsync("i01-act");
        await Three("i01-after-act");
    }

    [GivenStep]
    public async Task GivenClosesTheOtherIdStartedBeforeIt(string l)
    {
        await Call(l + "-before");
        StepCollector.CompleteStep(Other, true);
        await WhenAsync(l + "-label");
        await Three(l + "-after-label");
        await ThenAsync(l + "-label2");
        await Three(l + "-after-label2");
    }

    [Fact]
    public async Task I02_A_step_for_another_id_started_before_a_Given_closes_inside_it()
    {
        StepCollector.StartStep(Other, "Given", "other", null, null);
        await GivenClosesTheOtherIdStartedBeforeIt("i02");
        await Three("i02-after");
        await WhenAsync("i02-act");
        await Three("i02-after-act");
    }

    [Fact]
    public async Task I03_Sync_hand_steps_of_two_ids_interleaved_on_one_flow()
    {
        var id = Id;
        StepCollector.StartStep(Other, "Given", "other", null, null);
        StepCollector.StartStep(id, "Given", "i03-mine", null, null);
        StepCollector.CompleteStep(Other, true);
        await WhenAsync("i03-label");
        await Three("i03-after-label");
        StepCollector.CompleteStep(id, true);
        await Three("i03-after-mine");
        await WhenAsync("i03-act");
        await Three("i03-after-act");
    }
}
