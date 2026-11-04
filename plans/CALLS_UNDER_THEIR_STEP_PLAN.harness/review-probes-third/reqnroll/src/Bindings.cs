using System.Net;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace rnprobe;

public sealed class Fake : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(2, ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
    }
}

public static class Api
{
    private static readonly HttpClient Client = new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc", CallerName = "Test" }) { InnerHandler = new Fake() }) { BaseAddress = new Uri("http://svc.local") };
    public static Task Call(string where) => Client.GetAsync("/" + where);
    public static void CallSync(string where) => Client.GetAsync("/" + where).GetAwaiter().GetResult();
}

[Binding]
public class RunHooks
{
    [AfterTestRun]
    public static void Report() => ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "RN" });
}

[Binding]
public class Hooks
{
    public static bool Yes => true;

    // ---- step methods the hooks call
    [Step] public static void HookStep() { Api.CallSync("in-HookStep"); }
    [Step] public static async Task HookStepAsync() { await Task.Delay(1); await Api.Call("in-HookStepAsync"); }
    [GivenStep] public static void KeywordedHookStep() { Api.CallSync("in-KeywordedHookStep"); }
    [Step] public static void BadHookStep() { Api.CallSync("in-BadHookStep"); throw new InvalidOperationException("hook's step method failed"); }
    [Step] public static async Task RunningHookStep() { await Api.Call("in-RunningHookStep"); await Task.Delay(150); await Api.Call("in-RunningHookStep-2"); }

    public static Task? Running;

    [BeforeScenario("hookstep-before")] public void Hs1() => HookStep();
    [BeforeScenario("hookstep-before-async")] public async Task Hs2() { await HookStepAsync(); }
    [AfterScenario("hookstep-after")] public void Hs3() => HookStep();
    [BeforeStep("hookstep-beforestep")] public void Hs4() => HookStep();
    [AfterStep("hookstep-afterstep")] public void Hs5() => HookStep();
    [BeforeScenario("hookstep-before-bg")] public void Hs6() => KeywordedHookStep();

    [BeforeStep("asyncstephooks")] public async Task A1() { await HookStepAsync(); }
    [AfterStep("asyncstephooks")] public async Task A2() { await HookStepAsync(); await Api.Call("in-async-AfterStep-hook"); }
    [AfterStep("afterstep-throws-after-step-method")] public void A3() { HookStep(); throw new InvalidOperationException("AfterStep hook threw after its step method"); }
    [BeforeStep("beforestep-step-method-fails")] public void A4() { BadHookStep(); }
    [AfterScenario("afterscenario-attach")] public void A5() { Track.Attachment(WriteTemp("rn-afterscenario"), "rn-afterscenario"); Track.That(() => Assert.True(true), "assert-in-AfterScenario"); }
    [BeforeScenario("beforescenario-attach")] public void A6() { Track.Attachment(WriteTemp("rn-beforescenario"), "rn-beforescenario"); Track.That(() => Assert.True(true), "assert-in-BeforeScenario"); }
    [BeforeStep("step-attach-in-hooks")] public void A7() { Track.Attachment(WriteTemp("rn-beforestep"), "rn-beforestep"); Track.That(() => Assert.True(true), "assert-in-BeforeStep"); }
    [AfterStep("step-attach-in-hooks")] public void A8() { Track.Attachment(WriteTemp("rn-afterstep"), "rn-afterstep"); Track.That(() => Assert.True(true), "assert-in-AfterStep"); }
    static string WriteTemp(string name) { var p = Path.Combine(Path.GetTempPath(), name + ".txt"); File.WriteAllText(p, "x"); return p; }

    [BeforeScenario("hookcall-all")] public void C1() => Api.CallSync("in-BeforeScenario-hook");
    [BeforeStep("hookcall-all")] public void C2() => Api.CallSync("in-BeforeStep-hook");
    [AfterStep("hookcall-all")] public void C3() => Api.CallSync("in-AfterStep-hook");
    [AfterScenario("hookcall-all")] public void C4() => Api.CallSync("in-AfterScenario-hook");

    [BeforeScenario("hookstep-before-fails-caught")] public void F1() { try { BadHookStep(); } catch { } }
    [BeforeScenario("hookstep-before-fails")] public void F2() { BadHookStep(); }
    [BeforeScenario("hook-leaves-running")] public void L1() { Running = RunningHookStep(); }

