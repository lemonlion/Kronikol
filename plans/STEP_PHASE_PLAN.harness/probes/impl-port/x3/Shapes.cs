using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

namespace PhaseProbe.X3;

/// <summary>The steps every shape below uses.</summary>
public abstract class Steps : ProbeBase
{
    protected Calls C = Env.NewCalls();

    public bool Always => true;

    [GivenStep] public async Task GivenCall(string l) => await C.Make(l);
    [GivenStep] public void GivenCallSync(string l) => C.Make(l).GetAwaiter().GetResult();
    [GivenStep] public async Task GivenNothing() => await Task.Yield();
    [GivenStep] public async Task GivenMessage(string l) { await Task.Yield(); C.Message(l); }
    [WhenStep] public async Task WhenCall(string l) => await C.Make(l);
    [WhenStep] public void WhenCallSync(string l) => C.Make(l).GetAwaiter().GetResult();
    [WhenStep] public async Task WhenNothing() => await Task.Yield();
    [WhenStep] public async Task WhenMessage(string l) { await Task.Yield(); C.Message(l); }
    [ThenStep] public async Task ThenCall(string l) => await C.Make(l);
    [ThenStep] public async Task ThenNothing() => await Task.Yield();
    [WhenStep(SkipIf = nameof(Always), SkipReason = "s")] public async Task WhenSkipped(string l) => await C.Make(l);
    [GivenStep(SkipIf = nameof(Always), SkipReason = "s")] public async Task GivenSkipped(string l) => await C.Make(l);
    [Step] public async Task PlainCall(string l) => await C.Make(l);
}

public class Controls : Steps
{
    [Fact] public async Task C01_given_when_then() { await GivenCall("c01-g"); await WhenCall("c01-w"); await ThenCall("c01-t"); }

    [Fact] public async Task C02_when_first_then_given_when() { await WhenCall("c02-w1"); await GivenCall("c02-g"); await WhenCall("c02-w2"); }

    [Fact] public async Task C03_given_no_call_when_call() { await GivenNothing(); await WhenCall("c03-w"); }

    [Fact] public async Task C04_given_call_when_no_call_then_call() { await GivenCall("c04-g"); await WhenNothing(); await ThenCall("c04-t"); }

    [Fact] public async Task C05_message_only_given() { await GivenMessage("c05-g"); await WhenMessage("c05-w"); }
}

/// <summary>The public StartAction and StartSetup with what a consumer's own helper can hand them.</summary>
public class NullIds : Steps
{
    private static bool Off => Environment.GetEnvironmentVariable("PROBE_NULLS") != "on";

    [Fact]
    public async Task N01_StartAction_null_string()
    {
        if (Off) return;
        var id = Id;
        await GivenCall("n01-g");
        try { DefaultTrackingDiagramOverride.StartAction((string)null!); Dump.Note(id, "StartAction(null) did not throw"); }
        catch (Exception ex) { Dump.Note(id, "StartAction(null) THREW " + ex.GetType().Name); }
        await WhenCall("n01-w");
    }

    [Fact]
    public async Task N02_StartAction_func_null()
    {
        if (Off) return;
        var id = Id;
        await GivenCall("n02-g");
        try { DefaultTrackingDiagramOverride.StartAction(() => null!); Dump.Note(id, "StartAction(()=>null) did not throw"); }
        catch (Exception ex) { Dump.Note(id, "StartAction(()=>null) THREW " + ex.GetType().Name); }
        await WhenCall("n02-w");
    }

    [Fact]
    public async Task N03_StartSetup_null()
    {
        if (Off) return;
        var id = Id;
        try { DefaultTrackingDiagramOverride.StartSetup((string)null!); Dump.Note(id, "StartSetup(null) did not throw"); }
        catch (Exception ex) { Dump.Note(id, "StartSetup(null) THREW " + ex.GetType().Name); }
        await WhenCall("n03-w");
    }

