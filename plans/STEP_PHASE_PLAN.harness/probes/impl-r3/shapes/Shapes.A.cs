using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;
using TrackingDiagramOverride = Kronikol.BDDfy.xUnit3.TrackingDiagramOverride;

namespace Probe.Shapes;

/// <summary>The overloads and the outcomes.</summary>
public class A : Base
{
    public async Task GivenA(string l) { await C.Make(l + "-1"); await Task.Yield(); await C.Make(l + "-2"); }
    public async Task WhenA(string l) { await Task.Yield(); await C.Make(l + "-before"); await WovenGiven(l + "-sub"); await C.Make(l + "-after"); }
    public async Task ThenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task<int> WhenT(string l) { await Task.Yield(); await WovenGiven(l + "-sub"); await C.Make(l); return 42; }
    public void GivenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public void WhenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public void ThenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public async void WhenVoid(string l) { await Task.Yield(); await WovenGiven(l + "-sub"); await C.Make(l); Track.Attachment(Known.Attachable(l), l); Track.AssertionPassed("void " + l); }
    public async ValueTask WhenValueTask(string l) { await Task.Yield(); await WovenGiven(l + "-sub"); await C.Make(l); }

    [Fact] public void A01_expression_async_steps() =>
        this.Given(x => x.GivenA("a01-g")).When(x => x.WhenA("a01-w")).Then(x => x.ThenA("a01-t")).BDDfy();

    [Fact] public void A02_func_task_with_title() =>
        this.Given(() => GivenA("a02-g"), "a given").When(() => WhenA("a02-w"), "a when").Then(() => ThenA("a02-t"), "a then").BDDfy();

    [Fact] public void A03_task_of_t() =>
        this.Given(x => x.GivenS("a03-g")).When(x => x.WhenT("a03-w")).Then(x => x.ThenS("a03-t")).BDDfy();

    [Fact] public void A05_async_void_step() =>
        this.Given(x => x.GivenS("a05-g")).When(x => x.WhenVoid("a05-w")).Then(x => x.ThenS("a05-t")).BDDfy();

    [Fact] public void A06_value_task_step()
    {
        this.Given(x => x.GivenS("a06-g")).When(x => x.WhenValueTask("a06-w")).Then(x => x.ThenS("a06-t")).BDDfy();
        Thread.Sleep(100);
    }

