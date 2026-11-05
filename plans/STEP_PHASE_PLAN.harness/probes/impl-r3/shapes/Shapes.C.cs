using System.Reflection;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;
using Xunit.v3;
using TrackingDiagramOverride = Kronikol.BDDfy.xUnit3.TrackingDiagramOverride;

namespace Probe.Shapes;

/// <summary>The sync baseline of the index pairing: what 5.28.2 already does to a sync step.</summary>
public class Sync : Base
{
    public class ByNameSync(Calls c)
    {
        public void Setup() { c.Make("sync-setup").GetAwaiter().GetResult(); Track.Attachment(Known.Attachable("sync-setup"), "sync-setup"); Track.AssertionPassed("sync setup"); }
        public void GivenByName() { c.Make("sync-g").GetAwaiter().GetResult(); Track.Attachment(Known.Attachable("sync-g"), "sync-g"); Track.AssertionPassed("sync g"); }
        public void WhenByName() { c.Make("sync-w").GetAwaiter().GetResult(); Track.Attachment(Known.Attachable("sync-w"), "sync-w"); Track.AssertionPassed("sync w"); }
        public void ThenByName() { c.Make("sync-t").GetAwaiter().GetResult(); Track.Attachment(Known.Attachable("sync-t"), "sync-t"); Track.AssertionPassed("sync t"); }
        public void TearDown() { c.Make("sync-td").GetAwaiter().GetResult(); Track.Attachment(Known.Attachable("sync-td"), "sync-td"); Track.AssertionPassed("sync td"); }
    }

    [Fact] public void Y1_by_name_sync_with_setup_and_tear_down() => new ByNameSync(C).BDDfy();

    public void GivenSync(string l) { C.Make(l).GetAwaiter().GetResult(); Track.Attachment(Known.Attachable(l), l); }
    public void WhenSync(string l) { C.Make(l).GetAwaiter().GetResult(); Track.Attachment(Known.Attachable(l), l); }
    public void TearDownSync(string l) { C.Make(l).GetAwaiter().GetResult(); Track.Attachment(Known.Attachable(l), l); }

    [Fact] public void Y2_sync_tear_down_with_attachment() =>
        this.Given(x => x.GivenSync("y2-g")).When(x => x.WhenSync("y2-w")).TearDownWith(x => x.TearDownSync("y2-td")).BDDfy();

    private int _value;
    public void GivenEx(string l, int value) { C.Make($"{l}-{value}").GetAwaiter().GetResult(); Track.Attachment(Known.Attachable($"{l}-{value}"), $"{l}-{value}"); }

    [Fact] public void Y3_sync_examples_with_attachments() =>
        this.WithExamples(new ExampleTable("value") { 1, 2, 3 })
            .Given(x => x.GivenEx("y3-g", _value))
            .BDDfy();
}

/// <summary>Assorted shapes a suite has.</summary>
public class M : Base
{
    public void GivenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public void ThenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task GivenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task WhenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task ThenA(string l) { await Task.Yield(); await C.Make(l); }

    [Fact] public void M1_skip_if_on_the_story_step() =>
        this.Given(x => x.WovenSkipped("m1-g")).When(x => x.WhenA("m1-w")).Then(x => x.ThenA("m1-t")).BDDfy();

    public async Task WhenAttachesInWoven(string l) { await Task.Yield(); await WovenAttaches(l + "-sub"); }
    [GivenStep] public async Task WovenAttaches(string l) { await Task.Yield(); await C.Make(l); Track.Attachment(Known.Attachable(l), l); }

    [Fact] public void M2_attachment_in_a_woven_sub_step() =>
        this.Given(x => x.GivenS("m2-g")).When(x => x.WhenAttachesInWoven("m2-w")).Then(x => x.ThenS("m2-t")).BDDfy();

    public static IEnumerable<object[]> Rows() { yield return [1, "one"]; yield return [2, "two"]; }
    public async Task WhenRow(int n, string name) { await Task.Yield(); await C.Make($"m3-{name}"); Track.Attachment(Known.Attachable($"m3-{name}"), $"m3-{name}"); await WovenGiven($"m3-{name}-sub"); }

