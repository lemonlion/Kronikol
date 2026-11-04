using System.Net;
using System.Text;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Reports;
using Kronikol.Tracking;
using Reqnroll;

namespace P8.Suite;

[Binding]
public class P8Steps
{
    private static readonly HttpClient Client = new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "P8 Tests",
        FixedNameForReceivingService = "P8 API"
    }) { InnerHandler = new FakeApi() }) { BaseAddress = new Uri("http://p8.local/") };

    [Given("p8 setup call")]
    public async Task Setup() => (await Client.GetAsync("setup")).EnsureSuccessStatusCode();

    [When("p8 call with {word}")]
    public async Task CallWith(string word) => (await Client.GetAsync("call/" + word)).EnsureSuccessStatusCode();

    [Then("p8 done")]
    public void Done() { }

    [AfterTestRun(Order = 100)]
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "P8",
            SeparateBackgroundSteps = true,
            TestRunReportToggleDefaults = Environment.GetEnvironmentVariable("KRK_DIAGCLOSED") == "1" ? new ReportToggleDefaults { DiagramsSectionOpen = false } : new ReportToggleDefaults(),
        });

    private sealed class FakeApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
    }
}
