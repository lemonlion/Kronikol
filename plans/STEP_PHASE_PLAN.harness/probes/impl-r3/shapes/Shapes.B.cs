using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;
using TrackingDiagramOverride = Kronikol.BDDfy.xUnit3.TrackingDiagramOverride;

namespace Probe.Shapes;

/// <summary>Woven steps and assertions under an async step, and work left running.</summary>
public class W : Base
{
    public void GivenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public void ThenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task GivenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task ThenA(string l) { await Task.Yield(); await C.Make(l); }

    public async Task WhenManySub(string l)
    {
        await Task.Yield();
        WovenGivenSync(l + "-sync");
        await WovenGiven(l + "-async");
        await WovenGivenT(l + "-t");
        await WovenSkipped(l + "-skip");
        await WovenWhen(l + "-when");
        await C.Make(l + "-after");
        Track.AssertionPassed("when sub " + l);
    }

    [Fact] public void W1_woven_sub_steps_of_every_shape_after_the_first_await() =>
        this.Given(x => x.GivenS("w1-g")).When(x => x.WhenManySub("w1-w")).Then(x => x.ThenS("w1-t")).BDDfy();

    public async Task WhenCatchesFailure(string l)
    {
        await Task.Yield();
        try { await WovenThrows(l + "-sub"); } catch (InvalidOperationException) { }
        await C.Make(l + "-after");
    }

    [Fact] public void W2_a_woven_sub_step_fails_and_the_step_goes_on() =>
        this.Given(x => x.GivenS("w2-g")).When(x => x.WhenCatchesFailure("w2-w")).Then(x => x.ThenS("w2-t")).BDDfy();

    public async Task WhenWhenAll(string l)
    {
        await Task.Yield();
        await Task.WhenAll(WovenSlow(l + "-a", 60), WovenSlow(l + "-b", 20));
        await C.Make(l + "-after");
    }

    [Fact] public void W3_two_woven_sub_steps_at_once() =>
        this.Given(x => x.GivenS("w3-g")).When(x => x.WhenWhenAll("w3-w")).Then(x => x.ThenS("w3-t")).BDDfy();

    private Task? _left;

    public async Task GivenLeavesBeforeAwait(string l) { _left = WovenSlow(l + "-bg", 80); await Task.Yield(); await C.Make(l); }
    public async Task GivenLeavesAfterAwait(string l) { await Task.Yield(); _left = WovenSlow(l + "-bg", 80); await C.Make(l); }
    public async Task WhenWaits(string l) { await Task.Delay(150); await C.Make(l); }

    // left running, ending within the story
    [Fact] public void L1_a_woven_step_left_running_before_the_first_await() =>
        this.Given(x => x.GivenLeavesBeforeAwait("l1-g")).When(x => x.WhenWaits("l1-w")).Then(x => x.ThenA("l1-t")).BDDfy();

    [Fact] public void L2_a_woven_step_left_running_after_the_first_await() =>
        this.Given(x => x.GivenLeavesAfterAwait("l2-g")).When(x => x.WhenWaits("l2-w")).Then(x => x.ThenA("l2-t")).BDDfy();

    // left running past the story's end
    [Fact] public void L3_left_running_past_the_end_of_the_story()
    {
        var gate = new TaskCompletionSource();
        this.Given(() => LeaveGated("l3-g", gate.Task), "a given").When(x => x.WhenWaits("l3-w")).Then(x => x.ThenA("l3-t")).BDDfy();
        gate.SetResult();
        _left!.GetAwaiter().GetResult();
    }
    private async Task LeaveGated(string l, Task gate) { await Task.Yield(); _left = WovenGated(l + "-bg", gate); await C.Make(l); }

    // left running for the rest of the run
    private static readonly TaskCompletionSource NeverSet = new();
    [Fact] public void L4_left_running_past_the_end_of_the_test() =>
        this.Given(() => LeaveGated("l4-g", NeverSet.Task), "a given").When(x => x.WhenWaits("l4-w")).Then(x => x.ThenA("l4-t")).BDDfy();

