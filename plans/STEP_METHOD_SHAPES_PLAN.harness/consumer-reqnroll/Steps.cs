using System.Net;
using System.Text;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.BoDi;
using Shouldly;

[assembly: TrackSteps]
[assembly: TrackAssertions]

namespace Spike.Shapes;

/// <summary>The sub-steps: plain methods woven by Kronikol.StepTracking, called from Reqnroll step definitions.</summary>
public class ShopDriver(HttpClient client)
{
    // The issue's two methods, as it gives them.
    [Step]
    public int A_number_is_chosen() => 42;

    [Step]
    public async ValueTask A_value_task_step()
    {
        await Task.Delay(100);
        (await client.GetAsync("value-task")).EnsureSuccessStatusCode();
    }

    [Step]
    public async ValueTask<int> A_value_task_step_with_a_number()
    {
        await Task.Delay(100);
        (await client.GetAsync("value-task-number")).EnsureSuccessStatusCode();
        return 7;
    }

    [Step]
    public async Task A_task_step()
    {
        await Task.Delay(100);
        A_void_step();
        (await client.GetAsync("task")).EnsureSuccessStatusCode();
    }

    [Step]
    public void A_void_step() { }

    [Step]
    public Task Throws_before_it_has_a_task(string name)
    {
        if (name.Length == 0)
            throw new ArgumentException("A name is required", nameof(name));
        return client.GetAsync("not-reached");
    }

    [Step]
    public ValueTask Throws_before_it_has_a_value_task(string name)
    {
        if (name.Length == 0)
            throw new ArgumentException("A name is required", nameof(name));
        return new ValueTask(client.GetAsync("not-reached"));
    }

    public bool AlreadyDone => true;

    [Step(SkipIf = nameof(AlreadyDone), SkipReason = "Already done")]
    public void A_bypassed_sub_step() => throw new InvalidOperationException("This body does not run");

    [Step]
    public async Task A_sub_step_that_runs() => (await client.GetAsync("after-the-bypass")).EnsureSuccessStatusCode();

    [Step]
    public async Task A_step_with_a_bypassed_sub_step()
    {
        A_bypassed_sub_step();
        await A_sub_step_that_runs();
    }

    [Step]
    public void Takes_a_quantity(in int quantity) => quantity.ShouldBe(2);
}

[Binding]
public class ShapeSteps(ShopDriver shop, HttpClient client)
{
    [When("a value task step runs")]
    public async Task WhenAValueTaskStepRuns() => await shop.A_value_task_step();

    [Given("a number is chosen")]
    public void GivenANumberIsChosen() => shop.A_number_is_chosen().ShouldBe(42);

    [When("a value task step hands back a number")]
    public async Task WhenAValueTaskStepHandsBackANumber() => (await shop.A_value_task_step_with_a_number()).ShouldBe(7);

    [When("a task step and a void step run")]
    public async Task WhenATaskStepAndAVoidStepRun() => await shop.A_task_step();

    [When("a task step throws before it has a task")]
    public async Task WhenATaskStepThrowsBeforeItHasATask() => await shop.Throws_before_it_has_a_task("");

    [When("a value task step throws before it has a task")]
    public async Task WhenAValueTaskStepThrowsBeforeItHasATask() => await shop.Throws_before_it_has_a_value_task("");

    [When("a step calls a sub-step that is bypassed and then a sub-step that runs")]
    public async Task WhenAStepCallsASubStepThatIsBypassed() => await shop.A_step_with_a_bypassed_sub_step();

    [When("a step takes a quantity by reference")]
    public void WhenAStepTakesAQuantityByReference() => shop.Takes_a_quantity(2);

    [Then("the next step is the Gherkin step's own")]
    public async Task ThenTheNextStepIsTheGherkinStepsOwn() => (await client.GetAsync("gherkin-step")).EnsureSuccessStatusCode();
}

[Binding]
public class Hooks
{
    [BeforeScenario(Order = 0)]
    public void RegisterTrackedClient(IObjectContainer container)
    {
        var handler = new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
        {
            CallerName = "Shop Tests",
            FixedNameForReceivingService = "Shop API"
        }) { InnerHandler = new FakeShopApi() };
        container.RegisterInstanceAs(new HttpClient(handler) { BaseAddress = new Uri("http://shop.local/") });
    }

    [AfterTestRun]
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Step method shapes" });

    private sealed class FakeShopApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
    }
}
