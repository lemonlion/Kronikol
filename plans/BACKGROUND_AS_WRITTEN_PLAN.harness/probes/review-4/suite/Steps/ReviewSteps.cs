using Kronikol.Tracking;
using Reqnroll;

namespace Shapes.Suite.Steps;

[Binding]
public class ReviewSteps(ShapeDriver driver, ScenarioContext scenario, ITestRunner runner, IReqnrollOutputHelper output)
{
    private string Who => scenario.ScenarioInfo.Title;

    [StepDefinition("an r step")]
    public Task AnRStep() => driver.Ran_for(Who);

    [StepDefinition("an r step \"(.*)\"")]
    public Task AnRStepNamed(string name) => driver.Ran_for(Who + " " + name);

    [StepDefinition("an r table step")]
    public Task AnRTableStep(Table table) => driver.Ran_for(Who + " rows:" + table.RowCount);

    [StepDefinition("an r composite runs a woven namesake and then the step")]
    public async Task Namesake()
    {
        await WovenNamesake();
        var table = new Table("sku");
        table.AddRow("APPLE");
        await runner.GivenAsync("the namesake step", "the doc string of the namesake", table);
    }

    [Step(Description = "the namesake step")]
    public Task WovenNamesake() => driver.Ran_for(Who + " woven");

    [StepDefinition("the namesake step")]
    public Task TheNamesakeStep(string doc, Table table) => driver.Ran_for(Who + " defined " + doc + table.RowCount);

    [StepDefinition("an r composite runs an r step \"(.*)\"")]
    public async Task CompositeRuns(string name)
    {
        await driver.Ran_for(Who + " composite");
        await runner.GivenAsync($"an r step \"{name}\"");
    }

    [StepDefinition("an r step that calls a woven step and then fails")]
    public async Task CallsWovenThenFails()
    {
        await driver.Ran_for(Who);
        throw new InvalidOperationException("the composite failed after its woven step passed");
    }

    [StepDefinition("an r step that fails for row b")]
    public async Task FailsForRowB()
    {
        await driver.Ran_for(Who);
        if (scenario.ScenarioInfo.Arguments["row"]?.ToString() == "b")
            throw new InvalidOperationException("a Background step failed for row b");
    }

    [StepDefinition("an r composite runs the same step twice")]
    public async Task RunsTwice()
    {
        await runner.GivenAsync("an r numbered step \"1\"");
        await runner.GivenAsync("an r numbered step \"2\"");
    }

    [StepDefinition("an r numbered step \"(.*)\"")]
    public void Numbered(int number)
    {
        if (number == 2)
            throw new InvalidOperationException("the second time failed");
    }

    [StepDefinition("an r composite attaches around a nested step")]
    public async Task Attaches()
    {
        var folder = Path.Combine(Path.GetTempPath(), "krk-review");
        Directory.CreateDirectory(folder);
        var before = Path.Combine(folder, "before.txt");
        var inner = Path.Combine(folder, "inner.txt");
        var after = Path.Combine(folder, "after.txt");
        await File.WriteAllTextAsync(before, "before");
        await File.WriteAllTextAsync(inner, "inner");
        await File.WriteAllTextAsync(after, "after");
        output.AddAttachment(before);
        await runner.GivenAsync("an r step attaches \"" + inner.Replace(Path.DirectorySeparatorChar, '/') + "\"");
        output.AddAttachment(after);
    }

    [StepDefinition("an r step attaches \"(.*)\"")]
    public void AttachesInner(string path) => output.AddAttachment(path);
}

[Binding]
public class ReviewHooks(ScenarioContext scenario, ITestRunner runner, ShapeDriver driver)
{
    [BeforeScenario("hook-runs-a-step-first", Order = 1)]
    public Task RunsAStepFirst() => runner.GivenAsync("an r step \"from a BeforeScenario hook\"");

    [BeforeScenario("woven-hook", Order = 1)]
    public Task WovenInHook() => driver.Ran_for("woven from a hook");

    [BeforeScenario("woven-hook-runs-a-step", Order = 1)]
    public Task WovenHookRunsAStep() => WovenThatRunsAStep();

    [Step]
    public Task WovenThatRunsAStep() => runner.GivenAsync("an r step \"from a woven hook\"");

    [BeforeScenario("hook-fails-for-row-b", Order = 1)]
    public void HookFailsForRowB()
    {
        if (scenario.ScenarioInfo.Arguments["row"]?.ToString() == "b")
            throw new InvalidOperationException("a BeforeScenario hook failed for row b");
    }

    [AfterScenario("after-scenario-step-fails", Order = 1)]
    public Task AfterScenarioStepFails() => runner.ThenAsync("an r numbered step \"2\"");

    private bool _ranInBlock;

    [BeforeScenarioBlock("block-hook-runs-a-step")]
    public async Task BlockHookRunsAStep()
    {
        if (_ranInBlock)
            return;
        _ranInBlock = true;
        await runner.GivenAsync("an r step \"from a block hook\"");
    }
}

[Binding]
public class ReviewSteps2(ITestRunner runner)
{
    [StepDefinition("an r composite runs an r undefined step")]
    public Task RunsUndefined() => runner.GivenAsync("nobody defined this r step");