    // A background service: a loop of woven steps on a flow of its own, for the whole story
    public async Task GivenStartsLoop(string l, CancellationToken stop)
    {
        await Task.Yield();
        _left = Task.Run(async () =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested && i < 200)
            {
                await WovenWhen($"{l}-loop-{i++}");
                await Task.Delay(15);
            }
        });
        await C.Make(l);
    }

    [Fact] public void L5_a_background_loop_of_woven_steps()
    {
        using var stop = new CancellationTokenSource();
        this.Given(() => GivenStartsLoop("l5-g", stop.Token), "a given starts a loop")
            .When(x => x.WhenWaits("l5-w"))
            .Then(x => x.ThenA("l5-t"))
            .BDDfy();
        stop.Cancel();
        _left!.GetAwaiter().GetResult();
    }

    // Timeouts
    public async Task WhenTimesOutInWhenAny(string l)
    {
        await Task.Yield();
        await Task.WhenAny(WovenSlow(l + "-slow", 100), Task.Delay(10));
        await C.Make(l + "-after");
    }

    [Fact] public void L6_when_any_leaves_a_woven_step_running()
    {
        this.Given(x => x.GivenS("l6-g")).When(x => x.WhenTimesOutInWhenAny("l6-w")).Then(x => x.ThenA("l6-t")).BDDfy();
        Thread.Sleep(150);
    }

    public async Task WhenWaitAsyncTimesOut(string l)
    {
        await Task.Yield();
        await WovenSlow(l + "-slow", 100).WaitAsync(TimeSpan.FromMilliseconds(10));
    }

    [Fact] public void L7_wait_async_times_out_and_the_step_fails()
    {
        try
        {
            this.Given(x => x.GivenS("l7-g")).When(x => x.WhenWaitAsyncTimesOut("l7-w")).Then(x => x.ThenA("l7-t")).TearDownWith(x => x.ThenS("l7-td")).BDDfy();
        }
        finally
        {
            Thread.Sleep(150);
        }
    }

    // A When starts an operation and a Then waits for it
    public async Task WhenStarts(string l) { await Task.Yield(); await C.Make(l); _left = WovenSlow(l + "-op", 40); }
    public async Task ThenAwaitsIt(string l) { await _left!; await C.Make(l); }

    [Fact] public void L8_a_when_starts_it_and_a_then_awaits_it() =>
        this.Given(x => x.GivenS("l8-g")).When(x => x.WhenStarts("l8-w")).Then(x => x.ThenAwaitsIt("l8-t")).BDDfy();

    // Fire and forget, no woven step, only calls
    public async Task GivenFiresCalls(string l) { await Task.Yield(); _left = Task.Run(async () => { await Task.Delay(40); await C.Make(l + "-bg"); }); await C.Make(l); }

    [Fact] public void L9_calls_left_running_keep_their_phase()
    {
        this.Given(x => x.GivenFiresCalls("l9-g")).When(x => x.WhenWaits("l9-w")).Then(x => x.ThenA("l9-t")).BDDfy();
        _left!.GetAwaiter().GetResult();
    }
}

/// <summary>AsyncVoidSupportEnabled off.</summary>
public class Off : Base
{
    public async Task GivenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task WhenA(string l) { await Task.Yield(); await C.Make(l + "-before"); await WovenGiven(l + "-sub"); await C.Make(l + "-after"); Track.Attachment(Known.Attachable(l), l); Track.AssertionPassed("off " + l); }
    public async Task ThenA(string l) { await Task.Yield(); await C.Make(l); }

    [Fact] public void O1_flag_off()
    {
        Configurator.AsyncVoidSupportEnabled = false;
        try
        {
            this.Given(x => x.GivenA("o1-g")).When(x => x.WhenA("o1-w")).Then(x => x.ThenA("o1-t")).BDDfy();
            Thread.Sleep(200);
        }
        finally
        {
            Configurator.AsyncVoidSupportEnabled = true;
        }
    }
}

/// <summary>Stories under other things.</summary>
public class Nest : Base
{
    public async Task GivenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task WhenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task ThenA(string l) { await Task.Yield(); await C.Make(l); }

    [WhenStep] public async Task OuterWhen(string l)
    {
        await Task.Yield();
        await C.Make(l + "-before");
        this.Given(x => x.GivenA(l + "-g")).When(x => x.WhenA(l + "-w")).Then(x => x.ThenA(l + "-t")).BDDfy();
        await C.Make(l + "-after");
    }

    [Fact] public async Task S1_a_story_under_a_woven_step() => await OuterWhen("s1");

    public class Inner(Calls c)
    {
        public async Task GivenI(string l) { await Task.Yield(); await c.Make(l); }
        public async Task WhenI(string l) { await Task.Yield(); await c.Make(l); }
    }

    public async Task WhenRunsStory(string l)
    {
        await Task.Yield();
        await C.Make(l + "-before");
        new Inner(C).Given(x => x.GivenI(l + "-ig")).When(x => x.WhenI(l + "-iw")).BDDfy("inner story");
        await C.Make(l + "-after");
    }

    [Fact] public void S2_a_story_under_an_async_story_step() =>
        this.Given(x => x.GivenA("s2-g")).When(x => x.WhenRunsStory("s2-w")).Then(x => x.ThenA("s2-t")).BDDfy();

    [Fact] public async Task S3_story_after_an_await_in_an_async_test()
    {
        await Task.Yield();
        await C.Make("s3-test-before");
        this.Given(x => x.GivenA("s3-g")).When(x => x.WhenA("s3-w")).Then(x => x.ThenA("s3-t")).BDDfy();
        await C.Make("s3-test-after");
    }
}

