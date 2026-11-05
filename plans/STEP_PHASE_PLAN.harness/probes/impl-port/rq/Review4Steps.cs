using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using PhaseProbe;
using Reqnroll;
using Reqnroll.BoDi;

namespace PhaseProbe.Rq;

[Binding]
public class R4Bindings(Calls calls, ITestRunner runner, ScenarioContext scenario)
{
    private string Id => ReqNRollTestContext.CurrentTestInfo!.Value.Id;

    [GivenStep] public async Task WovenGiven(string l) => await calls.Make(l);
    [WhenStep] public async Task WovenWhen(string l) => await calls.Make(l);
    [WhenStep] public void WovenWhenSync(string l) => calls.Make(l).GetAwaiter().GetResult();

    [StepDefinition("a woven given runs inside then a call named (.*)")]
    public async Task W1(string l) { await WovenGiven(l + "-wg"); await calls.Make(l); }

    [StepDefinition("a woven when runs inside then a call named (.*)")]
    public async Task W2(string l) { await WovenWhen(l + "-ww"); await calls.Make(l); }

    [StepDefinition("a nested gherkin given then a call named (.*)")]
    public async Task W3(string l) { await runner.GivenAsync($"a call named {l}-nested"); await calls.Make(l); }

    [StepDefinition("a nested gherkin when then a call named (.*)")]
    public async Task W4(string l) { await runner.WhenAsync($"a call named {l}-nested"); await calls.Make(l); }

    [StepDefinition("a fork outlives this step with a call named (.*)")]
    public void W5(string l) { scenario["fork"] = Task.Run(async () => { await Task.Delay(25); await calls.Make(l); }); }

    [StepDefinition("the fork ends")]
    public async Task W6() { await (Task)scenario["fork"]; }

    [StepDefinition("a call on a flow forked here named (.*)")]
    public async Task W7(string l) { await Task.Run(() => calls.Make(l)); }

    [StepDefinition("a hand step left open with keyword (.*) then a call named (.*)")]
    public async Task W8(string keyword, string l) { StepCollector.StartStep(Id, keyword, "left open", null, null); await calls.Make(l); }
}

[Binding]
public class R4Hooks(IObjectContainer container, ScenarioContext scenario)
{
    private string Id => ReqNRollTestContext.CurrentTestInfo!.Value.Id;
    private Calls C => container.Resolve<Calls>();
    [GivenStep] public async Task HookGiven(string l) => await C.Make(l);
    [WhenStep] public async Task HookWhen(string l) => await C.Make(l);
    [ThenStep] public async Task HookThen(string l) => await C.Make(l);

    [BeforeScenario("hand-open-in-hook", Order = 5)]
    public void HandOpen() => StepCollector.StartStep(Id, "Given", "the environment, by hand", null, null);

    [AfterScenario("hand-open-in-hook", Order = 5)]
    public void HandClose() => StepCollector.CompleteStep(Id, true);

    [BeforeScenario("woven-given-in-hook", Order = 5)]
    public async Task WG() => await HookGiven("r4-hook-given");

    [BeforeScenario("woven-when-in-hook", Order = 5)]
    public async Task WW() => await HookWhen("r4-hook-when");

    [AfterScenario("woven-given-after", Order = 5)]
    public async Task AG() { await HookGiven("r4-after-given"); await C.Make("r4-after-plain"); }

    [AfterStep("woven-when-after-step", Order = 5)]
    public async Task AS() => await HookWhen("r4-after-step-" + scenario.StepContext.StepInfo.StepDefinitionType);

    [BeforeStep("woven-given-before-step", Order = 5)]
    public async Task BS() => await HookGiven("r4-before-step-" + scenario.StepContext.StepInfo.StepDefinitionType);
}
