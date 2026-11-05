using Kronikol.ReqNRoll;
using Reqnroll;

namespace Example.Api.Tests.Component.ReqNRollTUnit.StepDefinitions;

/// <summary>
/// What runs the adapter's own hooks for real: a Background as written, a step that runs steps, and a failure in either. A step
/// that fails does so only when the Integration project's ReqNRollDuplicateStepsTests sets KRONIKOL_EXAMPLE_STEP_FAILURES.
/// </summary>
[Binding]
public class StepsAsWrittenStepDefinitions(ScenarioContext scenario, ITestRunner runner)
{
    private static bool AskedToFail => Environment.GetEnvironmentVariable("KRONIKOL_EXAMPLE_STEP_FAILURES") == "true";

    private static void FailIfAsked(string what)
    {
        if (AskedToFail)
            throw new InvalidOperationException(what);
    }

    [StepDefinition("a step that passes")]
    [StepDefinition("another step that passes")]
    [StepDefinition("a third step that passes")]
    [StepDefinition("a rule's step that passes")]
    public void AStepThatPasses()
    {
    }

    [StepDefinition("a step runs a step with a table and a doc string")]
    public async Task AStepRunsAStepWithATableAndADocString()
    {
        var table = new Table("sku");
        table.AddRow("APPLE");
        await runner.GivenAsync("an inner step takes a table", null, table);
        await runner.GivenAsync("an inner step takes a doc string", "a doc string", null);
    }

    [StepDefinition("an inner step takes a table")]
    public void AnInnerStepTakesATable(Table table)
    {
    }

    [StepDefinition("an inner step takes a doc string")]
    public void AnInnerStepTakesADocString(string docString)
    {
    }

    [StepDefinition("a Background step that fails when asked")]
    public void ABackgroundStepThatFails() => FailIfAsked("a Background step failed");

    [StepDefinition("a step runs a step that fails when asked")]
    public Task AStepRunsAStepThatFails() => runner.GivenAsync("an inner step that fails when asked");

    [StepDefinition("an inner step that fails when asked")]
    public void AnInnerStepThatFails() => FailIfAsked("an inner step failed");

    [StepDefinition("a step runs a step whose argument does not convert when asked")]
    public Task AStepRunsAStepWhoseArgumentDoesNotConvert() =>
        runner.GivenAsync($"a step takes the number \"{(AskedToFail ? "not a number" : "1")}\"");

    [StepDefinition("a step takes the number \"(.*)\"")]
    public void AStepTakesTheNumber(int number)
    {
    }

    [StepDefinition("a step a hook stops when asked")]
    [StepDefinition("a step an AfterStep hook fails when asked")]
    public void AStepAHookActsOn()
    {
    }

    [StepDefinition("a step runs a step a hook stops when asked")]
    public Task AStepRunsAStepAHookStops() => runner.GivenAsync("a step a hook stops when asked");

    [StepDefinition("a step runs a step an AfterStep hook fails when asked")]
    public Task AStepRunsAStepAnAfterStepHookFails() => runner.GivenAsync("a step with a doc string an AfterStep hook fails when asked", "its doc string", null);

    [StepDefinition("a step with a doc string an AfterStep hook fails when asked")]
    public void AStepWithADocStringAnAfterStepHookFails(string docString)
    {
    }

    private string StepText => scenario.StepContext.StepInfo.Text;

    // The one order that can run ahead of the adapter's own BeforeStep, whose Order is int.MinValue.
    [BeforeStep("first-before-step-fails", Order = int.MinValue)]
    public void FirstBeforeStepFails()
    {
        if (StepText == "a step a hook stops when asked")
            FailIfAsked("a BeforeStep hook ahead of the adapter's failed");
    }

    [AfterStep("after-step-fails")]
    [AfterStep("after-scenario-runs-a-step")]
    public void AfterStepFails()
    {
        if (StepText is "a step an AfterStep hook fails when asked" or "a step with a doc string an AfterStep hook fails when asked")
            FailIfAsked("an AfterStep hook failed");
    }

    // The list a suite can read holds every step that ran, a step a step ran among them.
    // A step an AfterStep hook kept the adapter from closing is closed ahead of this hook, so this hook's time and work are not that step's.
    [AfterScenario("after-step-fails")]
    public void FindsTheStepAHookLeftOpen()
    {
        var steps = (List<ReqNRollStepInfo>)scenario[ReqNRollConstants.StepsCollectionKey];
        if (AskedToFail && !steps.Any(step => step.Text is "a step an AfterStep hook fails when asked" or "a step with a doc string an AfterStep hook fails when asked"))
            throw new InvalidOperationException("The step a hook left open is not in the list.");
    }

    // A suite's hook can run a step of its own, and an AfterStep hook can stop that one like any other.
    [AfterScenario("after-scenario-runs-a-step")]
    public async Task RunsAStepAnAfterStepHookFails()
    {
        try
        {
            await runner.GivenAsync("a step an AfterStep hook fails when asked");
        }
        catch (Exception) when (AskedToFail)
        {
        }
    }

    [AfterScenario("reads-the-public-list")]
    public void ReadsThePublicList()
    {
        var steps = (List<ReqNRollStepInfo>)scenario[ReqNRollConstants.StepsCollectionKey];
        if (!steps.Any(step => step.Text == "an inner step takes a table"))
            throw new InvalidOperationException("The steps list lost a step a step ran.");
        if (scenario[ReqNRollConstants.StepStopwatchKey] is not System.Diagnostics.Stopwatch)
            throw new InvalidOperationException("The public step stopwatch is gone.");
        var ranThem = steps.Single(step => step.Text == "a step runs a step with a table and a doc string");
        var lastItRan = steps.Single(step => step.Text == "an inner step takes a doc string");
        if (!(ranThem.Duration > lastItRan.Duration))
            throw new InvalidOperationException("A step that ran a step took that step's time for its own.");
    }
}
