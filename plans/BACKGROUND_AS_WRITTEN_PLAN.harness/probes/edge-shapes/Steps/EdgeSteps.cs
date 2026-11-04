using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.UnitTestProvider;

namespace Shapes.Suite.Steps;

/// <summary>How a scenario can stop or read oddly: what the matrix's five modes do not reach.</summary>
[Binding]
public class EdgeSteps(ShapeDriver driver, ScenarioContext scenario, ITestRunner runner, IUnitTestRuntimeProvider unitTest)
{
    private string Who => scenario.ScenarioInfo.Title;

    [StepDefinition("an edge step")]
    public Task AnEdgeStep() => driver.Ran_for(Who);

    [StepDefinition("an edge step \"(.*)\"")]
    public Task AnEdgeStepNamed(string name) => driver.Ran_for(Who + " " + name);

    [StepDefinition("^an awkward step .*$")]
    public Task AnAwkwardStep() => driver.Ran_for(Who);

    [StepDefinition("a pending step")]
    public void APendingStep() => throw new PendingStepException();

    [StepDefinition("a step ignores the test at run time")]
    public void AStepIgnoresTheTest() => unitTest.TestIgnore("ignored at run time");

    [StepDefinition("the hook fails before this step")]
    public Task TheHookFailsBefore() => driver.Ran_for(Who);

    [StepDefinition("the hook fails after this step")]
    public Task TheHookFailsAfter() => driver.Ran_for(Who);

    [StepDefinition("a composite step runs an undefined step")]
    public async Task ACompositeStepRunsAnUndefinedStep()
    {
        await driver.Ran_for(Who);
        await runner.GivenAsync("nobody defined this inner step");
    }

    [StepDefinition("a composite step runs a pending step")]
    public async Task ACompositeStepRunsAPendingStep()
    {
        await driver.Ran_for(Who);
        await runner.GivenAsync("a pending step");
    }

    [StepDefinition("a composite step runs a step that fails and carries on")]
    public async Task ACompositeStepRunsAStepThatFails()
    {
        await driver.Ran_for(Who);
        await runner.GivenAsync("an inner step that fails");
        await driver.Ran_for(Who + " after the inner step");
    }

    [StepDefinition("a composite step runs the step a hook stops")]
    public async Task ACompositeStepRunsTheStepAHookStops()
    {
        await driver.Ran_for(Who);
        await runner.GivenAsync("the hook fails before this step");
    }

    [StepDefinition("a composite step runs the step whose hook fails after it")]
    public async Task ACompositeStepRunsTheStepWhoseHookFailsAfterIt()
    {
        await driver.Ran_for(Who);
        await runner.GivenAsync("the hook fails after this step");
    }

    [StepDefinition("a composite step runs a woven helper")]
    public async Task ACompositeStepRunsAWovenHelper()
    {
        await A_woven_helper();
        await runner.GivenAsync("an edge note says", "a doc string", null);
    }

    [Step]
    public async Task A_woven_helper()
    {
        var table = new Table("sku", "count");
        table.AddRow("APPLE", "3");
        await runner.GivenAsync("an edge table is listed", null, table);
    }

    [StepDefinition("an edge table is listed")]
    public Task AnEdgeTableIsListed(Table table) => driver.Ran_for(Who + " rows:" + table.RowCount);

    [StepDefinition("an edge note says")]
    public Task AnEdgeNoteSays(string body) => driver.Ran_for(Who + " note:" + body);

    [StepDefinition("a composite step runs a step whose argument does not convert")]
    public async Task ACompositeStepRunsAStepWhoseArgumentDoesNotConvert()
    {
        await driver.Ran_for(Who);
        await runner.GivenAsync("an edge number \"not a number\"");
    }

    [StepDefinition("an edge number \"(.*)\"")]
    public void AnEdgeNumber(int number) => _ = number;

    [StepDefinition("a background step fails for row b")]
    public void ABackgroundStepFailsForRowB()
    {
        if (scenario.ScenarioInfo.Arguments["row"]?.ToString() == "b")
            throw new InvalidOperationException("a background step failed for row b");
    }

    [StepDefinition("a composite step runs a passing step and one whose hook fails")]
    public async Task ACompositeStepRunsAPassingStepAndOneWhoseHookFails()
    {
        await runner.GivenAsync("an edge step");
        await runner.GivenAsync("the hook fails after this step");
    }

    [StepDefinition("an inner step that fails")]
    public void AnInnerStepThatFails() => throw new InvalidOperationException("an inner step failed in " + Who);
}

[Binding]
public class EdgeHooks(ScenarioContext scenario, ITestRunner runner)
{
    private string Text => scenario.StepContext.StepInfo.Text;

    [BeforeStep("before-step-fails")]
    public void BeforeStepFails()
    {
        if (Text == "the hook fails before this step")
            throw new InvalidOperationException("a BeforeStep hook failed");
    }

    [AfterStep("after-step-fails")]
    public void AfterStepFails()
    {
        if (Text == "the hook fails after this step")
            throw new InvalidOperationException("an AfterStep hook failed");
    }

    // The one order that can run ahead of the adapter's own BeforeStep, whose Order is int.MinValue.
    [BeforeStep("first-before-step-fails", Order = int.MinValue)]
    public void FirstBeforeStepFails()
    {
        if (Text == "the hook fails before this step")
            throw new InvalidOperationException("a BeforeStep hook at int.MinValue failed");
    }

    [AfterScenario("after-scenario-fails")]
    public void AfterScenarioFails() => throw new InvalidOperationException("an AfterScenario hook failed");

    // Between a Background's Given and the scenario's When: no step is under way, so none can be marked failed.
    [BeforeScenarioBlock("block-hook-fails")]
    public void BlockHookFails()
    {
        if (scenario.CurrentScenarioBlock == ScenarioBlock.When)
            throw new InvalidOperationException("a block hook failed");
    }

    [AfterScenario("runs-a-step-after", Order = 0)]
    public Task RunsAStepAfter() => runner.ThenAsync("an edge step \"from an AfterScenario hook\"");
}