    [Fact]
    public async Task N04_identity_fetcher_gives_a_null_id_with_a_step_type_fetcher()
    {
        if (Off) return;
        var id = Id;
        var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            CurrentTestInfoFetcher = () => (null!, null!),
            CurrentStepTypeFetcher = () => "GIVEN",
        }) { InnerHandler = new AnswerEverything() };
        using var client = new HttpClient(handler);
        try { (await client.GetAsync("http://probe.local/n04")).EnsureSuccessStatusCode(); Dump.Note(id, "call with a null id did not throw"); }
        catch (Exception ex) { Dump.Note(id, "call with a null id THREW " + ex.GetType().Name); }
    }

    [Fact]
    public async Task N05_identity_fetcher_gives_a_null_id_with_no_step_type_fetcher()
    {
        if (Off) return;
        var id = Id;
        var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            CurrentTestInfoFetcher = () => (null!, null!),
        }) { InnerHandler = new AnswerEverything() };
        using var client = new HttpClient(handler);
        try { (await client.GetAsync("http://probe.local/n05")).EnsureSuccessStatusCode(); Dump.Note(id, "call with a null id did not throw"); }
        catch (Exception ex) { Dump.Note(id, "call with a null id THREW " + ex.GetType().Name); }
    }

    [Fact]
    public async Task N06_StartAction_empty_id()
    {
        var id = Id;
        try { DefaultTrackingDiagramOverride.StartAction(""); Dump.Note(id, "StartAction(empty) did not throw"); }
        catch (Exception ex) { Dump.Note(id, "StartAction(empty) THREW " + ex.GetType().Name); }
        await WhenCall("n06-w");
    }
}

public class SkipIfs : Steps
{
    [Fact] public async Task K01_given_skippedwhen_given_when() { await GivenCall("k01-g1"); await WhenSkipped("k01-skipped"); await GivenCall("k01-g2"); await WhenCall("k01-w"); await ThenCall("k01-t"); }
    [Fact] public async Task K02_skippedwhen_given_when() { await WhenSkipped("k02-skipped"); await GivenCall("k02-g"); await WhenCall("k02-w"); await ThenCall("k02-t"); }
    [Fact] public async Task K03_skippedgiven_call_when() { await GivenSkipped("k03-skipped"); await C.Make("k03-loose"); await WhenCall("k03-w"); }
    [Fact] public async Task K04_given_skippedwhen_then() { await GivenCall("k04-g"); await WhenSkipped("k04-skipped"); await ThenCall("k04-t"); }
    [Fact] public async Task K05_given_skippedwhen_explicit_later() { await GivenCall("k05-g"); await WhenSkipped("k05-skipped"); await C.Make("k05-more-setup"); DefaultTrackingDiagramOverride.StartAction(Id); await C.Make("k05-act"); }
}

public class Explicits : Steps
{
    [Fact] public async Task E01_explicit_after_woven_when_ended() { await GivenCall("e01-g"); await WhenCall("e01-w"); DefaultTrackingDiagramOverride.StartAction(Id); await ThenCall("e01-t"); }
    [Fact] public async Task E02_explicit_twice_with_calls() { DefaultTrackingDiagramOverride.StartSetup(Id); await C.Make("e02-setup"); DefaultTrackingDiagramOverride.StartAction(Id); await C.Make("e02-a"); DefaultTrackingDiagramOverride.StartAction(Id); await C.Make("e02-b"); }
    [Fact] public async Task E03_explicit_setup_action_setup_action() { DefaultTrackingDiagramOverride.StartSetup(Id); await C.Make("e03-s1"); DefaultTrackingDiagramOverride.StartAction(Id); await C.Make("e03-a1"); DefaultTrackingDiagramOverride.StartSetup(Id); await C.Make("e03-s2"); DefaultTrackingDiagramOverride.StartAction(Id); await C.Make("e03-a2"); }
    [Fact] public async Task E04_explicit_in_given_then_when() { await GivenCall("e04-g1"); DefaultTrackingDiagramOverride.StartAction(Id); await GivenCall("e04-g2"); await WhenCall("e04-w"); }
    [Fact] public async Task E05_startsetup_inside_when_then_startaction() { await GivenCall("e05-g"); await WhenSetupThenAct(); }

    [WhenStep] public async Task WhenSetupThenAct()
    {
        DefaultTrackingDiagramOverride.StartSetup(Id);
        await C.Make("e05-arrange");
        DefaultTrackingDiagramOverride.StartAction(Id);
        await C.Make("e05-act");
    }