    [StepDefinition("an r composite runs an inner step and then throws")]
    public async Task InnerThenThrows()
    {
        await runner.GivenAsync("an r numbered step \"1\"");
        throw new InvalidOperationException("the outer failed after the inner passed");
    }

    [AfterTestRun(Order = 0)]
    public static void Dump()
    {
        var file = Environment.GetEnvironmentVariable("KRK_DUMP");
        if (file is null) return;
        using var w = new StreamWriter(file);
        foreach (var s in Kronikol.ReqNRoll.ReqNRollScenarioCollector.GetAll().OrderBy(x => x.ScenarioTitle))
        {
            w.WriteLine($"{s.ScenarioTitle} [{s.ExecutionStatus}]");
            foreach (var step in s.Steps)
                w.WriteLine($"    {step.Keyword} {step.Text} : {step.Status} duration:{(step.Duration is null ? "null" : "set")} table:{step.TableText is not null} doc:{step.DocString is not null}");
        }
    }
}

[Binding]
public class ReviewSteps3(ITestRunner runner, Reqnroll.UnitTestProvider.IUnitTestRuntimeProvider unitTest)
{
    [StepDefinition("an r step ignores the test")]
    public void Ignores() => unitTest.TestIgnore("ignored at run time");

    [StepDefinition("an r composite runs a step that ignores the test")]
    public Task CompositeIgnores() => runner.GivenAsync("an r step ignores the test");

    [AfterScenario("after-scenario-throws", Order = 1)]
    public void AfterScenarioThrows() => throw new InvalidOperationException("a suite's AfterScenario hook failed");

    [BeforeScenario("hook-ignores", Order = 1)]
    public void HookIgnores() => unitTest.TestIgnore("ignored in a BeforeScenario hook");
}

[Binding]
public class ReviewSteps4(ITestRunner runner, ScenarioContext scenario)
{
    private static int _attempts;

    [StepDefinition("an r step that fails on the first attempt")]
    public void FailsOnFirstAttempt()
    {
        if (Interlocked.Increment(ref _attempts) == 1)
            throw new InvalidOperationException("the first attempt failed");
    }

    [StepDefinition("the hook runs a step before this")]
    [StepDefinition("the hook runs a step after this")]
    [StepDefinition("the hook runs a failing step after this")]
    public void Marked() { }

    private string Text => scenario.StepContext.StepInfo.Text;

    [BeforeStep("before-step-runs-a-step")]
    public async Task BeforeStepRuns()
    {
        if (Text == "the hook runs a step before this")
            await runner.GivenAsync("an r step \"from a BeforeStep hook\"");
    }

    [AfterStep("after-step-runs-a-step")]
    public async Task AfterStepRuns()
    {
        if (Text == "the hook runs a step after this")
            await runner.GivenAsync("an r step \"from an AfterStep hook\"");
    }

    [AfterStep("after-step-runs-a-failing-step")]
    public async Task AfterStepRunsFailing()
    {
        if (Text == "the hook runs a failing step after this")
            await runner.GivenAsync("an r numbered step \"2\"");
    }

    [BeforeStep("first-before-step-runs-a-step", Order = int.MinValue)]
    public async Task FirstBeforeStepRuns()
    {
        if (Text == "the hook runs a step before this")
            await runner.GivenAsync("an r step \"from a first BeforeStep hook\"");
    }
}

[Binding]
public class ReviewSteps5(ITestRunner runner, ShapeDriver driver)
{
    [StepDefinition("an r deep step 1")]
    public async Task Deep1()
    {
        await driver.Ran_for("deep 1");
        await WovenAround2();
        await driver.Ran_for("deep 1 after");
    }

    [Step]
    public Task WovenAround2() => runner.GivenAsync("an r deep step 2", "doc of 2", null);

    [StepDefinition("an r deep step 2")]
    public async Task Deep2(string doc)
    {
        var table = new Table("n");
        table.AddRow("3");
        await runner.WhenAsync("an r deep step 3", null, table);
        await runner.ThenAsync("an r deep step that is skipped");
    }

    [StepDefinition("an r deep step 3")]
    public void Deep3(Table table) => throw new InvalidOperationException("the deepest step failed");

    [StepDefinition("an r deep step that is skipped")]
    public void Skipped() { }
}

[Binding]
public class ReviewSteps6(ITestRunner runner, ScenarioContext scenario, ShapeDriver driver)
{
    [StepDefinition("an r composite whose woven sub-step fails for row b")]
    public async Task Composite()
    {
        await driver.Ran_for("composite");
        await runner.GivenAsync("an r inner step fails for row b");
    }

    [StepDefinition("an r inner step fails for row b")]
    public void Inner()
    {
        if (scenario.ScenarioInfo.Arguments["row"]?.ToString() == "b")
            throw new InvalidOperationException("the inner step failed for row b");
    }
}

[Binding]
public class ReviewHooks7(ITestRunner runner)
{
    [BeforeScenario("hook-runs-the-background-step", Order = 1)]
    public Task RunsTheBackgroundStep() => runner.GivenAsync("an r step \"bg one\"");
}