    [Theory, MemberData(nameof(Rows))] public void M3_theory_rows(int n, string name) =>
        this.Given(x => x.GivenS($"m3-g-{name}")).When(x => x.WhenRow(n, name)).BDDfy();

    public async Task WhenConfigureAwaitFalse(string l)
    {
        await Task.Delay(1).ConfigureAwait(false);
        await WovenGiven(l + "-sub").ConfigureAwait(false);
        await C.Make(l).ConfigureAwait(false);
        Track.AssertionPassed("cafalse " + l);
    }

    [Fact] public void M4_configure_await_false_all_the_way() =>
        this.Given(x => x.GivenS("m4-g")).When(x => x.WhenConfigureAwaitFalse("m4-w")).Then(x => x.ThenS("m4-t")).BDDfy();

    [Fact] public void M5_title_only_steps() =>
        this.Given("a title only").When(x => x.WhenA("m5-w")).Then("another title only").BDDfy();

    public Task WhenCompletedSync(string l) { WovenGivenSync(l + "-sub"); C.Make(l).GetAwaiter().GetResult(); return Task.CompletedTask; }

    [Fact] public void M6_a_step_that_returns_a_completed_task() =>
        this.Given(x => x.WhenCompletedSync("m6-g")).When(x => x.WhenCompletedSync("m6-w")).Then(x => x.ThenS("m6-t")).BDDfy();

    public Task WhenPassThrough(string l) => WovenWhen(l);

    [Fact] public void M7_a_step_that_returns_a_woven_steps_task() =>
        this.Given(x => x.GivenS("m7-g")).When(x => x.WhenPassThrough("m7-w")).Then(x => x.ThenS("m7-t")).BDDfy();

