using System.Net;
using System.Text;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.BoDi;
using Spike.Substeps.Steps;

namespace Spike.Substeps.Hooks;

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

    [BeforeScenario("seeded")]
    public Task SeedReferenceData(ShopDriver shop) => shop.Reference_data_is_seeded();

    [AfterTestRun]
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Sub-step spike" });

    private sealed class FakeShopApi : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            var (status, json) = path switch
            {
                "/payments" when body.Contains("declined") => (HttpStatusCode.PaymentRequired, """{"error":"card declined"}"""),
                "/orders" => (HttpStatusCode.OK, """{"status":"Confirmed"}"""),
                _ => (HttpStatusCode.OK, "{}")
            };
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
