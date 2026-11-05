using Kronikol;
using Kronikol.ReqNRoll;
using Reqnroll;

namespace EngineFixtures;

[Binding]
public class Steps(Driver driver, ITestRunner runner, ScenarioContext scenario)
{
    // Plain
    [Given("a registered customer")] public Task Registered() => driver.Customer();
    [When("the customer places the order")] public Task Places() => driver.Order();
    [Then("the order is confirmed")] public Task Confirmed() => driver.Read_back();
    [When("the payment fails")] public async Task Fails() { await driver.Post("payments"); throw new InvalidOperationException("payment declined"); }
    [When(@"order (\d+) is placed")] public Task OrderN(int amount) => driver.Post("orders/" + amount);
    [Then(@"order (\d+) is read back")] public Task ReadN(int amount) => driver.Post("orders/" + amount);

    // Background shapes, each deciding by the scenario's tags
    [Given("the shop is ready")]
    public async Task Ready()
    {
        await driver.Post("ready");
        if (scenario.ScenarioInfo.Tags.Contains("failgiven"))
            throw new InvalidOperationException("the shop is not ready");
    }
    [Given("the shop is stocked")] public Task Stocked() => driver.Post("stocked");
    [Given("the rule background runs")] public Task RuleBg() => driver.Post("rule-bg");
    [Given("the rule background fails when asked")]
    public async Task RuleBgFails()
    {
        await driver.Post("rule-bg-2");
        if (scenario.ScenarioInfo.Tags.Contains("failbg"))
            throw new InvalidOperationException("rule background failed");
    }

    // What did not finish
    [When("a step is pending")] public void Pending() => throw new PendingStepException();
    [When("a step calls ScenarioContext Pending")] public void ContextPending() => scenario.Pending();
    [Given("a background step is pending")] public void BgPending() => throw new PendingStepException();
    [When("a step skips itself")] public async Task SkipsItself() { await driver.Post("skips"); SkipIt.Now("skipped by the step"); }

    // Two bindings for one text
    [When("this step is ambiguous")] public Task AmbiguousA() => driver.Post("a");
    [When("this step is ambiguous")] public Task AmbiguousB() => driver.Post("b");

    // Wrong parameter count
    [When("this step binds badly")] public Task Badly(string nothing) => driver.Post("bad");

    // Tables and doc strings
    [Then("the basket is listed")] public Task Basket(Table table) => driver.Post("basket");
    [Then("the note says")] public Task Note(string body) => driver.Post("note");
    [Then(@"the amount is (\d+) with the note")] public Task AmountNote(int amount, string body) => driver.Post("amount");
    [When("^the payment (passes|breaks)$")]
    public async Task Fate(string fate)
    {
        await driver.Post("fate/" + fate);
        if (fate == "breaks" && Environment.GetEnvironmentVariable("FATE_ALL_PASS") != "1")
            throw new InvalidOperationException("payment broke");
    }

    // ITestRunner at depth
    [Given("A runs B which fails then C")]
    public async Task AB()
    {
        await runner.GivenAsync("an inner step fails");
        await runner.GivenAsync("a registered customer");
        await driver.Post("outer-after");
    }

    [Given("an inner step fails")] public async Task InnerFails() { await driver.Post("inner"); throw new InvalidOperationException("inner step failed"); }

    [Given("depth one runs depth two then a sibling")]
    public async Task D1()
    {
        await runner.GivenAsync("depth two fails then runs a child");
        await runner.GivenAsync("the sibling of depth two");
        await driver.Post("d1-after");
    }

    [Given("depth two fails then runs a child")]
    public async Task D2()
    {
        await runner.GivenAsync("an inner step fails");
        await runner.GivenAsync("the child of depth two");
        await driver.Post("d2-after");
    }

    [Given("the child of depth two")] public Task D3() => driver.Post("d3");
    [Given("the sibling of depth two")] public Task D2s() => driver.Post("d2s");

    [Given("a step whose step method runs two steps")]
    public Task ViaStepMethod() => driver.Wrapper_step_method(async () =>
    {
        await runner.GivenAsync("an inner step fails");
        await runner.GivenAsync("a registered customer");
    });

    [Given("a step that runs an undefined step")]
    public async Task OuterUndefined()
    {
        await runner.GivenAsync("nobody defined this inner step");
        await runner.GivenAsync("a registered customer");
    }

    [Given("a step that runs a pending step")]
    public async Task OuterPending()
    {
        await runner.WhenAsync("a step is pending");
        await runner.GivenAsync("a registered customer");
    }

    [Given("a step that runs a step that skips itself")]
    public async Task OuterSkipping()
    {
        await runner.WhenAsync("a step skips itself");
        await runner.GivenAsync("a registered customer");
    }

    [Given("a step that retries an inner step three times")]
    public async Task Retries()
    {
        for (var i = 0; i < 3; i++)
            await runner.GivenAsync("an inner step fails");
    }

