// S36: FluentAssertions: await _client.Awaiting(c => c.GetOrderAsync(request)).Should().ThrowAsync<T>(); (#211)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Client awaiting(c => c.GetOrderAsync(request)) should throw async ~<InvalidOperationException>
// AssertionFails: ✗ Client awaiting(c => c.GetOrderAsync(request)) should throw async ~<InvalidOperationException>
#nullable enable
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Kronikol.Tracking;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("missing");

    public Task AssertionFails() => Run("known");

    private async Task Run(string id)
    {
        var request = new OrderRequest { Id = id };
        await _client.Awaiting(c => c.GetOrderAsync(request)).Should().ThrowAsync<InvalidOperationException>();
    }
}
