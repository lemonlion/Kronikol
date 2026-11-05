using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using PhaseProbe;
using Reqnroll;
using Reqnroll.BoDi;
using Reqnroll.Bindings;
using System.Runtime.CompilerServices;

[assembly: TrackSteps]

namespace PhaseProbe.Rq;

[Binding]
public class PhaseSteps(Calls calls, ScenarioContext scenario)
{
    private string Id => ReqNRollTestContext.CurrentTestInfo!.Value.Id;

    [StepDefinition("the call (.*) comes before StartAction")]
    public async Task CallThenStartAction(string l) { await calls.Make(l); TrackingDiagramOverride.StartAction(); }

    [StepDefinition("StartAction then a call named (.*)")]
    public async Task StartActionThenCall(string l) { TrackingDiagramOverride.StartAction(); await calls.Make(l); }

    [StepDefinition("the call (.*) comes before StartAction and the call (.*)")]
    public async Task CallStartActionCall(string a, string b) { await calls.Make(a); TrackingDiagramOverride.StartAction(); await calls.Make(b); }

    [StepDefinition("StartSetup then a call named (.*) then StartAction then a call named (.*)")]
    public async Task SetupCallActionCall(string a, string b) { TrackingDiagramOverride.StartSetup(); await calls.Make(a); TrackingDiagramOverride.StartAction(); await calls.Make(b); }

    [StepDefinition("a call named (.*)")]
    public async Task ACall(string l) => await calls.Make(l);

    [StepDefinition("a message named (.*)")]
    public void AMessage(string l) => calls.Message(l);

    [StepDefinition("a failing step")]
    public void Failing() => throw new InvalidOperationException("this step fails on purpose");

    [StepDefinition("the phase is written Action by hand then a call named (.*)")]
    public async Task HandAction(string l) { TestPhaseContext.Current = TestPhase.Action; await calls.Make(l); }
}

[Binding]
public class Hooks(IObjectContainer container, ScenarioContext scenario)
{
    private static readonly HttpClient SharedHttp = new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });

    private static MessageTracker Tracker(bool setup = true, bool action = true) => new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = ReqNRollTestTrackingMessageHandlerOptions.TestInfoFetcher, TrackDuringSetup = setup, TrackDuringAction = action });

    public static readonly AsyncLocal<StrongBox<string>> CustomStage2 = new();
    public static class CustomStage { private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Values = new(); public static string Value { get => Values.GetValueOrDefault(ReqNRollTestContext.CurrentTestInfo?.Id ?? "", "Given"); set => Values[ReqNRollTestContext.CurrentTestInfo?.Id ?? ""] = value; } }

    private bool Tagged(string tag) => scenario.ScenarioInfo.Tags.Contains(tag) || scenario.ScenarioInfo.CombinedTags.Contains(tag);

    [BeforeScenario(Order = 0)]
    public void Register()
    {
        var setup = !Tagged("no-setup-tracking");
        var action = !Tagged("no-action-tracking");
        HttpClient http;
        if (Tagged("shared-handler"))
            http = SharedHttp;
        else
        {
            var options = new ReqNRollTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = setup, TrackDuringAction = action };
            if (Tagged("no-fetcher"))
                options.CurrentStepTypeFetcher = null;
            if (Tagged("custom-fetcher"))
                options.CurrentStepTypeFetcher = () => CustomStage.Value;
            http = new HttpClient(new TestTrackingMessageHandler(options) { InnerHandler = new AnswerEverything() });
        }
        container.RegisterInstanceAs(new Calls(http, Tracker(setup, action)));
    }

    private Calls C => container.Resolve<Calls>();

    [BeforeScenario("hook-calls", Order = 1)]
    [BeforeScenario("scenario-hook-call", Order = 1)]
    public async Task BeforeScenarioCall() => await C.Make("hook-before-scenario");

    [BeforeScenarioBlock("hook-calls", Order = 1)]
    public async Task BeforeBlockCall() => await C.Make($"hook-before-block-{scenario.CurrentScenarioBlock}");

    [AfterScenarioBlock("hook-calls", Order = 1)]
    public async Task AfterBlockCall() => await C.Make($"hook-after-block-{scenario.CurrentScenarioBlock}");

    [BeforeStep("hook-calls", Order = 1)]
    public async Task BeforeStepCall() => await C.Make($"hook-before-step-{scenario.StepContext.StepInfo.StepDefinitionType}");

    [AfterStep("hook-calls", Order = 1)]
    public async Task AfterStepCall() => await C.Make($"hook-after-step-{scenario.StepContext.StepInfo.StepDefinitionType}");

    [AfterScenario("hook-calls", Order = 1)]
    public async Task AfterScenarioCall() => await C.Make("hook-after-scenario");

    [BeforeScenarioBlock("before-block-explicit", Order = 1)]
    public void BeforeBlockExplicit()
    {
        if (scenario.CurrentScenarioBlock == ScenarioBlock.When)
            TrackingDiagramOverride.StartAction();
    }

    [AfterStep("after-step-explicit", Order = 1)]
    public void AfterStepExplicit()
    {
        if (scenario.StepContext.StepInfo.StepDefinitionType == StepDefinitionType.When)
            TrackingDiagramOverride.StartAction();
    }

    [BeforeStep("before-step-explicit", Order = 1)]
    public void BeforeStepExplicit()
    {
        if (scenario.StepContext.StepInfo.StepDefinitionType == StepDefinitionType.When)
            TrackingDiagramOverride.StartAction();
    }

    [AfterStep("clear-steps", Order = 1)]
    public void ClearAfterGiven()
    {
        if (scenario.StepContext.StepInfo.StepDefinitionType == StepDefinitionType.Given)
            StepCollector.ClearSteps(ReqNRollTestContext.CurrentTestInfo!.Value.Id);
    }

    [AfterTestRun]
    public static void Report()
    {
        Dump.Write("rq");
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true, DiagnosticMode = true });
    }
}

