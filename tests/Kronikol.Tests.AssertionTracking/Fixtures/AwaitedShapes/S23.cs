// S23: an awaited assertion passed into another call: Console.WriteLine((await Should.ThrowAsync<T>(...)).Message);
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
        await Task.Yield();
        var r = new OrderRequest { Id = id };
        Console.WriteLine((await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(r))).Message);
    }
}
