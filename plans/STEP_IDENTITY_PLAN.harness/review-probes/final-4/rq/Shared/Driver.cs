using System.Net;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;

[assembly: TrackSteps]

namespace EngineFixtures;

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

    [Step] public async Task Seed() => await Post("seed");
    [Step] public async Task Seed_fails() { await Post("seed-fail"); throw new InvalidOperationException("seed failed"); }
    [Step] public async Task Clean() => await Post("clean");
    [Step] public async Task Clean_fails() { await Post("clean-fail"); throw new InvalidOperationException("clean failed"); }
    [Step] public async Task Customer() => await Post("customers");
    [Step] public async Task Order() => await Post("orders");
    [Step] public async Task Read_back() => await Post("orders/1");
    [Step] public async Task Late_after_skip() => await Post("late");
    [Step] public async Task Wrapper_step_method(Func<Task> inner) { await Post("wrapper"); await inner(); await Post("wrapper-end"); }
}
