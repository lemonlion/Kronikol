using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using PhaseProbe;
using Reqnroll;
using Reqnroll.BoDi;

[assembly: TrackSteps]

namespace PhaseProbe.Reqnroll;

public class SubSteps(Calls calls)
{
    [Step] public async Task PlainAsync(string label) => await calls.Make(label);
    [GivenStep] public async Task GivenAsync(string label) => await calls.Make(label);
    [GivenStep] public void GivenSync(string label) => calls.Make(label).GetAwaiter().GetResult();
    [GivenStep] public async Task GivenThrows(string label) { await calls.Make(label); throw new InvalidOperationException("caught"); }
    [WhenStep] public async Task WhenAsync(string label) => await calls.Make(label);
    [ThenStep] public async Task ThenAsync(string label) => await calls.Make(label);
    [ButWhenStep] public async Task ButWhenAsync(string label) => await calls.Make(label);
    [Step] public async Task PlainThenWhen(string label) { await WhenAsync($"{label}-inner-when"); await calls.Make($"{label}-after-inner-when"); }
}

[Binding]
public class PhaseSteps(Calls calls, SubSteps sub)
{
    [StepDefinition("a call named (.*)")]
    public Task ACall(string label) => calls.Make(label);

    [StepDefinition("a message named (.*)")]
    public void AMessage(string label) => calls.Message(label);

    [When("the customer applies a discount through a Given sub-step")]
    public async Task WhenTheCustomerAppliesADiscountThroughAGivenSubStep()
    {
        await sub.GivenAsync("r00-discounts-SAVE10");
        await sub.PlainAsync("r00-orders");
    }

    [When("a When step calls an async Given sub-step, then calls (.*)")]
    public async Task WhenAsyncGiven(string label)
    {
        await calls.Make($"{label}-before-sub");
        await sub.GivenAsync($"{label}-in-sub");
        await calls.Make($"{label}-after-sub");
    }

    [When("a When step calls a sync Given sub-step, then calls (.*)")]
    public async Task WhenSyncGiven(string label)
    {
        await calls.Make($"{label}-before-sub");
        sub.GivenSync($"{label}-in-sub");
        await calls.Make($"{label}-after-sub");
    }

    [Given("a Given step calls an async When sub-step, then calls (.*)")]
    public async Task GivenAsyncWhen(string label)
    {
        await calls.Make($"{label}-before-sub");
        await sub.WhenAsync($"{label}-in-sub");
        await calls.Make($"{label}-after-sub");
    }

    [Given("a Given step calls an async Then sub-step, then calls (.*)")]
    public async Task GivenAsyncThen(string label)
    {
        await calls.Make($"{label}-before-sub");
        await sub.ThenAsync($"{label}-in-sub");
        await calls.Make($"{label}-after-sub");
    }

    [Given("a Given step calls a ButWhen sub-step, then calls (.*)")]
    public async Task GivenButWhen(string label)
    {
        await calls.Make($"{label}-before-sub");
        await sub.ButWhenAsync($"{label}-in-sub");
        await calls.Make($"{label}-after-sub");
    }

    [When("a When step calls a keyword-free sub-step, then calls (.*)")]
    public async Task WhenPlain(string label)
    {
        await calls.Make($"{label}-before-sub");
        await sub.PlainAsync($"{label}-in-sub");
        await calls.Make($"{label}-after-sub");
    }

    [When("a When binding woven as a GivenStep calls (.*)")]
    [GivenStep]
    public async Task WhenWovenAsGiven(string label)
    {
        await calls.Make($"{label}-in-binding");
    }

    [When("a When step catches a failing Given sub-step, then calls (.*)")]
    public async Task WhenCatches(string label)
    {
        try { await sub.GivenThrows($"{label}-in-sub"); } catch (InvalidOperationException) { }
        await calls.Make($"{label}-after-sub");
    }

    [Given("a Given step nests When under a keyword-free sub-step, then calls (.*)")]
    public async Task GivenNests(string label)
    {
        await sub.PlainThenWhen($"{label}-in-sub");
        await calls.Make($"{label}-after-sub");
    }
}

[Binding]
public class Hooks
{
    private static HttpClient? _shared;

    private static HttpClient Client(bool trackDuringSetup) => new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = trackDuringSetup,
    }) { InnerHandler = new AnswerEverything() });

    [BeforeScenario(Order = 0)]
    public void Register(IObjectContainer container, ScenarioContext scenario)
    {
        var tags = scenario.ScenarioInfo.Tags;
        var client = tags.Contains("shared-handler") ? _shared ??= Client(true) : Client(!tags.Contains("no-setup-tracking"));
        container.RegisterInstanceAs(client);
        container.RegisterInstanceAs(new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Probe",
            ServiceName = "Bus",
            CurrentTestInfoFetcher = ReqNRollTestTrackingMessageHandlerOptions.TestInfoFetcher,
        }));
    }

    private static StepTrackingOptions? _optionsBefore;

    [BeforeScenario("no-step-attributes", Order = 1)]
    public void StepAttributesOff()
    {
        if (Environment.GetEnvironmentVariable("PHASE_PROBE_PARALLEL") is not null)
            return; // A process-wide option, which would reach the scenarios running beside this one.
        _optionsBefore = StepCollector.Options;
        StepCollector.Options = new StepTrackingOptions { WhenTriggersAction = false };
    }

    [AfterScenario("no-step-attributes")]
    public void StepAttributesOn() => StepCollector.Options = _optionsBefore ?? StepCollector.Options;

    [BeforeScenario("hook-step", Order = 1)]
    public Task Seed(SubSteps sub) => sub.GivenAsync("r15-in-hook");

    [BeforeScenario("hook-call", Order = 1)]
    public Task BeforeHookCall(Calls calls) => calls.Make("r17-in-before-scenario");

    [AfterScenario("hook-call")]
    public Task AfterHookCall(Calls calls) => calls.Make("r17-in-after-scenario");

    [AfterTestRun]
    public static void Report()
    {
        Dump.Write(Environment.GetEnvironmentVariable("PHASE_PROBE_NAME") ?? "reqnroll-tunit");
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
    }
}
