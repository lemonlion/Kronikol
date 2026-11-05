using System.Net;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;

[assembly: TrackSteps]

namespace EngineFixtures;

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
    [Step] public async Task Seed_for(string name) => await Post("seed/" + name);

    [Step]
    public async Task A_seed_that_fails()
    {
        await Post("seed");
        throw new InvalidOperationException("a seed failed");
    }

    [Step] public async Task Reference_data_is_cleaned() => await Post("clean");
    [Step] public async Task Block_prepared() => await Post("block");
    [Step] public async Task Step_observed() => await Post("observe");
    [GivenStep] public async Task A_tenant_exists() => await Post("tenant");

    [Step]
    public async Task Seeding_fails()
    {
        await Post("seed");
        throw new InvalidOperationException("seeding failed");
    }

    [Step] public async Task A_slow_background_check() { await Task.Delay(300); await Post("background"); }
    [Step] public async Task A_quick_check() { await Task.Delay(10); await Post("quick"); }
    [Step] public async Task An_order_is_submitted_slowly() { await Task.Delay(500); await Post("orders"); }
    [Step] public async Task First_parallel() { await Task.Delay(40); await Post("first"); }
    [Step] public async Task Second_parallel() { await Task.Delay(10); await Post("second"); }

    [Step] public async Task A_customer_is_registered() => await Post("customers");
    [Step] public async Task An_order_is_submitted() => await Post("orders");
    [Step] public async Task The_order_is_read_back() => await Post("orders/1");
}
