using System.Net;
using System.Text;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;

namespace R7.Suite;

[Binding]
public class R7Calls(ScenarioContext scenario, ITestRunner runner)
{
    private static readonly HttpClient Client = new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "R7 Tests",
        FixedNameForReceivingService = "R7 API"
    }) { InnerHandler = new FakeApi() }) { BaseAddress = new Uri("http://r7.local/") };

    private bool _ran;
    private string Text => scenario.StepContext.StepInfo.Text;

    [BeforeStep("r7-before-runs", Order = int.MinValue)]
    public async Task BeforeRuns()
    {
        if (Text == "r7 s" && !_ran) { _ran = true; await runner.GivenAsync("r7 h!"); }
    }

    [BeforeStep("r7-before-catches", Order = int.MinValue)]
    public async Task BeforeCatches()
    {
        if (Text == "r7 s" && !_ran) { _ran = true; try { await runner.GivenAsync("r7 h!"); } catch (InvalidOperationException) { } }
    }

    [AfterStep("r7-after-runs", Order = 50)]
    public async Task AfterRuns()
    {
        if (Text == "r7 s" && !_ran) { _ran = true; await runner.GivenAsync("r7 h!"); }
    }

    [AfterStep("r7-cleanup-call", Order = 100)]
    public void AfterHook()
    {
        if (Text.Contains("hook fails")) throw new InvalidOperationException("an AfterStep hook failed on the calling step");
    }

    [AfterScenario("r7-cleanup-call", Order = 0)]
    public async Task CleanupCall()
    {
        await Client.DeleteAsync("/cleanup-from-the-afterscenario-hook");
    }

    [StepDefinition("r7c calls and hook fails")]
    public async Task CallsHookFails() => await Client.GetAsync("/called-in-the-step");

    [StepDefinition("r7c calls and definition fails")]
    public async Task CallsDefinitionFails()
    {
        await Client.GetAsync("/called-in-the-step");
        throw new InvalidOperationException("the definition failed after its call");
    }

    private sealed class FakeApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
    }
}