    [Fact] public async Task E06_startaction_on_another_flow() { await GivenCall("e06-g"); var id = Id; await Task.Run(() => DefaultTrackingDiagramOverride.StartAction(id)); await C.Make("e06-a"); }

    [Fact]
    public async Task E07_ClearSteps_retry_wrapper_of_woven_steps()
    {
        var id = Id;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            StepCollector.ClearSteps(id);
            await GivenCall($"e07-a{attempt}-g");
            await WhenCall($"e07-a{attempt}-w");
        }
    }

    [Fact]
    public async Task E08_ClearSteps_retry_wrapper_of_explicit_boundary()
    {
        var id = Id;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            StepCollector.ClearSteps(id);
            DefaultTrackingDiagramOverride.StartSetup(id);
            await C.Make($"e08-a{attempt}-setup");
            DefaultTrackingDiagramOverride.StartAction(id);
            await C.Make($"e08-a{attempt}-act");
        }
    }
}

public class Diagram : Steps
{
    [Fact] public async Task D01_custom_note_in_setup() { await GivenCall("d01-g1"); DefaultTrackingDiagramOverride.InsertPlantUml(Id, "note over Probe: consumer note"); await GivenCall("d01-g2"); await WhenCall("d01-w"); }
    [Fact] public async Task D02_delimiter_first() { DefaultTrackingDiagramOverride.InsertTestDelimiter(Id, "first"); await GivenCall("d02-g"); await WhenCall("d02-w"); }

    [Fact]
    public async Task D03_long_setup_splits()
    {
        await LongGiven();
        await WhenCall("d03-w");
        await ThenCall("d03-t");
    }

    [GivenStep] public async Task LongGiven()
    {
        for (var i = 0; i < 60; i++)
            await C.Make($"d03-g-{i:00}/{new string('x', 200)}");
    }

    [Fact]
    public async Task D04_polls_across_the_boundary()
    {
        await GivenPoll();
        await WhenPoll();
    }

    [GivenStep] public async Task GivenPoll() { for (var i = 0; i < 3; i++) await C.Make("d04-poll"); }
    [WhenStep] public async Task WhenPoll() { for (var i = 0; i < 3; i++) await C.Make("d04-poll"); }

    [Fact]
    public async Task D05_fold_only_after_the_boundary()
    {
        await GivenCall("d05-g");
        await WhenPoll5();
    }

    [WhenStep] public async Task WhenPoll5() { for (var i = 0; i < 3; i++) await C.Make("d05-poll"); }

    [Fact] public async Task D06_no_calls_at_all() { await GivenNothing(); await WhenNothing(); await ThenNothing(); }

    [Fact]
    public async Task D07_variants_on_a_call_before_the_steps()
    {
        var tracker = Env.Tracker(setup: MessageTrackerVerbosity.Summarised, action: MessageTrackerVerbosity.Detailed);
        tracker.TrackSendEvent("bus", "d07-before", new Uri("bus://probe/d07-before"), new { payload = "body-before" });
        await GivenCall("d07-g");
        await WhenCall("d07-w");
        await Task.Run(() =>
        {
            TestPhaseContext.Reset();
            tracker.TrackSendEvent("bus", "d07-after", new Uri("bus://probe/d07-after"), new { payload = "body-after" });
        });
    }

    [Fact]
    public async Task D08_variant_run_of_identical_calls_across_the_boundary_bars_or_not()
    {
        var tracker = Env.Tracker(setup: MessageTrackerVerbosity.Summarised, action: MessageTrackerVerbosity.Detailed);
        void Reset() => TestPhaseContext.Reset();
        await GivenNothing();
        await Task.Run(() => { Reset(); tracker.TrackSendEvent("bus", "d08", new Uri("bus://probe/d08"), new { payload = "body-d08" }); });
        await C.Make("d08-poll"); await C.Make("d08-poll"); await C.Make("d08-poll");
        await WhenNothing();
        await C.Make("d08-poll"); await C.Make("d08-poll"); await C.Make("d08-poll");
    }

    [Fact]
    public void D09_failing_in_when_after_marker()
    {
        GivenCallSync("d09-g");
        WhenCallSync("d09-w");
        Assert.Fail("d09 fails on purpose");
    }

