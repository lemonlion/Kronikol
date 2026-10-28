// S21b: a bare await across lines, the method's last statement: await Should.ThrowAsync<T>( / () => _client.GetOrderAsync(r));
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
        await Should.ThrowAsync<InvalidOperationException>(
            () => _client.GetOrderAsync(r));
    }
}
