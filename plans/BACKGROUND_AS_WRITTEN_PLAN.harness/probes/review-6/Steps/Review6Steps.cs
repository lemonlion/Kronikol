using System.Diagnostics;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;

namespace Shapes.Suite.Steps;

[Binding]
public class Review6Steps(ShapeDriver driver, ScenarioContext scenario, ITestRunner runner)
{
    private static Table Skus()
    {
        var table = new Table("sku");
        table.AddRow("APPLE");
        return table;
    }

    private string Text => scenario.StepContext.StepInfo.Text;

    // ---- the hooks ----
    [BeforeStep("r6-stop", Order = int.MinValue)]
    public void Stop()
    {
        if (Text.Contains("stopped") && !Text.Contains("composite") || Text.Contains("the hook stops"))
            throw new InvalidOperationException("a BeforeStep hook ahead of the adapter's failed: " + Text);
    }

    [BeforeStep("r6-stop-row-b", Order = int.MinValue)]
    public void StopRowB()
    {
        if (Text == "r6 nested for row b")
            throw new InvalidOperationException("a BeforeStep hook ahead of the adapter's failed for row b");
    }

    [AfterStep("r6-after-throws")]
    public void AfterThrows()
    {
        if (Text.Contains("nested fails after"))
            throw new InvalidOperationException("an AfterStep hook failed: " + Text);
    }

    private bool _ran;

    [BeforeStep("r6-before-runs")]
    public async Task BeforeRuns()
    {
        if (Text.StartsWith("r6 nested a hook adds to") && !_ran)
        {
            _ran = true;
            await runner.GivenAsync("r6 step a hook ran", "doc from the before hook", null);
        }
    }

    [AfterStep("r6-after-runs")]
    public async Task AfterRuns()
    {
        if (Text.StartsWith("r6 nested a hook adds to") && !_ran)
        {
            _ran = true;
            await runner.GivenAsync("r6 step a hook ran", "doc from the after hook", null);
        }
    }

    [BeforeStep("r6-first-before-runs", Order = int.MinValue)]
    public async Task FirstBeforeRuns()
    {
        if (Text.StartsWith("r6 nested a hook adds to") && !_ran)
        {
            _ran = true;
            await runner.GivenAsync("r6 step a hook ran", "doc from the first before hook", null);
        }
    }

    [AfterStep("r6-after-runs-stopped")]
    public async Task AfterRunsStopped()
    {
        if (Text.StartsWith("an r step") && !_ran)
        {
            _ran = true;
            await runner.GivenAsync("r6 step the hook stops with a doc", "doc of the stopped step", null);
        }
    }

    [AfterStep("r6-read-stopwatch", Order = 100)]
    public void ReadStopwatch()
    {
        if (!Text.Contains("composite")) return;
        var read = scenario.TryGetValue(ReqNRollConstants.StepStopwatchKey, out Stopwatch? watch) ? watch!.ElapsedMilliseconds + " ms, running " + watch.IsRunning : "no stopwatch";
        File.AppendAllText(Environment.GetEnvironmentVariable("KRK_STOPWATCH") ?? Path.Combine(Path.GetTempPath(), "krk-stopwatch.txt"), scenario.ScenarioInfo.Title + ": " + read + Environment.NewLine);
    }

    // ---- the steps ----
    [StepDefinition("r6 step a hook ran")]
    public void AStepAHookRan(string doc) { }

    [StepDefinition("r6 step the hook stops with a doc")]
    public void StoppedWithADoc(string doc) { }

    [StepDefinition("r6 nested a hook adds to")]
    public void NestedAHookAddsTo() { }

    [StepDefinition("r6 composite runs a nested step a hook adds to")]
    public Task CompositeHookAddsTo() => runner.GivenAsync("r6 nested a hook adds to");

    [StepDefinition("r6 stopped step takes (.*)")]
    public void StoppedTakes(string what, string doc, Table table) { }

    [Step(Description = "r6 woven wrapper")]
    public async Task WovenWrapper()
    {
        await runner.GivenAsync("r6 stopped step takes apples", "a doc string of the stopped step", Skus());
        await driver.Ran_for("after the stopped step");
    }

    [StepDefinition("r6 composite runs a woven step that runs a stopped step with a table and doc")]
    public async Task RunsWoven()
    {
        await WovenWrapper();
        await driver.Ran_for("after the woven step");
    }

