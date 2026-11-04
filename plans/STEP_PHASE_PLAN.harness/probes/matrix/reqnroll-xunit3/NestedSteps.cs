using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using PhaseProbe;
using Reqnroll;

namespace PhaseProbe.Reqnroll;

/// <summary>Bindings that run a Gherkin step through the injected ITestRunner, inside a step that is already running.</summary>
[Binding]
public class NestedSteps(Calls calls, SubSteps sub, ITestRunner runner)
{
    [When("a When binding runs a nested Gherkin Given, then calls (.*)")]
    public async Task WhenRunsGiven(string label)
    {
        await calls.Make($"{label}-before-nested");
        await runner.GivenAsync($"a call named {label}-nested-given");
        await calls.Make($"{label}-after-nested");
    }

    [When("a When binding runs a nested Gherkin When, then calls (.*)")]
    public async Task WhenRunsWhen(string label)
    {
        await calls.Make($"{label}-before-nested");
        await runner.WhenAsync($"a call named {label}-nested-when");
        await calls.Make($"{label}-after-nested");
    }

    [Given("a Given binding runs a nested Gherkin When, then calls (.*)")]
    public async Task GivenRunsWhen(string label)
    {
        await calls.Make($"{label}-before-nested");
        await runner.WhenAsync($"a call named {label}-nested-when");
        await calls.Make($"{label}-after-nested");
    }

    [When("a When binding runs nested Gherkin And and But, then calls (.*)")]
    public async Task WhenRunsAndAndBut(string label)
    {
        await runner.AndAsync($"a call named {label}-nested-and");
        await calls.Make($"{label}-between");
        await runner.ButAsync($"a call named {label}-nested-but");
        await calls.Make($"{label}-after-nested");
    }

    [Given("a Given binding with a woven sub-step calls (.*)")]
    public async Task GivenWithWovenSubStep(string label)
    {
        await calls.Make($"{label}-in-nested");
        await sub.GivenAsync($"{label}-in-woven");
        await calls.Make($"{label}-after-woven");
    }

    [When("a When binding runs a nested Gherkin Given with a woven sub-step, then calls (.*)")]
    public async Task WhenRunsGivenWithWoven(string label)
    {
        await calls.Make($"{label}-before-nested");
        await runner.GivenAsync($"a Given binding with a woven sub-step calls {label}");
        await calls.Make($"{label}-after-nested");
    }

    [Given("a Given binding runs a woven When on an unflowed thread, then calls (.*)")]
    public async Task GivenWithWovenWhenOnThread(string label)
    {
        await calls.Make($"{label}-before-thread");
        var (name, id) = ReqNRollTestTrackingMessageHandlerOptions.TestInfoFetcher();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using (TestIdentityScope.Begin(name, id))
                    sub.WhenAsync($"{label}-when-on-thread").GetAwaiter().GetResult();
            }
            catch (Exception e) { failure = e; }
        });
        thread.UnsafeStart();
        thread.Join();
        if (failure is not null)
            throw failure;
        await calls.Make($"{label}-after-thread");
    }

    [When("a sync When binding blocks on a nested Gherkin Given, then calls (.*)")]
    public void SyncWhenRunsGiven(string label)
    {
        calls.Make($"{label}-before-nested").GetAwaiter().GetResult();
        runner.GivenAsync($"a call named {label}-nested-given").GetAwaiter().GetResult();
        calls.Make($"{label}-after-nested").GetAwaiter().GetResult();
    }
}

/// <summary>Says each hook ran, and what its flow sees, with nothing of Kronikol's in between.</summary>
[Binding]
public class HookNotes(ScenarioContext scenario)
{
    [AfterScenario("after-scenario-when", Order = 0)]
    public Task AfterScenarioWhen(SubSteps sub) => sub.WhenAsync($"{scenario.ScenarioInfo.Title[..3].ToLowerInvariant()}-teardown-when");

    [BeforeStep(Order = int.MaxValue)]
    public void Before() => FlowTrace.Note($"hook:before-step {scenario.StepContext.StepInfo.Text}");

    [AfterStep(Order = int.MinValue)]
    public void After() => FlowTrace.Note($"hook:after-step {scenario.StepContext.StepInfo.Text}");
}
