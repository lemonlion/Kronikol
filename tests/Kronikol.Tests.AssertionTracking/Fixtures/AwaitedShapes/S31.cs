// S31: await Should.ThrowAsync<T>(() => _client.GetOrderAsync(request)).ConfigureAwait(false); the closure read after the await (#211)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Should throw InvalidOperationException
// AssertionFails: ✗ Should throw InvalidOperationException
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("missing");

    public Task AssertionFails() => Run("known");

    private async Task Run(string id)
    {
        var request = new OrderRequest { Id = id };
        await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request)).ConfigureAwait(false);
    }
}
