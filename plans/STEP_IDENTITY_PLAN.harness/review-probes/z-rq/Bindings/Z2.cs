using Kronikol;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.UnitTestProvider;

namespace HookSteps.Repro;

public class Z2Driver(Driver driver)
{
    [Step]
    public async Task Seeding_may_fail(bool fail)
    {
        await driver.Post("seed");
        if (fail) throw new InvalidOperationException("seeding failed");
    }
}

[Binding]
public class Z2Hooks(Z2Driver z, ScenarioContext scenario)
{
    [BeforeScenario("seed-maybe")]
    public Task SeedMaybe() => z.Seeding_may_fail(scenario.ScenarioInfo.Tags.Contains("seed-fails"));

    // What a suite does: a screenshot when a step has just failed.
    [AfterStep("screenshot")]
    public void Shot(IReqnrollOutputHelper output)
    {
        if (scenario.TestError is null) return;
        var path = Path.Combine(Path.GetTempPath(), "z-failure-shot-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "a screenshot");
        output.AddAttachment(path);
    }
}

[Binding]
public class Z2Steps(IUnitTestRuntimeProvider runtime, Driver driver)
{
    [When("the scenario skips itself")] public void SkipsItself() => runtime.TestIgnore("not today");
    [When("the step is pending")] public void IsPending() => throw new PendingStepException();
    [Given("this table is read")] public Task TableRead(Table table) => driver.Post("table");
}