    public Task WhenOtherThread(string l)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() => { Thread.Sleep(30); tcs.SetResult(); }) { IsBackground = true };
        thread.Start();
        return tcs.Task;
    }

    [Fact] public void A07_task_ends_on_another_thread() =>
        this.Given(x => x.GivenS("a07-g")).When(x => x.WhenOtherThread("a07-w")).Then(x => x.ThenS("a07-t")).BDDfy();

    private Task? _shared;
    public void GivenStarts(string l) => _shared = Work(l);
    private async Task Work(string l) { await Task.Delay(20); await C.Make(l); }
    public Task WhenReturnsShared() => _shared!;

    [Fact] public void A08_a_step_returns_a_task_an_earlier_step_started() =>
        this.Given(x => x.GivenStarts("a08-bg")).When(x => x.WhenReturnsShared()).Then(x => x.ThenS("a08-t")).BDDfy();

    public async Task WhenThrowsBefore(string l) { throw new InvalidOperationException("before " + l); }
    public async Task WhenThrowsAfter(string l) { await C.Make(l); await WovenGiven(l + "-sub"); await Task.Yield(); throw new InvalidOperationException("after " + l); }
    public Task WhenSyncThrow(string l) => throw new InvalidOperationException("sync " + l);
    public Task WhenFromException(string l) => Task.FromException(new InvalidOperationException("from " + l));
    public async Task WhenCancelled(string l) { await Task.Yield(); throw new OperationCanceledException("cancelled " + l); }
    public Task WhenFromCanceled(string l) => Task.FromCanceled(new CancellationToken(true));
    public async Task WhenAllFails(string l)
    {
        async Task Fail(string m) { await Task.Yield(); throw new InvalidOperationException(m); }
        await Task.WhenAll(Fail(l + "-1"), Fail(l + "-2"));
    }
    public Task WhenAllFailsRaw(string l)
    {
        async Task Fail(string m) { await Task.Yield(); throw new InvalidOperationException(m); }
        return Task.WhenAll(Fail(l + "-1"), Fail(l + "-2"));
    }
    public async Task WhenNotImplemented(string l) { await Task.Yield(); throw new NotImplementedException(l); }
    public async Task WhenSkip(string l) { await Task.Yield(); Assert.Skip("skipped " + l); }
    public async Task WhenAggregate(string l) { await Task.Yield(); throw new AggregateException(new InvalidOperationException(l + "-1"), new ArgumentException(l + "-2")); }
    public async Task WhenWovenThrows(string l) { await Task.Yield(); await WovenThrows(l + "-sub"); }

    [Fact] public void A09_throws_before_first_await() =>
        this.Given(x => x.GivenS("a09-g")).When(x => x.WhenThrowsBefore("a09-w")).Then(x => x.ThenS("a09-t")).BDDfy();
    [Fact] public void A10_throws_after_first_await() =>
        this.Given(x => x.GivenS("a10-g")).When(x => x.WhenThrowsAfter("a10-w")).Then(x => x.ThenS("a10-t")).BDDfy();
    [Fact] public void A11_func_task_throws_synchronously() =>
        this.Given(x => x.GivenS("a11-g")).When(x => x.WhenSyncThrow("a11-w")).Then(x => x.ThenS("a11-t")).BDDfy();
    [Fact] public void A12_task_from_exception() =>
        this.Given(x => x.GivenS("a12-g")).When(x => x.WhenFromException("a12-w")).Then(x => x.ThenS("a12-t")).BDDfy();
    [Fact] public void A13a_cancelled_after_await() =>
        this.Given(x => x.GivenS("a13a-g")).When(x => x.WhenCancelled("a13a-w")).Then(x => x.ThenS("a13a-t")).BDDfy();
    [Fact] public void A13b_task_from_canceled() =>
        this.Given(x => x.GivenS("a13b-g")).When(x => x.WhenFromCanceled("a13b-w")).Then(x => x.ThenS("a13b-t")).BDDfy();
    [Fact] public void A14a_when_all_two_failures_awaited() =>
        this.Given(x => x.GivenS("a14a-g")).When(x => x.WhenAllFails("a14a-w")).Then(x => x.ThenS("a14a-t")).BDDfy();
    [Fact] public void A14b_when_all_two_failures_returned() =>
        this.Given(x => x.GivenS("a14b-g")).When(x => x.WhenAllFailsRaw("a14b-w")).Then(x => x.ThenS("a14b-t")).BDDfy();
    [Fact] public void A14c_aggregate_thrown() =>
        this.Given(x => x.GivenS("a14c-g")).When(x => x.WhenAggregate("a14c-w")).Then(x => x.ThenS("a14c-t")).BDDfy();
    [Fact] public void A15_not_implemented_after_await() =>
        this.Given(x => x.GivenS("a15-g")).When(x => x.WhenNotImplemented("a15-w")).Then(x => x.ThenS("a15-t")).BDDfy();
    [Fact] public void A16_assert_skip_after_await() =>
        this.Given(x => x.GivenS("a16-g")).When(x => x.WhenSkip("a16-w")).Then(x => x.ThenS("a16-t")).BDDfy();
    [Fact] public void A16b_woven_sub_step_throws() =>
        this.Given(x => x.GivenS("a16b-g")).When(x => x.WhenWovenThrows("a16b-w")).Then(x => x.ThenS("a16b-t")).BDDfy();

    public async Task ThenFails(string l) { await Task.Yield(); await C.Make(l); Track.AssertionFailed("then " + l, "expected"); throw new InvalidOperationException("then " + l); }
    public async Task AndFollows(string l) { await Task.Yield(); await C.Make(l); Track.AssertionPassed("and " + l); }

    [Fact] public void A17_a_failing_then_and_the_asserting_step_after_it() =>
        this.Given(x => x.GivenS("a17-g")).When(x => x.WhenS("a17-w")).Then(x => x.ThenFails("a17-t")).And(x => x.AndFollows("a17-and")).BDDfy();

    // a woven step used directly as the BDDfy step
    [Fact] public void A19a_woven_step_as_the_story_step() =>
        this.Given(x => x.WovenGiven("a19a-g")).When(x => x.WovenWhen("a19a-w")).Then(x => x.WovenThen("a19a-t")).BDDfy();
    [Fact] public void A19b_failing_woven_step_as_the_story_step() =>
        this.Given(x => x.WovenGiven("a19b-g")).When(x => x.WovenThrows("a19b-w")).Then(x => x.WovenThen("a19b-t")).BDDfy();
}

/// <summary>TearDownWith, in both of the places BDDfy runs it.</summary>
public class T : Base
{
    public void GivenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public void WhenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task WhenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task WhenFails(string l) { await Task.Yield(); await C.Make(l); throw new InvalidOperationException("when " + l); }

    public async Task TearDownA(string l)
    {
        await C.Make(l + "-before");
        await Task.Yield();
        await C.Make(l + "-after");
        await WovenGiven(l + "-sub");
        Track.Attachment(Known.Attachable(l + "-td"), l + "-td");
        Track.AssertionPassed("teardown " + l);
    }
    public void TearDownS(string l)
    {
        C.Make(l).GetAwaiter().GetResult();
        Track.Attachment(Known.Attachable(l + "-td"), l + "-td");
    }
    public async Task TearDownThrows(string l) { await Task.Yield(); await C.Make(l); throw new InvalidOperationException("teardown " + l); }