    public async Task GivenForksLateWork(string l)
    {
        await Task.Yield();
        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            Track.AssertionPassed("late assertion from " + l);
            Track.Attachment(Known.Attachable(l + "-late"), l + "-late");
            await C.Make(l + "-late");
        });
        await C.Make(l);
    }
    public async Task WhenWaits(string l) { await Task.Delay(80); await C.Make(l); }

    [Fact] public void M8_a_background_task_asserts_and_attaches_while_a_later_step_runs() =>
        this.Given(x => x.GivenForksLateWork("m8-g")).When(x => x.WhenWaits("m8-w")).Then(x => x.ThenA("m8-t")).BDDfy();

    // the story's steps run on a Task.Run: the whole story on another flow
    [Fact] public void M9_a_story_on_task_run() =>
        Task.Run(() => this.Given(x => x.GivenA("m9-g")).When(x => x.WhenA("m9-w")).Then(x => x.ThenA("m9-t")).BDDfy()).GetAwaiter().GetResult();

    // a Lazy story
    [Fact] public void M10_lazy_bddfy()
    {
        var engine = this.Given(x => x.GivenA("m10-g")).When(x => x.WhenA("m10-w")).LazyBDDfy();
        engine.Run();
    }

    // a retry of the whole story, as a suite's own helper does it
    private int _tries;
    public async Task WhenFlaky(string l) { var t = ++_tries; await Task.Yield(); await C.Make($"{l}-try{t}"); await WovenGiven($"{l}-sub{t}"); if (t == 1) throw new InvalidOperationException("flaky"); }
    public class Flaky(Func<Task> when)
    {
        public Task GivenNothing() => Task.CompletedTask;
        public Task WhenItRuns() => when();
    }

    [Fact] public void M11_a_retry_of_the_whole_story()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                new Flaky(() => WhenFlaky("m11-w")).Given(x => x.GivenNothing()).When(x => x.WhenItRuns()).BDDfy("attempt");
                return;
            }
            catch (InvalidOperationException) when (attempt < 2)
            {
            }
        }
    }

    // a tracked call in BeforeAfterTest hooks
    [Fact, TrackedHook] public void M12_before_after_test_hooks_around_an_async_story() =>
        this.Given(x => x.GivenA("m12-g")).When(x => x.WhenA("m12-w")).BDDfy();

    // cold task started later
    private Task? _cold;
    public Task GivenCold(string l)
    {
        _cold = new Task(() => C.Make(l).GetAwaiter().GetResult());
        var thread = new Thread(() => { Thread.Sleep(30); _cold.Start(); }) { IsBackground = true };
        thread.Start();
        return _cold;
    }

    [Fact] public void M13_a_cold_task_started_later() =>
        this.Given(x => x.GivenCold("m13-g")).When(x => x.WhenA("m13-w")).BDDfy();

    public Task WhenRun(string l) => Task.Run(() =>
    {
        Known.Note($"task.run body: sync context {SynchronizationContext.Current?.GetType().Name ?? "none"}");
        C.Make(l).GetAwaiter().GetResult();
    }, default);

    [Fact] public void M14_a_step_returns_task_run() =>
        this.Given(x => x.GivenS("m14-g")).When(x => x.WhenRun("m14-w")).Then(x => x.ThenS("m14-t")).BDDfy();

    // a hand-made step with a keyword of no phase, and a table of its own, after the first await
    public async Task WhenHandTable(string l)
    {
        await Task.Yield();
        StepCollector.StartStep(Id, "Then", "hand with a table", null, null, [["a", "b"], ["1", "2"]], "a doc string");
        await C.Make(l);
        StepCollector.CompleteStep(Id, true);
    }

    [Fact] public void M15_hand_step_with_a_table_after_the_first_await() =>
        this.Given(x => x.GivenS("m15-g")).When(x => x.WhenHandTable("m15-w")).Then(x => x.ThenS("m15-t")).BDDfy();

    // diagram insertions
    public async Task WhenInserts(string l)
    {
        await Task.Yield();
        TrackingDiagramOverride.InsertPlantUml("note over Probe: inserted " + l);
        await C.Make(l);
    }

    [Fact] public void M16_inserted_diagram_fragment_after_the_first_await() =>
        this.Given(x => x.GivenS("m16-g")).When(x => x.WhenInserts("m16-w")).Then(x => x.ThenS("m16-t")).BDDfy();

    // the test method reads the step's phase on its own flow after the story
    [Fact] public void M17_the_test_reads_its_phase_after_the_story()
    {
        this.Given(x => x.GivenA("m17-g")).When(x => x.WhenA("m17-w")).BDDfy();
        Known.Note($"phase after story: {TestPhaseContext.Current}");
        C.Make("m17-after").GetAwaiter().GetResult();
    }
}

public sealed class TrackedHookAttribute : BeforeAfterTestAttribute
{
    private static readonly Calls Hook = new(
        new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Hook", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Hook", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    public override void Before(MethodInfo methodUnderTest, IXunitTest test) => Hook.Make("hook-before").GetAwaiter().GetResult();
    public override void After(MethodInfo methodUnderTest, IXunitTest test) => Hook.Make("hook-after").GetAwaiter().GetResult();
}

/// <summary>A task that never ends, under xUnit's own time limit.</summary>
public class Never : Base
{
    private static readonly TaskCompletionSource Forever = new();
    public async Task GivenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task WhenNever(string l) { await Task.Yield(); await C.Make(l); await WovenGiven(l + "-sub"); Track.Attachment(Known.Attachable(l), l); await Forever.Task; }

    [Fact(Timeout = 800)] public async Task Z1_a_step_that_never_ends_under_a_test_time_limit()
    {
        await Task.Yield();
        this.Given(x => x.GivenA("z1-g")).When(x => x.WhenNever("z1-w")).Then(x => x.GivenA("z1-t")).BDDfy();
    }
}

/// <summary>Second round.</summary>
public class R2 : Base
{
    public void GivenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public void ThenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task GivenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task ThenA(string l) { await Task.Yield(); await C.Make(l); }

