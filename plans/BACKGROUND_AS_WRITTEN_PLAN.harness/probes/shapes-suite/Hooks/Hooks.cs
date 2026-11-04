using System.Net;
using System.Text;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.BoDi;

namespace Shapes.Suite.Hooks;

[Binding]
public class Hooks
{
    [BeforeScenario(Order = 0)]
    public void RegisterTrackedClient(IObjectContainer container)
    {
        var handler = new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
        {
            CallerName = "Shape Tests",
            FixedNameForReceivingService = "Shape API"
        }) { InnerHandler = new FakeApi() };
        container.RegisterInstanceAs(new HttpClient(handler) { BaseAddress = new Uri("http://shape.local/") });
    }

    [AfterTestRun]
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "Shapes",
            SeparateBackgroundSteps = Environment.GetEnvironmentVariable("KRK_SEPARATE") == "1",
            GenerateMergeableData = Environment.GetEnvironmentVariable("KRK_MERGEABLE") == "1",
        });

    private sealed class FakeApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
    }
}
