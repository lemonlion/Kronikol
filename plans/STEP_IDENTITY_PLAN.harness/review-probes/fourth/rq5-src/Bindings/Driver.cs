using System.Net;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;

[assembly: TrackSteps]

namespace Rq5;

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
    [Step] public async Task Reference_data_is_cleaned() => await Post("clean");
    [Step] public async Task Block_prepared() => await Post("block");
    [GivenStep] public async Task A_tenant_exists() => await Post("tenant");
    [Step] public async Task Seeding_fails() { await Post("seedfail"); throw new InvalidOperationException("seeding failed"); }
    [Step] public async Task Cleaning_fails() { await Post("cleanfail"); throw new InvalidOperationException("cleaning failed"); }
    [Step] public async Task A_customer_is_registered() => await Post("customers");
    [Step] public async Task An_inner_helper() => await Post("inner");
}
