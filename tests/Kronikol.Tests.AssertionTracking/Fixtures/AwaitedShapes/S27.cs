// S27: an awaited assertion in a using block: await Should.ThrowAsync<T>(() => _client.GetOrderAsync(request)); (#211: plain Release keeps the closure in a local there too)
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
        using (var gate = new System.IO.MemoryStream())
        {
            var request = new OrderRequest { Id = id };
            await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
        }
    }
}