/// <summary>Hooks and lifecycle methods around an async story.</summary>
public class Life : Base, IAsyncLifetime
{
    public async ValueTask InitializeAsync() { await Task.Yield(); await C.Make(Name() + "-init"); }
    public async ValueTask DisposeAsync() { await Task.Yield(); await C.Make(Name() + "-dispose"); }
    private static string Name() => Xunit.TestContext.Current.TestMethod!.MethodName[..3].ToLowerInvariant();

    public async Task GivenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task WhenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task ThenFails(string l) { await Task.Yield(); await C.Make(l); throw new InvalidOperationException(l); }

    [Fact] public void H1_life_with_a_passing_story() =>
        this.Given(x => x.GivenA("h1-g")).When(x => x.WhenA("h1-w")).BDDfy();

    [Fact] public void H2_life_with_a_failing_story() =>
        this.Given(x => x.GivenA("h2-g")).When(x => x.WhenA("h2-w")).Then(x => x.ThenFails("h2-t")).BDDfy();
}

/// <summary>A phase written by hand, and hand-made steps.</summary>
public class Hand : Base
{
    public void GivenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public void ThenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task ThenA(string l) { await Task.Yield(); await C.Make(l); }

    public async Task WhenWritesPhaseEarly(string l) { TestPhaseContext.Current = TestPhase.Setup; await C.Make(l + "-1"); await Task.Yield(); await C.Make(l + "-2"); }
    public async Task WhenWritesPhaseLate(string l) { await Task.Yield(); TestPhaseContext.Current = TestPhase.Setup; await C.Make(l); }
    public Task WhenWritesPhaseSync(string l) { TestPhaseContext.Current = TestPhase.Setup; return C.Make(l); }

    [Fact] public void P1_phase_written_before_the_first_await() =>
        this.Given(x => x.GivenS("p1-g")).When(x => x.WhenWritesPhaseEarly("p1-w")).Then(x => x.ThenS("p1-t")).BDDfy();
    [Fact] public void P2_phase_written_after_the_first_await() =>
        this.Given(x => x.GivenS("p2-g")).When(x => x.WhenWritesPhaseLate("p2-w")).Then(x => x.ThenS("p2-t")).BDDfy();
    [Fact] public void P3_phase_written_in_a_method_that_returns_a_task() =>
        this.Given(x => x.GivenS("p3-g")).When(x => x.WhenWritesPhaseSync("p3-w")).Then(x => x.ThenS("p3-t")).BDDfy();

    public async Task GivenStartsAction(string l) { await Task.Yield(); await C.Make(l + "-1"); TrackingDiagramOverride.StartAction(); await C.Make(l + "-2"); }
    public async Task WhenStartsSetup(string l) { await Task.Yield(); await C.Make(l + "-1"); TrackingDiagramOverride.StartSetup(); await C.Make(l + "-2"); }

    [Fact] public void P4_start_action_by_hand_in_an_async_given() =>
        this.Given(x => x.GivenStartsAction("p4-g")).When(x => x.ThenS("p4-w")).Then(x => x.ThenS("p4-t")).BDDfy();
    [Fact] public void P5_start_setup_by_hand_in_an_async_when() =>
        this.Given(x => x.GivenS("p5-g")).When(x => x.WhenStartsSetup("p5-w")).Then(x => x.ThenS("p5-t")).BDDfy();

    // Hand-made steps through the public overloads
    public async Task WhenHandAmbient(string l)
    {
        await Task.Yield();
        StepCollector.StartStep("Given", "hand ambient", null, null);
        await C.Make(l + "-in");
        StepCollector.CompleteStep(true);
        await C.Make(l + "-after");
    }
    public async Task WhenHandById(string l)
    {
        await Task.Yield();
        StepCollector.StartStep(Id, "Given", "hand by id", null, null);
        await C.Make(l + "-in");
        StepCollector.CompleteStep(Id, true);
        await C.Make(l + "-after");
    }
    public async Task WhenHandFails(string l)
    {
        await Task.Yield();
        StepCollector.StartStep(Id, "Given", "hand fails", null, null);
        await C.Make(l + "-in");
        StepCollector.CompleteStep(Id, false, "hand failure");
        await C.Make(l + "-after");
    }
    public async Task WhenHandEarly(string l)
    {
        StepCollector.StartStep(Id, "Given", "hand opened before the first await", null, null);
        await Task.Yield();
        await C.Make(l + "-in");
        StepCollector.CompleteStep(Id, true);
        await C.Make(l + "-after");
    }
    public Task WhenHandInReturningMethod(string l)
    {
        StepCollector.StartStep(Id, "Given", "hand opened in a method that returns a task", null, null);
        return CloseHandLater(l);
    }
    private async Task CloseHandLater(string l)
    {
        await Task.Yield();
        await C.Make(l + "-in");
        StepCollector.CompleteStep(Id, true);
        await C.Make(l + "-after");
    }
    public async Task WhenClosesTooMany(string l)
    {
        await Task.Yield();
        await C.Make(l);
        StepCollector.CompleteStep(Id, true);
        await C.Make(l + "-after");
    }
    public async Task WhenBypassesItself(string l)
    {
        await Task.Yield();
        await C.Make(l);
        StepCollector.BypassStep(Id, "by hand");
        await C.Make(l + "-after");
    }
    public async Task WhenNotesActive(string l)
    {
        Known.Note($"active before first await: {StepCollector.HasActiveStep(Id)}");
        await Task.Yield();
        Known.Note($"active after first await: {StepCollector.HasActiveStep(Id)}");
        Known.Note($"steps closed so far: {StepCollector.GetSteps(Id).Length}");
    }

