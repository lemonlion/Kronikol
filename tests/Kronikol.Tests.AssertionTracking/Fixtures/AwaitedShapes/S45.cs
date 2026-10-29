// S45: a generic async method of the test class: private async Task Run<T>(T id) (#211: D-B, the state machine generic)
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

    private async Task Run<T>(T id)
    {
        var copy = id;
        await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(new OrderRequest { Id = copy!.ToString()! }));
    }
}