    [Fact] public void T1_async_tear_down_after_a_pass() =>
        this.Given(x => x.GivenS("t1-g")).When(x => x.WhenA("t1-w")).TearDownWith(x => x.TearDownA("t1")).BDDfy();
    [Fact] public void T2_async_tear_down_after_a_failure() =>
        this.Given(x => x.GivenS("t2-g")).When(x => x.WhenFails("t2-w")).TearDownWith(x => x.TearDownA("t2")).BDDfy();
    [Fact] public void T3_async_tear_down_throws() =>
        this.Given(x => x.GivenS("t3-g")).When(x => x.WhenA("t3-w")).TearDownWith(x => x.TearDownThrows("t3")).BDDfy();
    [Fact] public void T4_async_tear_down_throws_after_a_failure() =>
        this.Given(x => x.GivenS("t4-g")).When(x => x.WhenFails("t4-w")).TearDownWith(x => x.TearDownThrows("t4")).BDDfy();
    [Fact] public void T5_sync_tear_down_attachment() =>
        this.Given(x => x.GivenS("t5-g")).When(x => x.WhenA("t5-w")).TearDownWith(x => x.TearDownS("t5")).BDDfy();
}

/// <summary>What lands on the step once it stays open: attachments and assertions.</summary>
public class At : Base
{
    private void Attach(string l) => Track.Attachment(Known.Attachable(l), l);

    public async Task GivenAt(string l) { Attach(l + "-early"); await Task.Yield(); await C.Make(l); Attach(l + "-late"); Track.AssertionPassed("given " + l); }
    public async Task WhenAt(string l) { Attach(l + "-early"); await Task.Yield(); await C.Make(l); Attach(l + "-late"); Track.AssertionPassed("when " + l); }
    public async Task ThenAt(string l) { Attach(l + "-early"); await Task.Yield(); await C.Make(l); Attach(l + "-late"); Track.AssertionPassed("then " + l); Track.AssertionFailed("then failed " + l, "soft"); }

    [Fact] public void At1_attachments_and_assertions_after_the_first_await() =>
        this.Given(x => x.GivenAt("at1-g")).When(x => x.WhenAt("at1-w")).Then(x => x.ThenAt("at1-t")).BDDfy();

    public void ThenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task WhenFailsAt(string l) { await Task.Yield(); Attach(l + "-late"); Track.AssertionFailed("when failed " + l, "boom"); throw new InvalidOperationException(l); }

    [Fact] public void At2_attachment_on_a_step_that_fails_after_the_first_await() =>
        this.Given(x => x.GivenAt("at2-g")).When(x => x.WhenFailsAt("at2-w")).Then(x => x.ThenS("at2-t")).BDDfy();

    // Examples: one test, several BDDfy scenarios
    private int _value;
    public async Task GivenEx(string l, int value) { await Task.Yield(); await C.Make($"{l}-{value}"); Attach($"{l}-{value}"); Track.AssertionPassed($"ex {l} {value}"); }

    [Fact] public void At3_examples_with_attachments_after_the_first_await() =>
        this.WithExamples(new ExampleTable("value") { 1, 2, 3 })
            .Given(x => x.GivenEx("at3-g", _value))
            .When(x => x.GivenEx("at3-w", _value))
            .BDDfy();

    // Two stories in one test, one after the other
    public class Mini(Calls c)
    {
        public async Task GivenM(string l) { await Task.Yield(); await c.Make(l); Track.Attachment(Known.Attachable(l), l); Track.AssertionPassed("m " + l); }
        public async Task WhenM(string l) { await Task.Yield(); await c.Make(l); Track.Attachment(Known.Attachable(l), l); Track.AssertionPassed("m " + l); }
        public async Task WhenMFails(string l) { await Task.Yield(); await c.Make(l); throw new InvalidOperationException(l); }
        public async Task Slow(string l, int ms) { await Task.Delay(ms); await c.Make(l); }
    }

    [Fact] public void At4_two_stories_one_after_the_other()
    {
        new Mini(C).Given(x => x.GivenM("at4-a-g")).When(x => x.WhenM("at4-a-w")).BDDfy("first story");
        new Mini(C).Given(x => x.GivenM("at4-b-g")).When(x => x.WhenM("at4-b-w")).BDDfy("second story");
    }

    [Fact] public void At5_two_stories_the_second_fails()
    {
        new Mini(C).Given(x => x.GivenM("at5-a-g")).When(x => x.WhenM("at5-a-w")).BDDfy("first story");
        new Mini(C).Given(x => x.GivenM("at5-b-g")).When(x => x.WhenMFails("at5-b-w")).BDDfy("second story");
    }

