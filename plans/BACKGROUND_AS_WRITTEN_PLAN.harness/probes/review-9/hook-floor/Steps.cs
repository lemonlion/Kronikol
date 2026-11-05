using Kronikol;
using Kronikol.ReqNRoll;
using Reqnroll;

namespace HookProbe;

[Binding]
public class Steps(ScenarioContext scenario, ITestRunner runner, IReqnrollOutputHelper output)
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "hook-log.txt");
    private static readonly object Gate = new();
    private void Log(string text)
    {
        lock (Gate) File.AppendAllText(LogPath, scenario.ScenarioInfo.Title + " :: " + text + Environment.NewLine);
    }

    [StepDefinition("bg passes")]
    [StepDefinition("own passes")]
    [StepDefinition("own passes too")]
    [StepDefinition("rule bg passes")]
    [StepDefinition("inner passes")]
    [StepDefinition("hook target top")]
    [StepDefinition("hook target inner")]
    [StepDefinition("another inner")]
    public void Pass() { }

    [StepDefinition("own passes for (.*)")]
    public void PassFor(string row) { }

    [StepDefinition("a background step that fails on row two")]
    public void BackgroundFailsOnRowTwo()
    {
        if (scenario.ScenarioInfo.Arguments["row"]?.ToString() == "two") throw new InvalidOperationException("bg failed on row two");
    }

    [StepDefinition("bg runs inner")]
    public Task BgRunsInner() => runner.GivenAsync("inner passes");

    [StepDefinition("own runs hook target inner")]
    public Task OwnRunsHookTargetInner() => runner.GivenAsync("hook target inner");

    [StepDefinition("own runs failing inner and goes on")]
    public async Task OwnRunsFailingInner()
    {
        await runner.GivenAsync("inner fails");
        await runner.GivenAsync("inner passes");
    }

    [StepDefinition("inner fails")]
    public void InnerFails() => throw new InvalidOperationException("inner failed");

    [StepDefinition("a pending step")]
    public void APendingStep() => throw new PendingStepException();

    [StepDefinition("own runs inner then fails")]
    public async Task OwnRunsInnerThenFails()
    {
        await runner.GivenAsync("inner passes");
        throw new InvalidOperationException("outer failed");
    }

    [StepDefinition("own runs inner then hook target inner")]
    public async Task OwnRunsInnerThenHookTargetInner()
    {
        await runner.GivenAsync("inner passes");
        await runner.GivenAsync("hook target inner");
    }

    [StepDefinition("own runs hook target inner then another")]
    public async Task OwnRunsHookTargetInnerThenAnother()
    {
        await runner.GivenAsync("hook target inner");
        await runner.GivenAsync("another inner");
    }

    private string StepText => scenario.StepContext.StepInfo.Text;

    [BeforeStep("before-step-first", Order = int.MinValue)]
    public void BeforeStepFirst()
    {
        if (StepText is "hook target top" or "hook target inner") throw new InvalidOperationException("before first");
    }

    [BeforeStep("before-step-late")]
    public void BeforeStepLate()
    {
        if (StepText is "hook target top" or "hook target inner") throw new InvalidOperationException("before late");
    }

    [AfterStep("after-step-throw")]
    public void AfterStepThrow()
    {
        if (StepText is "hook target top" or "hook target inner") throw new InvalidOperationException("after step");
    }

    [AfterStep("after-step-tie", Order = int.MaxValue)]
    public void AfterStepTie()
    {
        if (StepText is "hook target top") throw new InvalidOperationException("after step tie");
    }

    [AfterStep("after-step-runs-step")]
    public async Task AfterStepRunsStep()
    {
        if (StepText is "hook target top") await runner.GivenAsync("inner passes");
    }

    [BeforeScenario("before-scenario-runs-step")]
    public Task BeforeScenarioRunsStep() => runner.GivenAsync("inner passes");

    [AfterScenario("after-scenario-throw")]
    public void AfterScenarioThrow() => throw new InvalidOperationException("after scenario");

    [AfterScenario("after-scenario-runs-step")]
    public Task AfterScenarioRunsStep() => runner.GivenAsync("inner passes");

    [AfterScenario("after-scenario-runs-failing-step")]
    public Task AfterScenarioRunsFailingStep() => runner.GivenAsync("inner fails");

    [AfterScenario("reads-list-min", Order = int.MinValue)]
    public void ReadsListMin()
    {
        var steps = (List<ReqNRollStepInfo>)scenario[ReqNRollConstants.StepsCollectionKey];
        Log("reads-list-min sees: " + string.Join(", ", steps.Select(s => s.Text)));
    }

    [AfterScenario("reads-list-default")]
    public void ReadsListDefault()
    {
        var steps = (List<ReqNRollStepInfo>)scenario[ReqNRollConstants.StepsCollectionKey];
        Log("reads-list-default sees: " + string.Join(", ", steps.Select(s => s.Text)));
    }

    [AfterScenario("attaches-after")]
    public void AttachesAfter()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "attached.txt");
        File.WriteAllText(path, "attached");
        output.AddAttachment(path);
    }

    [AfterTestRun]
    public static void Report()
    {
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
        });
    }
}
