using System.Net;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;

[assembly: TrackSteps]

namespace HookSteps.Every;

/// <summary>What a real driver does: named steps, each making one tracked call.</summary>
public class Driver
{
    private sealed class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private readonly HttpClient _client = new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "Tests",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Stub() }) { BaseAddress = new Uri("http://shop.test/") };

    public Task Post(string path) => _client.PostAsync(path, new StringContent("{}"));

    [Step] public async Task Reference_data_is_seeded() => await Post("seed");

    [Step]
    public async Task Reference_data_is_attached()
    {
        var path = Path.Combine(Path.GetTempPath(), "every-scenario-attachment.txt");
        await File.WriteAllTextAsync(path, "seeded");
        Track.Attachment(path);
        await Post("seed");
    }

    [GivenStep] public async Task A_tenant_exists() => await Post("tenant");
    [Step] public async Task A_customer_is_registered() => await Post("customers");
    [Step] public async Task A_coupon_is_added() => await Post("coupons");
    [Step] public async Task A_voucher_is_added() => await Post("vouchers");
    [Step] public async Task An_order_is_submitted() => await Post("orders");
    [Step] public async Task The_order_is_read_back() => await Post("orders/1");
}

[Binding]
public class Steps(Driver driver)
{
    [Given("a registered customer")] public Task Registered() => driver.A_customer_is_registered();
    [Given("the customer has a coupon")] public Task Coupon() => driver.A_coupon_is_added();
    [Given("the customer has a voucher")] public Task Voucher() => driver.A_voucher_is_added();
    [When("the customer places the order")] public Task Places() => driver.An_order_is_submitted();
    [Then("the order is confirmed")] public Task Confirmed() => driver.The_order_is_read_back();
    [Then("the order is read back")] public Task ReadBack() => driver.The_order_is_read_back();
}

[Binding]
public class Hooks(Driver driver)
{
    [BeforeScenario("every")] public Task Every() => driver.Reference_data_is_seeded();
    [BeforeScenario("onehook")] public Task One() => driver.Reference_data_is_seeded();
    [BeforeScenario("whenfirst")] public Task WhenFirst() => driver.Reference_data_is_seeded();
    [BeforeScenario("whenfirstgiven")] public Task WhenFirstGiven() => driver.A_tenant_exists();
    [BeforeScenario("everyfile")] public Task EveryFile() => driver.Reference_data_is_attached();

    [AfterTestRun(Order = int.MaxValue)]
    public static void Report() => ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
    });
}