    [Fact] public void H3_hand_step_ambient() =>
        this.Given(x => x.GivenS("h3-g")).When(x => x.WhenHandAmbient("h3-w")).Then(x => x.ThenS("h3-t")).BDDfy();
    [Fact] public void H4_hand_step_by_id() =>
        this.Given(x => x.GivenS("h4-g")).When(x => x.WhenHandById("h4-w")).Then(x => x.ThenS("h4-t")).BDDfy();
    [Fact] public void H5_hand_step_fails() =>
        this.Given(x => x.GivenS("h5-g")).When(x => x.WhenHandFails("h5-w")).Then(x => x.ThenS("h5-t")).BDDfy();
    [Fact] public void H6_hand_step_opened_before_the_first_await() =>
        this.Given(x => x.GivenS("h6-g")).When(x => x.WhenHandEarly("h6-w")).Then(x => x.ThenS("h6-t")).BDDfy();
    [Fact] public void H7_hand_step_opened_in_a_method_that_returns_a_task() =>
        this.Given(x => x.GivenS("h7-g")).When(x => x.WhenHandInReturningMethod("h7-w")).Then(x => x.ThenS("h7-t")).BDDfy();
    [Fact] public void H8_a_step_closes_itself_by_hand() =>
        this.Given(x => x.GivenS("h8-g")).When(x => x.WhenClosesTooMany("h8-w")).Then(x => x.ThenS("h8-t")).BDDfy();
    [Fact] public void H9_a_step_bypasses_itself_by_hand() =>
        this.Given(x => x.GivenS("h9-g")).When(x => x.WhenBypassesItself("h9-w")).Then(x => x.ThenS("h9-t")).BDDfy();
    [Fact] public void H10_has_active_step_inside_an_async_step() =>
        this.Given(x => x.GivenS("h10-g")).When(x => x.WhenNotesActive("h10-w")).Then(x => x.ThenS("h10-t")).BDDfy();
}

/// <summary>The per-phase options.</summary>
public class Opt : Base
{
    private readonly HttpClient _noSetup = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = false }) { InnerHandler = new AnswerEverything() });
    private readonly HttpClient _noAction = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringAction = false }) { InnerHandler = new AnswerEverything() });

    public async Task GivenN(string l) { await Task.Yield(); await _noSetup.GetAsync($"http://probe.local/{l}"); }
    [GivenStep] public async Task WovenGivenN(string l) { await _noSetup.GetAsync($"http://probe.local/{l}"); }
    [WhenStep] public async Task WovenWhenA(string l) { await _noAction.GetAsync($"http://probe.local/{l}"); }
    public async Task WhenWithWovenGiven(string l) { await Task.Yield(); await _noSetup.GetAsync($"http://probe.local/{l}-before"); await WovenGivenN(l + "-sub"); await _noSetup.GetAsync($"http://probe.local/{l}-after"); }
    public async Task GivenWithWovenWhen(string l) { await Task.Yield(); await _noAction.GetAsync($"http://probe.local/{l}-before"); await WovenWhenA(l + "-sub"); await _noAction.GetAsync($"http://probe.local/{l}-after"); }
    public async Task ThenN(string l) { await Task.Yield(); await _noSetup.GetAsync($"http://probe.local/{l}"); }
    public async Task ThenA(string l) { await Task.Yield(); await _noAction.GetAsync($"http://probe.local/{l}"); }

    [Fact] public void O2_track_during_setup_off_with_a_woven_given_under_an_async_when() =>
        this.Given(x => x.GivenN("o2-g")).When(x => x.WhenWithWovenGiven("o2-w")).Then(x => x.ThenN("o2-t")).BDDfy();
    [Fact] public void O3_track_during_action_off_with_a_woven_when_under_an_async_given() =>
        this.Given(x => x.GivenWithWovenWhen("o3-g")).When(x => x.ThenA("o3-w")).Then(x => x.ThenA("o3-t")).BDDfy();
}