[Binding]
public class SubStepBindings(Calls calls, ITestRunner runner)
{
    [Step] public async Task PlainAsync(string l) => await calls.Make(l);
    [Step] public void PlainSync(string l) => calls.Make(l).GetAwaiter().GetResult();
    [GivenStep] public async Task GivenAsync(string l) => await calls.Make(l);
    [Step] public async Task Throwing(string l) { await calls.Make(l); throw new InvalidOperationException("caught"); }

    [When("the fetcher moves on then a call named (.*)")]
    public async Task Moved(string l) { Hooks.CustomStage.Value = "When"; await calls.Make(l); }

    [When("an async woven sub-step runs then StartAction then a call named (.*)")]
    public async Task A(string l) { await PlainAsync(l + "-sub"); TrackingDiagramOverride.StartAction(); await calls.Make(l); }

    [When("a sync woven sub-step runs then StartAction then a call named (.*)")]
    public async Task B(string l) { PlainSync(l + "-sub"); TrackingDiagramOverride.StartAction(); await calls.Make(l); }

    [When("two woven sub-steps run together then StartAction then a call named (.*)")]
    public async Task C(string l) { await Task.WhenAll(PlainAsync(l + "-sub1"), GivenAsync(l + "-sub2")); TrackingDiagramOverride.StartAction(); await calls.Make(l); }

    [When("a nested gherkin given runs then StartAction then a call named (.*)")]
    public async Task D(string l) { await runner.GivenAsync($"a call named {l}-nested"); TrackingDiagramOverride.StartAction(); await calls.Make(l); }

    [When("a throwing woven sub-step is caught then StartAction then a call named (.*)")]
    public async Task E(string l) { try { await Throwing(l + "-sub"); } catch (InvalidOperationException) { } TrackingDiagramOverride.StartAction(); await calls.Make(l); }

    [When("an unawaited woven sub-step runs then StartAction then a call named (.*)")]
    public async Task F(string l) { var t = PlainAsync(l + "-sub"); TrackingDiagramOverride.StartAction(); await calls.Make(l); await t; }
}