    [Step(Description = "r6 woven wrapper that throws")]
    public async Task WovenWrapperThrows()
    {
        await runner.GivenAsync("r6 stopped step takes apples", "a doc string of the stopped step", Skus());
        throw new InvalidOperationException("the woven step threw after the stopped step");
    }

    [StepDefinition("r6 composite runs a woven step that runs a stopped step and throws")]
    public Task RunsWovenThrows() => WovenWrapperThrows();

    [StepDefinition("r6 nested fails after taking (.*)")]
    public void NestedFailsAfter(string what, string doc, Table table) { }

    [StepDefinition("r6 nested passes after taking (.*)")]
    public void NestedPassesAfter(string what, string doc, Table table) { }

    [StepDefinition("r6 composite runs a nested step the AfterStep hook fails")]
    public Task RunsAfterFails() => runner.GivenAsync("r6 nested fails after taking apples", "a doc string of the nested step", Skus());

    [StepDefinition("r6 composite runs a nested step the AfterStep hook passes")]
    public Task RunsAfterPasses() => runner.GivenAsync("r6 nested passes after taking apples", "a doc string of the nested step", Skus());

    [StepDefinition("r6 second nested takes (.*)")]
    public void SecondNested(string what, string doc, Table table) { }

    [StepDefinition("r6 composite catches what an AfterStep hook threw and runs another")]
    public async Task CatchesAfter()
    {
        try { await runner.GivenAsync("r6 nested fails after taking apples", "a doc string of the nested step", Skus()); }
        catch (InvalidOperationException) { }
        await runner.GivenAsync("r6 second nested takes pears", "a doc string of the second", Skus());
    }

    [Step(Description = "r6 woven around")]
    public Task WovenAround() => runner.GivenAsync("r6 nested fails after taking apples", "a doc string of the nested step", Skus());

    [StepDefinition("r6 composite runs a woven step around a nested step the AfterStep hook fails")]
    public Task RunsWovenAround() => WovenAround();

    // ---- the phase ----
    [StepDefinition("r6 inner stopped given")]
    public void InnerStoppedGiven() { }

    [StepDefinition("r6 inner stopped when")]
    public void InnerStoppedWhen() { }

    [StepDefinition("r6 inner given that runs")]
    public void InnerGiven() { }

    [StepDefinition("r6 when composite runs a stopped given and calls")]
    public async Task WhenRunsStoppedGiven()
    {
        await driver.Ran_for("before " + TestPhaseContext.Current);
        await runner.GivenAsync("r6 inner stopped given");
        await driver.Ran_for("after " + TestPhaseContext.Current);
    }

    [StepDefinition("r6 when composite runs a given and calls")]
    public async Task WhenRunsGiven()
    {
        await driver.Ran_for("before " + TestPhaseContext.Current);
        await runner.GivenAsync("r6 inner given that runs");
        await driver.Ran_for("after " + TestPhaseContext.Current);
    }

    [StepDefinition("r6 given composite runs a stopped when and calls")]
    public async Task GivenRunsStoppedWhen()
    {
        await driver.Ran_for("before " + TestPhaseContext.Current);
        await runner.WhenAsync("r6 inner stopped when");
        await driver.Ran_for("after " + TestPhaseContext.Current);
    }

    // ---- rows ----
    [StepDefinition("r6 nested for row (.*)")]
    public void NestedForRow(string row, string doc) { }

    [StepDefinition("r6 composite runs a nested step named for its row")]
    public Task RunsNestedNamed() => runner.GivenAsync("r6 nested for row " + scenario.ScenarioInfo.Arguments["row"], "doc for " + scenario.ScenarioInfo.Arguments["row"], null);

    // ---- keywords ----
    [StepDefinition("r6 stopped and step")]
    public void StoppedAndStep() { }

    [StepDefinition("r6 composite runs a stopped and step")]
    public async Task RunsStoppedAnd()
    {
        await runner.AndAsync("r6 stopped and step");
    }

    // ---- the stopwatch ----
    [StepDefinition("r6 inner stopped quick")]
    public void InnerStoppedQuick() { }

    [StepDefinition("r6 inner quick")]
    public void InnerQuick() { }

    [StepDefinition("r6 slow composite runs a stopped step")]
    public async Task SlowStopped()
    {
        await Task.Delay(300);
        await runner.GivenAsync("r6 inner stopped quick");
    }