    [Fact]
    public async Task D10_when_with_no_given_failing()
    {
        await GivenNothing();
        await WhenNothing();
        Assert.Fail("d10 fails on purpose with no calls");
    }
}

public class Lifecycle : Steps, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await GivenCall($"life-init-g-{TestContext.Current.TestMethod!.MethodName[..3]}");
    }

    public async ValueTask DisposeAsync()
    {
        await C.Make($"life-dispose-{TestContext.Current.TestMethod!.MethodName[..3]}");
    }

    [Fact] public async Task L01_given_in_InitializeAsync_when_in_test() { await WhenCall("l01-w"); await ThenCall("l01-t"); }
    [Fact] public async Task L02_no_keyword_in_test() { await C.Make("l02-loose"); }
    [Fact] public async Task L03_call_before_when() { await C.Make("l03-loose"); await WhenCall("l03-w"); }
}

public class CtorGiven : Steps
{
    public CtorGiven() => GivenCallSync($"ctor-g-{TestContext.Current.TestMethod!.MethodName[..3]}");

    [Fact] public async Task L10_given_in_ctor_when_in_test() { await WhenCall("l10-w"); await ThenCall("l10-t"); }
}

public class Fixtured(Fixtured.Fixture fixture) : Steps, IClassFixture<Fixtured.Fixture>
{
    public class Fixture : IAsyncLifetime
    {
        public readonly Calls Shared = Env.NewCalls();
        public async ValueTask InitializeAsync() => await Shared.Make("fixture-init");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [GivenStep] public async Task GivenShared(string l) => await fixture.Shared.Make(l);
    [WhenStep] public async Task WhenShared(string l) => await fixture.Shared.Make(l);

    [Fact] public async Task L20_class_fixture_client_one() { await GivenShared("l20-g"); await WhenShared("l20-w"); }
    [Fact] public async Task L21_class_fixture_client_two() { await GivenShared("l21-g"); await WhenShared("l21-w"); }
}

public class Pollers : Steps
{
    private async Task Poll(Calls calls, string label, CancellationToken stop)
    {
        var i = 0;
        while (!stop.IsCancellationRequested)
        {
            await calls.Make($"{label}-{i++:00}");
            await Task.Delay(8);
        }
    }

    [GivenStep]
    public async Task GivenStartsAPoller(Calls calls, CancellationTokenSource stop, List<Task> running, string label)
    {
        running.Add(Task.Run(() => Poll(calls, label, stop.Token)));
        await Task.Delay(30);
    }

    [Fact]
    public async Task B01_poller_started_in_given_outlives_it()
    {
        using var stop = new CancellationTokenSource();
        var running = new List<Task>();
        await GivenStartsAPoller(C, stop, running, "b01-poll");
        await WhenCall("b01-w");
        await Task.Delay(30);
        await ThenCall("b01-t");
        stop.Cancel();
        await Task.WhenAll(running);
    }

    [Fact]
    public async Task B02_poller_started_in_given_with_TrackDuringSetup_off_outlives_it()
    {
        using var stop = new CancellationTokenSource();
        var running = new List<Task>();
        var off = Env.NewCalls(trackDuringSetup: false);
        await GivenStartsAPoller(off, stop, running, "b02-poll");
        await WhenCall("b02-w");
        await Task.Delay(30);
        await ThenCall("b02-t");
        stop.Cancel();
        await Task.WhenAll(running);
    }

