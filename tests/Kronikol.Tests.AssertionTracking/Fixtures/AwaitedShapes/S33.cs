// S33: an awaited assertion in an async method returning ValueTask (#211)
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

    public Task AssertionPasses() => Run("missing").AsTask();

    public Task AssertionFails() => Run("known").AsTask();

    private async ValueTask Run(string id)
    {
        var request = new OrderRequest { Id = id };
        await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
    }
}