    [BeforeScenario("hookthrows-beforescenario")] public void T1() { Api.CallSync("in-throwing-BeforeScenario"); throw new InvalidOperationException("BeforeScenario hook threw"); }
    [BeforeStep("hookthrows-beforestep")] public void T2() { Api.CallSync("in-throwing-BeforeStep"); throw new InvalidOperationException("BeforeStep hook threw"); }
    [AfterStep("hookthrows-afterstep")] public void T3() { Api.CallSync("in-throwing-AfterStep"); throw new InvalidOperationException("AfterStep hook threw"); }
    [AfterScenario("hookthrows-afterscenario")] public void T4() { Api.CallSync("in-throwing-AfterScenario"); throw new InvalidOperationException("AfterScenario hook threw"); }
}

[Binding]
public class Steps(ScenarioContext ctx, ITestRunner runner, IReqnrollOutputHelper output)
{
    public bool Yes => true;
    static Task? _started; static TaskCompletionSource? _go;

    // ---- basics
    [When("a background step fails when tagged")] public void BgMaybeFails() { Api.CallSync("in-bgmaybefails"); if (ctx.ScenarioInfo.Tags.Contains("bgfails")) throw new InvalidOperationException("background step failed here"); }
    [Given("a background step")] public async Task Bg1() { await Api.Call("in-bg1"); await SubBg(); }
    [Given("a second background step")] public void Bg2() { Api.CallSync("in-bg2"); }
    [Step] async Task SubBg() { await Api.Call("in-SubBg"); }
    [Given("a shared given")] public void Sh1() { Api.CallSync("in-shared1"); SubSh(); }
    [Given("a second shared given")] public void Sh2() { Api.CallSync("in-shared2"); }
    [Step] void SubSh() { Api.CallSync("in-SubSh"); }
    [Given("a plain given")] public void Plain() { Api.CallSync("in-plain"); }
    [When("I act {string}")] public async Task Act(string who) { await Api.Call("in-act-" + who); await SubAct(who); await Api.Call("in-act-after-" + who); }
    [Step] async Task SubAct(string who) { await Task.Delay(1); await Api.Call("in-SubAct-" + who); }
    [Then("I check")] public void Check() { Api.CallSync("in-check"); }
    [Then("I check again")] public void Check2() { Api.CallSync("in-check2"); }

    [Given("a background step that varies")] public void Varies() { if (ctx.ScenarioInfo.Tags.Contains("more-subs")) OnlySecond(); Always(); }
    [Step] void OnlySecond() { Api.CallSync("in-OnlySecond"); }
    [Step] void Always() { Api.CallSync("in-Always"); }

    // ---- failures
    [When("a step throws")] public void Throws() { Api.CallSync("in-throws"); throw new InvalidOperationException("the step threw"); }
    [When("a step's sub step throws")] public void SubThrows() { Api.CallSync("in-subthrows"); Bad(); }
    [Step] void Bad() { Api.CallSync("in-Bad"); throw new InvalidOperationException("the sub step threw"); }
    [When("a step is pending")] public void Pending() { Api.CallSync("in-pending"); throw new PendingStepException("not written yet"); }
    [When("a step catches its sub step's failure")] public void Catches() { try { Bad(); } catch { } Api.CallSync("in-catches-after"); }
    [When("a step throws after a sub step")] public void ThrowsAfter() { SubOk(); Api.CallSync("in-throwsafter"); throw new InvalidOperationException("thrown after a passed sub step"); }
    [Step] void SubOk() { Api.CallSync("in-SubOk"); }
    [When("an ambiguous step")] public void Ambig1() { }
    [When("an ambiguous (.*)")] public void Ambig2(string x) { }

