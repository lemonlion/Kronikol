// S17f: Shouldly, an awaited argument that reads a captured local before the await: "order".ShouldBe(await _client.GetOrderAsync(request)); (F8)
// Builds: Debug, Release, ReleaseDebugPlus
// request is read only inside the awaited argument, before the await, so it shows as written: the argument's value is the await's result.
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