    // two sync stories at once: the sync baseline of At6
    public class MiniS(Calls c)
    {
        public void Slow(string l, int ms) { Thread.Sleep(ms); c.Make(l).GetAwaiter().GetResult(); }
    }

    [Fact] public async Task R1_two_sync_stories_at_once_in_one_test()
    {
        var a = Task.Run(() => new MiniS(C).Given(x => x.Slow("r1-a-g", 40)).When(x => x.Slow("r1-a-w", 40)).Then(x => x.Slow("r1-a-t", 40)).BDDfy("story a"));
        await Task.Delay(10);
        var b = Task.Run(() => new MiniS(C).Given(x => x.Slow("r1-b-g", 40)).When(x => x.Slow("r1-b-w", 40)).Then(x => x.Slow("r1-b-t", 40)).BDDfy("story b"));
        await Task.WhenAll(a, b);
    }

    // a failing Then in a by-name story with a Setup
    public class ByNameFails(Calls c)
    {
        public void Setup() { c.Make("bnf-setup").GetAwaiter().GetResult(); }
        public async Task GivenByName() { await Task.Yield(); await c.Make("bnf-g"); Track.AssertionPassed("bnf g"); }
        public async Task WhenByName() { await Task.Yield(); await c.Make("bnf-w"); Track.AssertionFailed("bnf soft failure in when", "soft"); }
        public async Task ThenByName() { await Task.Yield(); await c.Make("bnf-t"); Track.AssertionFailed("bnf then", "the real failure"); throw new InvalidOperationException("the real failure"); }
    }

    [Fact] public void R2_by_name_with_a_setup_and_a_failing_then() => new ByNameFails(C).BDDfy();

    // a step that awaits a Task.Run holding a woven step
    public async Task WhenAwaitsTaskRun(string l)
    {
        await Task.Yield();
        await Task.Run(async () => { await WovenGiven(l + "-in-run"); await C.Make(l + "-run-call"); });
        await C.Make(l + "-after");
    }

    [Fact] public void R3_a_step_awaits_a_task_run_with_a_woven_step() =>
        this.Given(x => x.GivenS("r3-g")).When(x => x.WhenAwaitsTaskRun("r3-w")).Then(x => x.ThenS("r3-t")).BDDfy();

    // a hand-made step that spans BDDfy steps
    public async Task GivenOpensSpan(string l) { await Task.Yield(); StepCollector.StartStep(Id, null, "a span", null, null); await C.Make(l); }
    public async Task ThenClosesSpan(string l) { await Task.Yield(); await C.Make(l); StepCollector.CompleteStep(Id, true); }

    [Fact] public void R4_a_hand_made_step_spans_story_steps() =>
        this.Given(x => x.GivenOpensSpan("r4-g")).When(x => x.ThenA("r4-w")).Then(x => x.ThenClosesSpan("r4-t")).BDDfy();

    // A When whose body fails after leaving a woven step that ends after the story
    private Task? _left;
    public async Task WhenLeavesAndFails(string l, Task gate) { await Task.Yield(); _left = WovenGated(l + "-left", gate); await C.Make(l); throw new InvalidOperationException(l); }