    [Given("a step that runs steps with arguments, the first failing")]
    public async Task OuterArguments()
    {
        await runner.GivenAsync("an inner step fails");
        var table = new Table("sku", "count");
        table.AddRow("APPLE", "3");
        await runner.ThenAsync("the basket is listed", null, table);
        await runner.ThenAsync("the note says", "a doc string", null);
    }

    [Given("a step that runs a step with the wrong keyword text")]
    public async Task Weird()
    {
        await runner.GivenAsync("an inner step fails");
        await runner.AndAsync("a registered customer");
        await runner.ButAsync("a registered customer");
    }

    [Given("a step that fails after running a step")]
    public async Task OuterThrows()
    {
        await runner.GivenAsync("a registered customer");
        throw new InvalidOperationException("outer threw");
    }

    [Given("a step that fails inner and throws itself")]
    public async Task OuterThrowsAfterInner()
    {
        await runner.GivenAsync("an inner step fails");
        await runner.GivenAsync("a registered customer");
        throw new InvalidOperationException("outer threw too");
    }

    [Given("a step that starts a step without awaiting and fails")]
    public async Task FireForget()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(5);
            await runner.GivenAsync("a registered customer");
        });
        await Task.Delay(30);
        throw new InvalidOperationException("outer threw");
    }
}

public static class SkipIt
{
    public static void Now(string why)
    {
#if LANE_X3
        Xunit.Assert.Skip(why);
#elif LANE_TUNIT
        global::TUnit.Core.Skip.Test(why);
#else
        throw new InvalidOperationException("no skip on this lane");
#endif
    }
}

/// <summary>One hook per shape, each scoped to its scenario's tag.</summary>
[Binding]
public class Hooks(Driver driver, ScenarioContext scenario)
{
    // BeforeScenario shapes
    [BeforeScenario("hookthrows")] public void HookThrows() => throw new InvalidOperationException("the hook failed");
    [BeforeScenario("hookpending")] public void HookPending() => scenario.Pending();
    [BeforeScenario("hookskip")] public void HookSkip() => SkipIt.Now("skipped by the hook");
    [BeforeScenario("hookseedfails")] public Task HookSeedFails() => driver.Seed_fails();

    [BeforeScenario("caughtseed")]
    public async Task CaughtSeed()
    {
        try { await driver.Seed_fails(); }
        catch (InvalidOperationException) { await driver.Seed(); }
    }

    [BeforeScenario("hookrunstwo")]
    public async Task HookRunsTwo(ITestRunner runner)
    {
        await runner.GivenAsync("an inner step fails");
        await runner.GivenAsync("a registered customer");
    }

    [BeforeScenario("hookstepmethodrunstwo")]
    public Task HookStepMethodRunsTwo(ITestRunner runner) => driver.Wrapper_step_method(async () =>
    {
        await runner.GivenAsync("an inner step fails");
        await runner.GivenAsync("a registered customer");
    });

    [BeforeScenario("hookrunsundefined")] public Task HookRunsUndefined(ITestRunner runner) => runner.GivenAsync("nobody defined this hook step");

    // AfterScenario shapes
    [AfterScenario("aftercall")] public Task AfterCall() => driver.Post("teardown");
    [AfterScenario("afterstep")] public Task AfterStepMethod() => driver.Clean();
    [AfterScenario("afterstepfails")] public Task AfterStepFails() => driver.Clean_fails();

    [AfterScenario("aftercaughtfail")]
    public async Task AfterCaught()
    {
        try { await driver.Clean_fails(); }
        catch (InvalidOperationException) { }
    }

    [AfterScenario("afterrunsstep")] public Task AfterRuns(ITestRunner runner) => runner.GivenAsync("a registered customer");
    [AfterScenario("afterthrows")] public void AfterThrows() => throw new InvalidOperationException("after scenario threw");

    // Block hooks that catch a failed step method
    [BeforeScenarioBlock("blockcaught")]
    public async Task BlockCaught()
    {
        if (scenario.CurrentScenarioBlock == ScenarioBlock.When)
        {
            try { await driver.Seed_fails(); }
            catch (InvalidOperationException) { }
        }
    }

    // A file on a step, and a suite reading what the steps list says
    [AfterScenario("readstatus")]
    public void ReadStatus(IReqnrollOutputHelper output) => output.WriteLine("status " + scenario.ScenarioExecutionStatus);

    // Feature hooks
    [BeforeFeature("beforefeaturethrows")] public static void BeforeFeatureThrows() => throw new InvalidOperationException("before feature threw");

    [BeforeFeature("featurerunsstep")]
    public static async Task FeatureRunsStep(ITestRunner runner)
    {
        try { await runner.GivenAsync("a registered customer"); }
        catch (Exception e) { Console.WriteLine("feature step threw: " + e.GetType().Name + " " + e.Message); }
    }

    // Report
    [AfterTestRun(Order = int.MaxValue)]
    public static void Report() => ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
    });
}
