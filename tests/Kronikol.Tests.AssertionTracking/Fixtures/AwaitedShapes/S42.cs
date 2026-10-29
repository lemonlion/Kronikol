// S42: an async method that never uses this: await Should.ThrowAsync<T>(() => new OrdersClient().GetOrderAsync(request)); (#211: D-A in plain Release, the closure kept on the stack and read off the state machine)
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
    public Task AssertionPasses() => Run("missing");

    public Task AssertionFails() => Run("known");

    private async Task Run(string id)
    {
        var request = new OrderRequest { Id = id };
        await Should.ThrowAsync<InvalidOperationException>(() => new OrdersClient().GetOrderAsync(request));
    }
}
