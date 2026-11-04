using Kronikol.Tracking;
using Reqnroll;

namespace Shapes.Suite.Steps;

[Binding]
public class Review5Steps(ShapeDriver driver, ScenarioContext scenario, ITestRunner runner, IReqnrollOutputHelper Output)
{
    private string Who => scenario.ScenarioInfo.Title;
    private string RowArg => scenario.ScenarioInfo.Arguments["row"]?.ToString() ?? "";

    // ---- a woven step with a keyword attribute reads like the nested step ----
    [StepDefinition("q composite runs a keyword woven namesake and then the step")]
    public async Task KeywordNamesake()
    {
        await WovenKeywordNamesake();
        var table = new Table("sku");
        table.AddRow("APPLE");
        await runner.GivenAsync("the q namesake step", "the doc string of the namesake", table);
    }

    [GivenStep(Description = "the q namesake step")]
    public Task WovenKeywordNamesake() => driver.Ran_for(Who + " woven");

    [StepDefinition("the q namesake step")]
    public Task TheQNamesakeStep(string doc, Table table) => driver.Ran_for(Who + " defined " + doc + table.RowCount);

    // the nested one fails, and only it
    [StepDefinition("q composite runs a keyword woven namesake and then a failing step")]
    public async Task KeywordNamesakeFailing()
    {
        await WovenKeywordNamesakeBefore();
        await runner.GivenAsync("the q failing namesake step");
    }

    [GivenStep(Description = "the q failing namesake step")]
    public Task WovenKeywordNamesakeBefore() => driver.Ran_for(Who + " woven");

    [StepDefinition("the q failing namesake step")]
    public void TheQFailingNamesakeStep() => throw new InvalidOperationException("the real nested step failed");

    // ---- a woven [ThenStep] that fails, the nested step of the same text after it never runs ----
    [StepDefinition("q composite whose keyword woven step of the same text fails first")]
    public async Task WovenFailsFirst()
    {
        await WovenThatFails();
        await runner.GivenAsync("the q never reached step");
    }

    [ThenStep(Description = "the q never reached step")]
    public Task WovenThatFails() => throw new InvalidOperationException("the woven step failed");

    [StepDefinition("the q never reached step")]
    public void TheQNeverReachedStep() { }

    // ---- a composite that catches what a nested step threw, then carries on ----
    [StepDefinition("q composite catches a failing nested step and runs another")]
    public async Task CatchesNested()
    {
        try { await runner.GivenAsync("q nested step that throws"); } catch (Exception) { }
        await runner.GivenAsync("q nested step after the catch");
    }

    [StepDefinition("q nested step that throws")]
    public void NestedThrows() => throw new InvalidOperationException("the nested step threw");

    [StepDefinition("q nested step after the catch")]
    public Task NestedAfter() => driver.Ran_for(Who + " after");

    // ---- a Background composite with many sub-steps, failing by itself for a row ----
    [StepDefinition("q big composite fails by itself for row (.*)")]
    public async Task BigComposite(string failingRow)
    {
        for (var n = 0; n < 40; n++)
            await Woven(n);
        if (RowArg == failingRow)
            throw new InvalidOperationException("the composite failed by itself for row " + RowArg);
    }

    [Step]
    public Task Woven(int number) => Task.CompletedTask;

    [StepDefinition("q storm composite fails by itself")]
    public async Task StormComposite()
    {
        for (var n = 0; n < 60; n++)
            await Woven(n);
        throw new InvalidOperationException("the composite failed by itself");
    }

    [StepDefinition("q composite runs a stopped step and then a woven step")]
    public async Task StoppedThenWoven()
    {
        await runner.GivenAsync("q step the hook stops");
        await Woven(1);
    }

    [StepDefinition("q composite runs a step, a stopped step and then a woven step")]
    public async Task StepStoppedThenWoven()
    {
        await runner.GivenAsync("q step with the word first");
        await runner.GivenAsync("q step the hook stops");
        await Woven(2);
    }

    [StepDefinition("q step the hook stops")]
    public void TheHookStops() { }

    [BeforeStep("q-hook-stops", Order = int.MinValue)]
    public void HookStops()
    {
        if (scenario.StepContext.StepInfo.Text == "q step the hook stops")
            throw new InvalidOperationException("a BeforeStep hook ahead of the adapter's failed");
    }

    [StepDefinition("q step attaches a file for its row")]
    public void AttachesForRow()
    {
        var folder = Path.Combine(Path.GetTempPath(), "krk-review5");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "row-" + RowArg + ".txt");
        File.WriteAllText(file, "row " + RowArg);
        Output.AddAttachment(file);
    }

    [StepDefinition("q composite catches a keyword woven failure then runs the same text")]
    public async Task CatchesWoven()
    {
        try { await WovenFailsThenCaught(); } catch (InvalidOperationException) { }
        await runner.GivenAsync("the q caught namesake step");
    }

    [GivenStep(Description = "the q caught namesake step")]
    public async Task WovenFailsThenCaught()
    {
        await Task.Yield();
        throw new InvalidOperationException("the woven step failed");
    }

    [StepDefinition("the q caught namesake step")]
    public void TheQCaughtNamesakeStep() { }

    [StepDefinition("q composite runs a keyword woven namesake and then a step that runs a step")]
    public async Task NamesakeThenComposite()
    {
        await WovenOuterNamesake();
        await runner.GivenAsync("the q outer namesake step");
    }

    [GivenStep(Description = "the q outer namesake step")]
    public Task WovenOuterNamesake() => driver.Ran_for(Who + " woven");

    [StepDefinition("the q outer namesake step")]
    public Task TheQOuterNamesakeStep() => runner.GivenAsync("q step with the word inner");

    // ---- nested with the row in its text ----
    [StepDefinition("q step with the word (.*)")]
    public void WithWord(string word) { }

    [StepDefinition("q composite names its row")]
    public Task NamesItsRow() => runner.GivenAsync("q step with the word row" + RowArg + "word");

    [StepDefinition("q step fails for row (.*)")]
    public void FailsForRow(string failing)
    {
        if (RowArg == failing)
            throw new InvalidOperationException("a Background step failed for row " + failing);
    }

    // ---- a nested step whose text a hook or the runner could change ----
    [StepDefinition("q composite runs a step with spaces around its text")]
    public Task SpacesAround() => runner.GivenAsync("  q step with the word spaced  ");

    // ---- a nested step that is run twice with the same text, around a woven step with the text and a keyword ----
    [StepDefinition("q composite runs the same text twice around a woven namesake")]
    public async Task TwiceAround()
    {
        var first = new Table("n");
        first.AddRow("first");
        await runner.GivenAsync("q table step", null, first);
        await WovenTableNamesake();
        var second = new Table("n");
        second.AddRow("second");
        await runner.GivenAsync("q table step", null, second);
    }

    [WhenStep(Description = "q table step")]
    public Task WovenTableNamesake() => driver.Ran_for(Who + " woven");

    [StepDefinition("q table step")]
    public void QTableStep(Table table) { }

    // ---- a step definition the Background calls by C# as well: the step itself is woven ----
    [StepDefinition("q step woven and defined")]
    [GivenStep(Description = "q step woven and defined")]
    public void WovenAndDefined() { }

    [StepDefinition("q composite runs a step woven and defined")]
    public async Task RunsWovenAndDefined()
    {
        var table = new Table("n");
        table.AddRow("1");
        await runner.GivenAsync("q step woven and defined");
    }
}
