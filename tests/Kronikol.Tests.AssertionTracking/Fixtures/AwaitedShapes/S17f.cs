// S17f: Shouldly, an awaited argument that reads a captured local before the await: "order".ShouldBe(await _client.GetOrderAsync(request)); (F8)
// Builds: Debug, Release, ReleaseDebugPlus
// A statement first woven by #212 shows only the values it reads after its merge point: a closure or local read before the await is left out in every build (in plain Release it is gone after the resumption, #211).
// AssertionPasses: ✓ "order" should be await _client.GetOrderAsync(request)
// AssertionFails: ✗ "order" should be await _client.GetOrderAsync(request)
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("known");

    public Task AssertionFails() => Run("missing");

    private async Task Run(string id)
    {
        await Task.Yield();
        var request = new OrderRequest { Id = id };
        Func<Task<string>> again = () => _client.GetOrderAsync(request);
        "order".ShouldBe(await _client.GetOrderAsync(request));
    }
}