    [StepDefinition("r6 slow composite runs a step")]
    public async Task SlowRuns()
    {
        await Task.Delay(300);
        await runner.GivenAsync("r6 inner quick");
    }

    [StepDefinition("r6 step the hook stops at the top")]
    public void StoppedAtTheTop() { }
}

[Binding]
public class Review6MoreSteps(ShapeDriver driver, ScenarioContext scenario, ITestRunner runner, Reqnroll.UnitTestProvider.IUnitTestRuntimeProvider unitTest)
{
    private string Text => scenario.StepContext.StepInfo.Text;

    [AfterStep("r6-after-throws-all")]
    public void AfterThrowsAll() => throw new InvalidOperationException("an AfterStep hook failed on every step: " + Text);

    private static Table Skus()
    {
        var table = new Table("sku");
        table.AddRow("APPLE");
        return table;
    }

    [StepDefinition("r6 composite catches what an AfterStep hook threw then calls")]
    public async Task CatchesThenCalls()
    {
        try { await runner.GivenAsync("r6 nested fails after taking apples", "a doc string of the nested step", Skus()); }
        catch (InvalidOperationException) { }
        await driver.Ran_for("after the catch");
    }

    [BeforeStep("r6-pending", Order = int.MinValue)]
    public void PendingHook()
    {
        if (Text == "r6 inner pending by hook") throw new PendingStepException("pending from a hook");
    }

    [BeforeStep("r6-ignore", Order = int.MinValue)]
    public void IgnoreHook()
    {
        if (Text == "r6 inner ignored by hook") unitTest.TestIgnore("ignored from a hook");
    }

    [StepDefinition("r6 inner pending by hook")]
    [StepDefinition("r6 inner ignored by hook")]
    public void Inner() { }

    [StepDefinition("r6 composite runs an inner pending by hook")]
    public Task RunsPending() => runner.GivenAsync("r6 inner pending by hook");

    [StepDefinition("r6 composite runs an inner ignored by hook")]
    public Task RunsIgnored() => runner.GivenAsync("r6 inner ignored by hook");
}

[Binding]
public class Review6BlockSteps(ScenarioContext scenario, ITestRunner runner)
{
    private bool _ran;

    [BeforeScenarioBlock("r6-block-throws")]
    public void BlockThrows()
    {
        if (scenario.CurrentScenarioBlock == ScenarioBlock.When)
            throw new InvalidOperationException("a BeforeScenarioBlock hook failed");
    }

    [BeforeScenarioBlock("r6-block-runs")]
    public async Task BlockRuns()
    {
        if (scenario.CurrentScenarioBlock == ScenarioBlock.When && !_ran)
        {
            _ran = true;
            await runner.GivenAsync("r6 inner a block hook ran", "doc from the block hook", null);
        }
    }

    [StepDefinition("r6 inner a block hook ran")]
    public void InnerBlock(string doc) { }

    [StepDefinition("r6 inner when")]
    public void InnerWhen(Table table) { }

    [StepDefinition("r6 inner when plain")]
    public void InnerWhenPlain() { }

    [StepDefinition("r6 composite runs a when a block hook stops with a table")]
    public Task RunsBlockStopped()
    {
        var table = new Table("sku");
        table.AddRow("APPLE");
        return runner.WhenAsync("r6 inner when", null, table);
    }

    [StepDefinition("r6 composite runs a when a block hook adds to")]
    public Task RunsBlockAdded() => runner.WhenAsync("r6 inner when plain");
}

[Binding]
public class Review6TwiceSteps(ScenarioContext scenario, ITestRunner runner)
{
    private int _seen;

    [BeforeStep("r6-stop-second", Order = int.MinValue)]
    public void StopSecond()
    {
        if (scenario.StepContext.StepInfo.Text == "r6 same nested text" && ++_seen == 2)
            throw new InvalidOperationException("the hook stopped the second time");
    }

    [StepDefinition("r6 same nested text")]
    public void Same(string doc) { }

    [StepDefinition("r6 composite runs the same nested text twice with a doc each")]
    public async Task Twice()
    {
        await runner.GivenAsync("r6 same nested text", "doc of the first", null);
        await runner.GivenAsync("r6 same nested text", "doc of the second", null);
    }
}
