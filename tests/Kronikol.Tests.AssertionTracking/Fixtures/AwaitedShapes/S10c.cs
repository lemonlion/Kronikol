// S10c: var v = Should.NotThrow(() => _client.GetOrderAsync(request)); (Shouldly 4.3.0's value-returning NotThrow, synchronous)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Should not throw | ✓ V should be "order"
// AssertionFails: ✗ Should not throw
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
        var v = Should.NotThrow(() => _client.GetOrderAsync(request));
        v.ShouldBe("order");
    }
}