    // ---- ITestRunner
    [When("a step runs another")] public async Task RunsAnother() { await Api.Call("in-outer-before"); await runner.WhenAsync("the inner step", null, null, "When "); await Api.Call("in-outer-after"); }
    [When("the inner step")] public async Task Inner() { await Api.Call("in-inner"); await SubInner(); }
    [Step] async Task SubInner() { await Api.Call("in-SubInner"); }
    [When("a step runs another that fails")] public async Task RunsFailing() { await Api.Call("in-outerf-before"); await runner.WhenAsync("the inner step fails", null, null, "When "); await Api.Call("in-outerf-after"); }
    [When("the inner step fails")] public void InnerFails() { Api.CallSync("in-innerf"); throw new InvalidOperationException("the inner step threw"); }
    [When("a step runs an undefined one")] public async Task RunsUndefined() { await runner.WhenAsync("nothing binds this inner step", null, null, "When "); await Api.Call("in-outeru-after"); }
    [When("a step method runs a gherkin step")] public async Task SM() { await Api.Call("in-SM-before"); await StepRuns(); await Api.Call("in-SM-after"); }
    [Step] async Task StepRuns() { await Api.Call("in-StepRuns-before"); await runner.WhenAsync("the inner step", null, null, "When "); await Api.Call("in-StepRuns-after"); }
    [When("a step runs two others")] public async Task RunsTwo() { await runner.WhenAsync("the inner step", null, null, "When "); await Api.Call("in-between-inners"); await runner.WhenAsync("the inner step", null, null, "When "); await Api.Call("in-after-inners"); }

    // ---- flows
    [Given("a step runs two sub steps at once")] public async Task TwoAtOnce() { await Task.WhenAll(K1(), K2()); await Api.Call("in-TwoAtOnce-after"); }
    [Step] async Task K1() { await Task.Delay(5); await Api.Call("in-K1"); await Task.Delay(30); }
    [Step] async Task K2() { await Task.Delay(15); await Api.Call("in-K2"); await Task.Delay(30); }
    [Given("a step starts a task")] public void StartsTask() { _go = new TaskCompletionSource(); var go = _go; _started = Task.Run(async () => { await go.Task; await Api.Call("detached-from-given"); }); }
    [When("the task is released")] public async Task Release() { _go!.SetResult(); await _started!; await Api.Call("in-release"); }
    [Given("a step leaves a sub step running")] public async Task Leaves() { _go = new TaskCompletionSource(); _started = Left(_go); await Api.Call("in-leaves"); }
    [Step] async Task Left(TaskCompletionSource go) { await go.Task; await Api.Call("in-Left-after-given-ended"); }
    [When("the left sub step calls")] public async Task LeftCalls() { _go!.SetResult(); await _started!; await Api.Call("in-leftcalls"); }

    // ---- bypass
    [Given("a step with a sync bypass")] public void SyncBypass() { Api.CallSync("in-syncbypass-before"); MaybeS(); Api.CallSync("in-syncbypass-after"); SubOk(); Api.CallSync("in-syncbypass-end"); }
    [Step(SkipIf = nameof(Yes), SkipReason = "no")] void MaybeS() { Api.CallSync("never"); }
    [Given("a step with an async bypass")] public async Task AsyncBypass() { await Api.Call("in-asyncbypass-before"); await MaybeA(); await Api.Call("in-asyncbypass-after"); SubOk(); await Api.Call("in-asyncbypass-end"); }
    [Step(SkipIf = nameof(Yes), SkipReason = "no")] async Task MaybeA() { await Api.Call("never"); }
    [Given("a step with an async bypass then a failure")] public async Task AsyncBypassF() { await MaybeA(); await Api.Call("in-asyncbypassf-after"); Track.That(() => Assert.True(true), "assert-after-async-bypass"); throw new InvalidOperationException("failed after an async bypass"); }

    // ---- attachments and assertions
    [Given("a step attaches")] public void Attaches() { output.AddAttachment(WriteTemp("rn-output-helper")); Track.Attachment(WriteTemp("rn-track"), "rn-track"); SubAttach(); }
    [Step] void SubAttach() { Track.Attachment(WriteTemp("rn-sub"), "rn-sub"); }
    [Given("a step asserts")] public void Asserts() { Track.That(() => Assert.True(true), "assert-in-gherkin"); SubAssert(); Track.That(() => Assert.True(true), "assert-in-gherkin-after"); }
    [Step] void SubAssert() { Track.That(() => Assert.True(true), "assert-in-sub"); }
    static string WriteTemp(string name) { var p = Path.Combine(Path.GetTempPath(), name + ".txt"); File.WriteAllText(p, "x"); return p; }
}