    [Fact]
    public async Task B03_poller_started_before_any_step()
    {
        using var stop = new CancellationTokenSource();
        var poll = Task.Run(() => Poll(C, "b03-poll", stop.Token));
        await GivenCall("b03-g");
        await Task.Delay(20);
        await WhenCall("b03-w");
        await Task.Delay(20);
        await ThenCall("b03-t");
        stop.Cancel();
        await poll;
    }
}

public class Forked : Steps
{
    [Fact] public async Task F01_when_on_a_fork_then_main_flow_calls() { await GivenCall("f01-g"); await Task.Run(() => WhenCall("f01-w")); await C.Make("f01-main"); await C.Make("f01-main2"); }
    [Fact] public async Task F02_when_on_a_fork_main_flow_TrackDuringSetup_off() { var off = Env.NewCalls(trackDuringSetup: false); await off.Make("f02-before"); await GivenCall("f02-g"); await Task.Run(() => WhenCall("f02-w")); await off.Make("f02-main"); }
    [Fact] public async Task F03_given_and_when_together() { await Task.WhenAll(GivenCall("f03-g"), WhenCall("f03-w")); await ThenCall("f03-t"); }
    [Fact] public async Task F04_when_and_given_together() { await Task.WhenAll(WhenCall("f04-w"), GivenCall("f04-g")); await ThenCall("f04-t"); }
    [Fact] public async Task F05_two_whens_together() { await GivenCall("f05-g"); await Task.WhenAll(WhenCall("f05-w1"), WhenCall("f05-w2")); await ThenCall("f05-t"); }
    [Fact] public async Task F06_given_left_running_when_starts() { var g = GivenSlow("f06-g"); await WhenCall("f06-w"); await g; await ThenCall("f06-t"); }
    [GivenStep] public async Task GivenSlow(string l) { await Task.Delay(30); await C.Make(l); }
    [Fact] public async Task F07_given_on_a_fork_when_on_main() { await Task.Run(() => GivenCall("f07-g")); await C.Make("f07-loose"); await WhenCall("f07-w"); await C.Make("f07-after"); }
    [Fact] public async Task F08_suppressed_flow_when() { await GivenCall("f08-g"); Task t; using (ExecutionContext.SuppressFlow()) t = Task.Run(() => WhenCall("f08-w")); await t; await C.Make("f08-main"); }
}

public class Handmade : Steps
{
    private static void Step(string id, string keyword, string text) => StepCollector.StartStep(id, keyword, text, null, null);

    [Fact] public async Task H01_hand_made_given_when() { var id = Id; Step(id, "Given", "g"); await C.Make("h01-g"); StepCollector.CompleteStep(id, true); Step(id, "When", "w"); await C.Make("h01-w"); StepCollector.CompleteStep(id, true); }
    [Fact] public async Task H02_hand_made_for_another_test() { var other = "other-test-" + Guid.NewGuid().ToString("N")[..6]; Dump.Names[other] = "H02-other"; Step(other, "Given", "g"); StepCollector.CompleteStep(other, true); await C.Make("h02-a"); Step(other, "When", "w"); StepCollector.CompleteStep(other, true); await C.Make("h02-b"); }
    [Fact] public async Task H03_hand_made_lower_case_keywords() { var id = Id; Step(id, "given", "g"); await C.Make("h03-g"); StepCollector.CompleteStep(id, true); Step(id, "when", "w"); await C.Make("h03-w"); StepCollector.CompleteStep(id, true); }
    [Fact] public async Task H04_hand_made_when_left_open() { var id = Id; Step(id, "Given", "g"); await C.Make("h04-g"); StepCollector.CompleteStep(id, true); Step(id, "When", "w"); await C.Make("h04-w"); DefaultTrackingDiagramOverride.StartAction(id); await C.Make("h04-after"); }
    [Fact] public async Task H05_hand_made_nested_when_in_plain() { var id = Id; Step(id, "Given", "g"); await C.Make("h05-g"); StepCollector.CompleteStep(id, true); Step(id, null!, "wrapper"); Step(id, "When", "inner"); await C.Make("h05-w"); StepCollector.CompleteStep(id, true); StepCollector.CompleteStep(id, true); }
    [Fact] public async Task H06_hand_set_TestPhaseContext_then_when() { TestPhaseContext.Current = TestPhase.Setup; await C.Make("h06-hand-setup"); await WhenCall("h06-w"); }
    [Fact] public async Task H07_hand_set_action_after_given() { await GivenCall("h07-g"); TestPhaseContext.Current = TestPhase.Action; await C.Make("h07-hand-action"); await WhenCall("h07-w"); }
    [Fact] public async Task H08_hand_reset_in_setup() { await GivenCall("h08-g"); TestPhaseContext.Reset(); await C.Make("h08-reset"); await WhenCall("h08-w"); }
    [Fact]
    public async Task H09_scope_only_identity_hand_made()
    {
        var virtualId = "virtual-" + Guid.NewGuid().ToString("N")[..6];
        Dump.Names[virtualId] = "H09-virtual";
        using (TestIdentityScope.Begin("virtual test", virtualId))
        {
            var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() };
            using var client = new HttpClient(handler);
            Step(virtualId, "Given", "g");
            await client.GetAsync("http://probe.local/h09-g");
            StepCollector.CompleteStep(virtualId, true);
            Step(virtualId, "When", "w");
            await client.GetAsync("http://probe.local/h09-w");
            StepCollector.CompleteStep(virtualId, true);
        }
    }
    [Fact]
    public async Task H10_no_framework_flow_scope_on_raw_thread()
    {
        var virtualId = "thread-" + Guid.NewGuid().ToString("N")[..6];
        Dump.Names[virtualId] = "H10-thread";
        Task t;
        using (ExecutionContext.SuppressFlow())
            t = Task.Run(async () =>
            {
                using var scope = TestIdentityScope.Begin("thread test", virtualId);
                var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() };
                using var client = new HttpClient(handler);
                Step(virtualId, "Given", "g");
                await client.GetAsync("http://probe.local/h10-g");
                StepCollector.CompleteStep(virtualId, true);
                Step(virtualId, "When", "w");
                await client.GetAsync("http://probe.local/h10-w");
                StepCollector.CompleteStep(virtualId, true);
            });
        await t;
    }
}

