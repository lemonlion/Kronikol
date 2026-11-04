using System.Net;
using System.Text;
using System.Threading.Channels;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.BoDi;

[assembly: TrackSteps]

namespace Probe.Rq;

[Binding]
public class Hooks
{
    [BeforeScenario(Order = 0)]
    public void RegisterTrackedClient(IObjectContainer container)
    {
        var handler = new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
        {
            CallerName = "Tests",
            FixedNameForReceivingService = "Svc"
        }) { InnerHandler = new Fake() };
        container.RegisterInstanceAs(new HttpClient(handler) { BaseAddress = new Uri("http://svc.local/") });
    }

    [BeforeStep("userhook", Order = 100)]
    public async Task UserBefore()
    {
        StepCollector.StartStep(ReqNRollTestContext.CurrentTestInfo!.Value.Id, null, "opened by a user hook", null, null);
        await Task.Yield();
    }

    [AfterStep("userhook", Order = 100)]
    public void UserAfter() =>
        StepCollector.CompleteStep(ReqNRollTestContext.CurrentTestInfo!.Value.Id, false, "the user hook's step failed");

    // Sync on purpose: Reqnroll carries what a sync binding leaves in the context on to the next binding.
    [BeforeScenario("synchook", Order = 100)]
    public void SyncBefore() =>
        StepCollector.StartStep(ReqNRollTestContext.CurrentTestInfo!.Value.Id, null, "opened by a sync hook", null, null);

    [AfterScenario("synchook", Order = 100)]
    public void SyncAfter() =>
        StepCollector.CompleteStep(ReqNRollTestContext.CurrentTestInfo!.Value.Id, true);

    [AfterTestRun]
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Review" });

    private sealed class Fake : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
    }
}

[Binding]
public class ReviewSteps(HttpClient client, ReviewDriver driver, ScenarioContext scenario)
{
    private static Task _left;
    private static Channel<string> _inbox;

    [Given("a background task will run a sub-step")]
    public void GivenBackground()
    {
        client.GetAsync("given-own").GetAwaiter().GetResult();
        _left = Task.Run(async () =>
        {
            await Task.Delay(60);
            await driver.A_later_sub_step();
        });
    }

    [When("a long step runs")]
    public async Task WhenLong()
    {
        await client.GetAsync("when-own/1");
        await Task.Delay(150);
        await client.GetAsync("when-own/2");
        await _left;
    }

    [Then("the result is checked")]
    public Task ThenChecked() => client.GetAsync("then-own");

    [Given("a plain given")]
    public Task PlainGiven() => client.GetAsync("given-own");

    [When("a plain when")]
    public Task PlainWhen() => client.GetAsync("when-own");

    [Given("the service is started")]
    public async Task GivenService()
    {
        await client.GetAsync("given-own");
        var id = ReqNRollTestContext.CurrentTestInfo!.Value;
        var inbox = _inbox = Channel.CreateUnbounded<string>();
        using (TestIdentityScope.Detach())
        {
            _ = Task.Run(async () =>
            {
                await foreach (var message in inbox.Reader.ReadAllAsync())
                {
                    using (TestIdentityScope.Begin(id.Name, id.Id))
                        await client.GetAsync("consumer/" + message);
                }
            });
        }
    }

    [When("a message is published")]
    public async Task WhenPublished()
    {
        await client.GetAsync("when-own");
        await _inbox.Writer.WriteAsync("from-when");
        await Task.Delay(100);
    }

    [Then("a second message is published")]
    public async Task ThenSecond()
    {
        await client.GetAsync("then-own");
        await _inbox.Writer.WriteAsync("from-then");
        await Task.Delay(100);
        _inbox.Writer.Complete();
    }

    [Given("a step that calls a step")]
    public async Task GivenCallsAStep()
    {
        await client.GetAsync("outer/before");
        var runner = scenario.ScenarioContainer.Resolve<ITestRunner>();
        await runner.WhenAsync("a plain when");
        await client.GetAsync("outer/after");
    }

    [When("a step runs a sub-step then fails")]
    public async Task WhenFails()
    {
        await driver.A_passing_sub_step();
        await client.GetAsync("own/after-sub-step");
        throw new InvalidOperationException("the step failed");
    }
}

public class ReviewDriver(HttpClient client)
{
    [Step]
    public async Task A_later_sub_step()
    {
        await client.GetAsync("later-sub-step");
        await Task.Delay(10);
    }

    [Step]
    public async Task A_passing_sub_step()
    {
        await client.GetAsync("sub/one");
        await client.GetAsync("sub/two");
    }
}
