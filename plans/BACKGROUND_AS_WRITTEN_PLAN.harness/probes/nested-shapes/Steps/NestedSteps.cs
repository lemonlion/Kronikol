using Reqnroll;

namespace Shapes.Suite.Steps;

/// <summary>What the matrix cannot see: a step running steps that have arguments, one failing inside another, and hooks that run steps.</summary>
[Binding]
public class NestedSteps(ShapeDriver driver, ScenarioContext scenario, ITestRunner runner)
{
    private string Who => scenario.ScenarioInfo.Title;

    [Given("a composite step passes arguments")]
    public async Task ACompositeStepPassesArguments()
    {
        await driver.Ran_for(Who);
        var table = new Table("sku", "count");
        table.AddRow("APPLE", "3");
        await runner.GivenAsync("the basket is listed", null, table);
        await runner.GivenAsync("the note \"kept\" says", "a doc string", null);
    }

    [Given("the basket is listed")]
    public Task TheBasketIsListed(Table table) => driver.Ran_for(Who + " rows:" + table.RowCount);

    [Given("the note \"(.*)\" says")]
    public Task TheNoteSays(string name, string body) => driver.Ran_for(Who + " " + name + ":" + body);

    [Given("a composite step runs two steps")]
    public async Task ACompositeStepRunsTwoSteps()
    {
        await driver.Ran_for(Who);
        await runner.GivenAsync("the first inner step");
        await runner.GivenAsync("the second inner step");
    }

    [Given("the first inner step")]
    public async Task TheFirstInnerStep()
    {
        await driver.Ran_for(Who);
        if (Environment.GetEnvironmentVariable("KRK_MODE") == "fail-nested")
            throw new InvalidOperationException("fail-nested failed in " + Who);
    }

    [Given("the second inner step")]
    public Task TheSecondInnerStep() => driver.Ran_for(Who);

    [Given("the hook prepares")]
    public Task TheHookPrepares() => driver.Ran_for(Who);

    [BeforeScenario("hook-runs-gherkin", Order = 100)]
    public Task AHookRunsAGherkinStep() => runner.GivenAsync("the hook prepares");

    [BeforeScenario("hook-runs-woven", Order = 100)]
    public Task AHookRunsAWovenStep() => driver.Ran_for(Who + " hook");
}