public class Fetchers : ProbeBase
{
    private static readonly AsyncLocal<string?> Stage = new();
    private static Calls NewCalls() => new(new HttpClient(Env.Handler(() => Stage.Value)), Env.Tracker());
    private static readonly Calls Shared = NewCalls();

    private static async Task Run(Calls c, string label)
    {
        Stage.Value = "Given";
        await c.Make($"{label}-g");
        Stage.Value = "When";
        await c.Make($"{label}-w");
        await c.Make($"{label}-w2");
        Stage.Value = "Then";
        await c.Make($"{label}-t");
    }

    [Fact] public async Task T01_shared_handler_first() => await Run(Shared, "t01");
    [Fact] public async Task T02_shared_handler_second() => await Run(Shared, "t02");
    [Fact]
    public async Task T03_a_client_per_step()
    {
        Stage.Value = "Given";
        await NewCalls().Make("t03-g");
        Stage.Value = "When";
        await NewCalls().Make("t03-w");
        Stage.Value = "Then";
        await NewCalls().Make("t03-t");
    }
    [Fact]
    public async Task T04_given_on_a_when_on_b_then_on_a()
    {
        var a = NewCalls(); var b = NewCalls();
        Stage.Value = "Given";
        await a.Make("t04-g");
        Stage.Value = "When";
        await b.Make("t04-w");
        Stage.Value = "Then";
        await a.Make("t04-t");
    }
    [Fact]
    public async Task T05_two_clients_both_in_given_both_in_when()
    {
        var a = NewCalls(); var b = NewCalls();
        Stage.Value = "Given";
        await a.Make("t05-ga"); await b.Make("t05-gb");
        Stage.Value = "When";
        await a.Make("t05-wa"); await a.Make("t05-wa2"); await b.Make("t05-wb"); await b.Make("t05-wb2");
    }
    [Fact]
    public async Task T06_then_given_when_by_fetcher()
    {
        var a = NewCalls();
        Stage.Value = "Then";
        await a.Make("t06-t");
        Stage.Value = "Given";
        await a.Make("t06-g");
        Stage.Value = "When";
        await a.Make("t06-w");
    }
    [Fact]
    public async Task T07_fetcher_throws_on_every_call()
    {
        var id = Id;
        var c = new Calls(new HttpClient(Env.Handler(() => throw new InvalidOperationException("no step"))), Env.Tracker());
        try { await c.Make("t07"); Dump.Note(id, "call did not throw"); }
        catch (Exception ex) { Dump.Note(id, "call THREW " + ex.GetType().Name); }
    }
    [Fact]
    public async Task T08_explicit_then_fetcher()
    {
        var a = NewCalls();
        Stage.Value = "Given";
        await a.Make("t08-g");
        DefaultTrackingDiagramOverride.StartAction(Id);
        await a.Make("t08-after-explicit");
        Stage.Value = "When";
        await a.Make("t08-w");
    }
    [Fact]
    public async Task T09_fetcher_then_explicit_later()
    {
        var a = NewCalls();
        Stage.Value = "Given";
        await a.Make("t09-g");
        Stage.Value = "When";
        await a.Make("t09-w");
        DefaultTrackingDiagramOverride.StartAction(Id);
        await a.Make("t09-after-explicit");
    }
    [Fact]
    public async Task T10_when_says_nothing_to_client_but_message_goes()
    {
        var a = NewCalls();
        Stage.Value = "Given";
        await a.Make("t10-g");
        Stage.Value = "When";
        a.Message("t10-w-message");
        Stage.Value = "Then";
        a.Message("t10-t-message");
        await a.Make("t10-t");
    }
}