    [Fact] public async Task At6_two_stories_at_once_in_one_test()
    {
        var a = Task.Run(() => new Mini(C).Given(x => x.Slow("at6-a-g", 40)).When(x => x.Slow("at6-a-w", 40)).Then(x => x.Slow("at6-a-t", 40)).BDDfy("story a"));
        await Task.Delay(10);
        var b = Task.Run(() => new Mini(C).Given(x => x.Slow("at6-b-g", 40)).When(x => x.Slow("at6-b-w", 40)).Then(x => x.Slow("at6-b-t", 40)).BDDfy("story b"));
        await Task.WhenAll(a, b);
    }

    // by name
    public class ByName(Calls c)
    {
        public async Task GivenByName() { await Task.Yield(); await c.Make("byname-g"); Track.Attachment(Known.Attachable("byname-g"), "byname-g"); Track.AssertionPassed("byname g"); }
        public async Task WhenByName() { await Task.Yield(); await c.Make("byname-w"); Track.Attachment(Known.Attachable("byname-w"), "byname-w"); Track.AssertionPassed("byname w"); }
        public async Task ThenByName() { await Task.Yield(); await c.Make("byname-t"); Track.Attachment(Known.Attachable("byname-t"), "byname-t"); Track.AssertionPassed("byname t"); }
        public async Task AndAnother() { await Task.Yield(); await c.Make("byname-and"); Track.Attachment(Known.Attachable("byname-and"), "byname-and"); Track.AssertionPassed("byname and"); }
    }

    [Fact] public void N1_by_name() => new ByName(C).BDDfy();

    public class ByNameWithSetup(Calls c)
    {
        public async Task Setup() { await Task.Yield(); await c.Make("bns-setup"); Track.Attachment(Known.Attachable("bns-setup"), "bns-setup"); Track.AssertionPassed("bns setup"); }
        public async Task GivenByName() { await Task.Yield(); await c.Make("bns-g"); Track.Attachment(Known.Attachable("bns-g"), "bns-g"); Track.AssertionPassed("bns g"); }
        public async Task WhenByName() { await Task.Yield(); await c.Make("bns-w"); Track.Attachment(Known.Attachable("bns-w"), "bns-w"); Track.AssertionPassed("bns w"); }
        public async Task ThenByName() { await Task.Yield(); await c.Make("bns-t"); Track.Attachment(Known.Attachable("bns-t"), "bns-t"); Track.AssertionPassed("bns t"); }
        public async Task TearDown() { await Task.Yield(); await c.Make("bns-td"); Track.Attachment(Known.Attachable("bns-td"), "bns-td"); Track.AssertionPassed("bns td"); }
    }

    [Fact] public void N2_by_name_with_setup_and_tear_down() => new ByNameWithSetup(C).BDDfy();

    public class ByNameSyncSetup(Calls c)
    {
        public void Setup() { c.Make("bss-setup").GetAwaiter().GetResult(); }
        public async Task GivenByName() { await Task.Yield(); await c.Make("bss-g"); Track.Attachment(Known.Attachable("bss-g"), "bss-g"); Track.AssertionPassed("bss g"); }
        public async Task WhenByName() { await Task.Yield(); await c.Make("bss-w"); Track.Attachment(Known.Attachable("bss-w"), "bss-w"); Track.AssertionPassed("bss w"); }
        public async Task ThenByName() { await Task.Yield(); await c.Make("bss-t"); Track.Attachment(Known.Attachable("bss-t"), "bss-t"); Track.AssertionPassed("bss t"); }
    }

    [Fact] public void N3_by_name_with_a_sync_setup() => new ByNameSyncSetup(C).BDDfy();

    public class AttributeStory(Calls c)
    {
        [Given] public async Task SomeGiven() { await Task.Yield(); await c.Make("attr-g"); Track.Attachment(Known.Attachable("attr-g"), "attr-g"); }
        [When] public async Task SomeWhen() { await Task.Yield(); await c.Make("attr-w"); Track.Attachment(Known.Attachable("attr-w"), "attr-w"); }
        [Then] public async Task SomeThen() { await Task.Yield(); await c.Make("attr-t"); Track.Attachment(Known.Attachable("attr-t"), "attr-t"); }
    }

    [Fact] public void N4_attribute_steps() => new AttributeStory(C).BDDfy();

    // Step durations
    public async Task WhenSlow(string l) { await Task.Delay(300); await C.Make(l); await WovenSlow(l + "-sub", 300); }

    [Fact] public void D1_a_slow_async_step() =>
        this.Given(x => x.ThenS("d1-g")).When(x => x.WhenSlow("d1-w")).Then(x => x.ThenS("d1-t")).BDDfy();
}
