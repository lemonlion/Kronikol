using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Probe;
using Reqnroll;

[assembly: TrackSteps]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Probe.Reqnroll;

/// <summary>A fresh handler per scenario for boundary shapes; three clients so every label shows what the per-phase options keep.</summary>
public sealed class Calls
{
    private static HttpClient New(bool s = true, bool a = true) => new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = s, TrackDuringAction = a
    }) { InnerHandler = new Answer() });

    public readonly HttpClient Http = New();
    private readonly HttpClient _noSetup = New(s: false);
    private readonly HttpClient _noAction = New(a: false);
    private readonly MessageTracker _bus = new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = ReqNRollTestTrackingMessageHandlerOptions.TestInfoFetcher });

    public async Task Make(string label)
    {
        (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
        await _noSetup.GetAsync($"http://probe.local/{label}-nosetup");
        await _noAction.GetAsync($"http://probe.local/{label}-noaction");
    }

    public void Message(string label) => _bus.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
}

public sealed class Woven(Calls calls)
{
    [GivenStep] public async Task GivenAsync(string label) => await calls.Make(label);
    [WhenStep] public async Task WhenAsync(string label) => await calls.Make(label);
}

public sealed record Order(string Customer);

public sealed class CallingConstructor
{
    public readonly string Note;
    public CallingConstructor(Calls calls) { calls.Make("ctor-of-binding-dependency").GetAwaiter().GetResult(); Note = "made"; }
}

[Binding]
public class CtorBinding(CallingConstructor made, Calls calls)
{
    [StepDefinition("a binding class with a calling constructor is used, named (.*)")]
    public Task UsesCtor(string label) => calls.Make(label + made.Note);
}

[Binding]
public class PhaseSteps(Calls calls, Woven woven, ITestRunner runner)
{
    [StepDefinition("a call named (.*)")]
    public Task ACall(string label) => calls.Make(label);

    [StepDefinition("a message named (.*)")]
    public void AMessage(string label) => calls.Message(label);

    [StepDefinition("a failing call named (.*)")]
    public async Task AFailing(string label) { await calls.Make(label); throw new InvalidOperationException("planned"); }

    [When("a binding runs a nested Gherkin Given named (.*)")]
    public async Task NestedGiven(string label)
    {
        await calls.Make($"{label}-before-nested");
        await runner.GivenAsync($"a call named {label}-in-nested");
        await calls.Make($"{label}-after-nested");
    }

    [Given("a binding runs a nested Gherkin When named (.*)")]
    public async Task NestedWhen(string label)
    {
        await calls.Make($"{label}-before-nested");
        await runner.WhenAsync($"a call named {label}-in-nested");
        await calls.Make($"{label}-after-nested");
    }

    [When("a binding starts with a nested Gherkin Given named (.*)")]
    public async Task StartsWithNestedGiven(string label)
    {
        await runner.GivenAsync($"a call named {label}-in-nested");
        await calls.Make($"{label}-after-nested-1");
        await calls.Make($"{label}-after-nested-2");
    }

    [StepArgumentTransformation("(.*)-customer(.*)")]
    public Order MakeOrder(string a, string b)
    {
        calls.Make($"{a}-transform{b}").GetAwaiter().GetResult();
        return new Order(a + b);
    }

    [When("an order for customer (.*) is placed")]
    public Task Placed(Order order) => calls.Make($"{order.Customer}-placed");

    [StepDefinition("a woven When runs in an And, named (.*)")]
    public Task WovenWhen(string label) => woven.WhenAsync(label);

    [StepDefinition("a woven Given runs in an And, named (.*)")]
    public async Task WovenGiven(string label)
    {
        await woven.GivenAsync(label);
        await calls.Make($"{label}-after-woven");
    }

    [When("a binding calls StartSetup by hand, named (.*)")]
    public async Task HandStartSetup(string label)
    {
        await calls.Make($"{label}-before");
        TrackingDiagramOverride.StartSetup();
        await calls.Make($"{label}-after");
    }

    [Given("a binding calls StartAction by hand, named (.*)")]
    public async Task HandStartAction(string label)
    {
        await calls.Make($"{label}-before");
        TrackingDiagramOverride.StartAction();
        await calls.Make($"{label}-after");
    }

    [When("a binding writes TestPhaseContext by hand, named (.*)")]
    public void HandWrite(string label)
    {
        calls.Make($"{label}-before").GetAwaiter().GetResult();
        TestPhaseContext.Current = TestPhase.Setup;
        calls.Make($"{label}-after").GetAwaiter().GetResult();
    }

    [StepDefinition("a plain call named (.*)")]
    public async Task APlainCall(string label) => (await calls.Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();

    [StepDefinition("two plain calls named (.*)")]
    public async Task TwoPlainCalls(string label)
    {
        await APlainCall(label + "-1");
        calls.Message(label + "-msg");
        await APlainCall(label + "-2");
    }

    [Given("a binding runs a nested Gherkin When with two calls named (.*)")]
    public async Task NestedWhenTwoCalls(string label)
    {
        await APlainCall($"{label}-before-nested");
        await runner.WhenAsync($"two plain calls named {label}-in-nested");
        await APlainCall($"{label}-after-nested");
        calls.Message($"{label}-after-nested-msg");
    }

    [Given("a binding leaves a keyword-free hand step open named (.*)")]
    public async Task LeavesHandStepOpen(string label)
    {
        await calls.Make($"{label}-first");
        // What a hand-made helper does when its body throws and it has no finally, or a Complete on another path was missed.
        StepCollector.StartStep(ReqNRollTestContext.CurrentTestInfo!.Value.Id, null, "a sub-step never closed", null, null);
        await calls.Make($"{label}-in-open-sub");
    }

    [StepDefinition("a sync binding leaves a keyword-free hand step open named (.*)")]
    public void SyncLeavesHandStepOpen(string label)
    {
        calls.Make($"{label}-first").GetAwaiter().GetResult();
        StepCollector.StartStep(ReqNRollTestContext.CurrentTestInfo!.Value.Id, null, "a sub-step never closed", null, null);
        calls.Make($"{label}-in-open-sub").GetAwaiter().GetResult();
    }

    [AfterTestRun]
    public static void Report()
    {
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "reqnroll");
        if (Environment.GetEnvironmentVariable("PROBE_REPORT") == "1")
            ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
    }
}

[Binding]
public class Hooks(Calls calls, ScenarioContext scenario)
{
    private int _n;
    private string L(string hook) => $"r06-hook-{hook}-{++_n}";
    private bool On => scenario.ScenarioInfo.Tags.Contains("hooks");

    [BeforeScenario] public Task BeforeScenario() => On ? calls.Make(L("beforescenario")) : Task.CompletedTask;
    [BeforeScenarioBlock] public Task BeforeBlock() => On ? calls.Make(L("beforeblock")) : Task.CompletedTask;
    [AfterScenarioBlock] public Task AfterBlock() => On ? calls.Make(L("afterblock")) : Task.CompletedTask;
    [BeforeStep] public Task BeforeStep() => On ? calls.Make(L("beforestep")) : Task.CompletedTask;
    [AfterStep] public Task AfterStep() => On ? calls.Make(L("afterstep")) : Task.CompletedTask;
    [AfterScenario] public async Task AfterScenario()
    {
        if (On) await calls.Make(L("afterscenario"));
        if (scenario.ScenarioInfo.Tags.Contains("aftercall")) await calls.Make("r10-afterscenario");
    }
}