[Collection("P1")] public class Parallel1 : Steps { [Fact] public Task P11() => ParallelShared.Run(this, "p11"); [Fact] public Task P12() => ParallelShared.Run(this, "p12"); [Fact] public Task P13() => ParallelShared.Run(this, "p13"); }
[Collection("P2")] public class Parallel2 : Steps { [Fact] public Task P21() => ParallelShared.Run(this, "p21"); [Fact] public Task P22() => ParallelShared.Run(this, "p22"); [Fact] public Task P23() => ParallelShared.Run(this, "p23"); }
[Collection("P3")] public class Parallel3 : Steps { [Fact] public Task P31() => ParallelShared.Run(this, "p31"); [Fact] public Task P32() => ParallelShared.Run(this, "p32"); [Fact] public Task P33() => ParallelShared.Run(this, "p33"); }

public static class ParallelShared
{
    public static readonly Calls C = Env.NewCalls();

    public static async Task Run(Steps steps, string label)
    {
        for (var i = 0; i < 3; i++) await steps.GivenShared($"{label}-g{i}");
        for (var i = 0; i < 3; i++) await steps.WhenShared($"{label}-w{i}");
        await steps.ThenShared($"{label}-t");
    }

    [GivenStep] public static async Task GivenShared(this Steps steps, string l) => await C.Make(l);
    [WhenStep] public static async Task WhenShared(this Steps steps, string l) => await C.Make(l);
    [ThenStep] public static async Task ThenShared(this Steps steps, string l) => await C.Make(l);
}

public class ActNotAStep : Steps
{
    [Fact] public async Task A01_given_plain_act_then() { await GivenCall("a01-g"); await C.Make("a01-the-act"); await ThenCall("a01-t"); }
    [Fact] public async Task A02_given_keyword_free_step_act_then() { await GivenCall("a02-g"); await PlainCall("a02-the-act"); await ThenCall("a02-t"); }
    [Fact] public async Task A03_given_plain_act_no_then() { await GivenCall("a03-g"); await C.Make("a03-the-act"); }
    [Fact] public async Task A04_given_plain_act_then_with_no_call() { await GivenCall("a04-g"); await C.Make("a04-the-act"); await ThenNothing(); }
    [Fact]
    public async Task A05_given_plain_act_then_track_during_setup_off()
    {
        var off = Env.NewCalls(trackDuringSetup: false);
        await off.Make("a05-before");
        await GivenCall("a05-g");
        await off.Make("a05-the-act");
        await ThenCall("a05-t");
    }
}

public class Backgrounds : ProbeBase
{
    private static readonly AsyncLocal<string?> Stage = new();
    private static readonly Calls ViaFetcher = new(new HttpClient(Env.Handler(() => Stage.Value)), Env.Tracker());

    [Fact]
    public async Task G01_a_flow_nobody_owns_says_given_then_when_on_a_handler_tests_share()
    {
        Task t;
        using (ExecutionContext.SuppressFlow())
            t = Task.Run(async () =>
            {
                Stage.Value = "Given";
                await ViaFetcher.Make("g01-bg-g");
                Stage.Value = "When";
                await ViaFetcher.Make("g01-bg-w");
            });
        await t;
        Stage.Value = "Given";
        await ViaFetcher.Make("g01-g");
        Stage.Value = "When";
        await ViaFetcher.Make("g01-w");
    }

    [Fact]
    public async Task G02_a_second_test_on_that_handler()
    {
        Stage.Value = "Given";
        await ViaFetcher.Make("g02-g");
        Stage.Value = "When";
        await ViaFetcher.Make("g02-w");
    }
}