    [Fact] public void R5_a_step_fails_while_a_woven_step_it_left_is_running()
    {
        var gate = new TaskCompletionSource();
        try
        {
            this.Given(x => x.GivenS("r5-g")).When(() => WhenLeavesAndFails("r5-w", gate.Task), "a when").Then(x => x.ThenS("r5-t")).TearDownWith(x => x.ThenS("r5-td")).BDDfy();
        }
        finally
        {
            gate.SetResult();
            _left!.GetAwaiter().GetResult();
        }
    }
}

/// <summary>What the per-phase options drop that 5.28.2 kept.</summary>
public class Opt2 : Base
{
    private readonly HttpClient _noSetup = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = false }) { InnerHandler = new AnswerEverything() });
    private readonly HttpClient _noAction = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringAction = false }) { InnerHandler = new AnswerEverything() });

    [GivenStep] public async Task WovenGivenA(string l) { await _noAction.GetAsync($"http://probe.local/{l}"); }
    [WhenStep] public async Task WovenWhenN(string l) { await _noSetup.GetAsync($"http://probe.local/{l}"); }
    public async Task WhenWithGiven(string l) { await Task.Yield(); await _noAction.GetAsync($"http://probe.local/{l}-before"); await WovenGivenA(l + "-sub"); await _noAction.GetAsync($"http://probe.local/{l}-after"); }
    public async Task GivenWithWhen(string l) { await Task.Yield(); await _noSetup.GetAsync($"http://probe.local/{l}-before"); await WovenWhenN(l + "-sub"); await _noSetup.GetAsync($"http://probe.local/{l}-after"); }
    public async Task ThenN(string l) { await Task.Yield(); await _noSetup.GetAsync($"http://probe.local/{l}"); }

    [Fact] public void O4_track_during_action_off_with_a_woven_given_under_an_async_when() =>
        this.Given(x => x.ThenN("o4-g")).When(x => x.WhenWithGiven("o4-w")).Then(x => x.ThenN("o4-t")).BDDfy();
    [Fact] public void O5_track_during_setup_off_with_a_woven_when_under_an_async_given() =>
        this.Given(x => x.GivenWithWhen("o5-g")).When(x => x.ThenN("o5-w")).Then(x => x.ThenN("o5-t")).BDDfy();
}

/// <summary>A class fixture with a shared tracked client, and a step whose first await is a woven step.</summary>
public sealed class SharedApi : IAsyncLifetime
{
    public readonly Calls C = new(
        new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Fixture", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Fixture", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    public async ValueTask InitializeAsync()
    {
        // Tracked with no test to belong to, as a fixture's warm-up call is.
        try { await C.Make("fixture-warm-up"); } catch (Exception) { }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class WithFixture(SharedApi api) : Base, IClassFixture<SharedApi>
{
    public async Task GivenF(string l) { await Task.Yield(); await api.C.Make(l); }
    [GivenStep] public async Task WovenF(string l) { await api.C.Make(l); }
    public async Task WhenWovenFirst(string l) { await WovenF(l + "-1"); await WovenF(l + "-2"); await api.C.Make(l + "-after"); }

    [Fact] public void F1_a_shared_client_on_a_class_fixture() =>
        this.Given(x => x.GivenF("f1-g")).When(x => x.WhenWovenFirst("f1-w")).Then(x => x.GivenF("f1-t")).BDDfy();

    [Fact] public void F2_a_second_test_on_the_same_fixture() =>
        this.Given(x => x.GivenF("f2-g")).When(x => x.WhenWovenFirst("f2-w")).Then(x => x.GivenF("f2-t")).BDDfy();
}

/// <summary>A woven step that outlives the test, with what the story's steps hold.</summary>
public class Outlive : Base
{
    private static readonly TaskCompletionSource NeverSet = new();
    private void Attach(string l) => Track.Attachment(Known.Attachable(l), l);
    public async Task GivenLeaves(string l) { await Task.Yield(); Attach(l + "-before-left"); Track.AssertionPassed("given " + l); _ = WovenGated(l + "-left", NeverSet.Task); await C.Make(l); }
    public async Task WhenHolds(string l) { await Task.Yield(); await C.Make(l); Attach(l); Track.AssertionPassed("when " + l); await WovenGiven(l + "-sub"); }
    public async Task ThenHolds(string l) { await Task.Yield(); await C.Make(l); Attach(l); Track.AssertionFailed("then " + l, "the failure"); throw new InvalidOperationException("the failure"); }

    [Fact] public void L10_a_woven_step_outlives_the_test_and_the_steps_hold_attachments() =>
        this.Given(x => x.GivenLeaves("l10-g")).When(x => x.WhenHolds("l10-w")).Then(x => x.ThenHolds("l10-t")).BDDfy();
}
