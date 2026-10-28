// S10: await Should.NotThrowAsync(() => _client.GetOrderAsync(new OrderRequest { Id = id })); (#212's NotThrowAsync row)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Should not throw
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

        await Should.NotThrowAsync(() => _client.GetOrderAsync(new OrderRequest { Id = id }));
    }
}
