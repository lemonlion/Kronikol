// S2: var exception = await Should.ThrowAsync<T>(async () => await _client.GetOrderAsync(request)); (#212's assigned row)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Should throw InvalidOperationException | ✓ Exception message should be "not found"
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
        var request = new OrderRequest { Id = id };
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () => await _client.GetOrderAsync(request));
        exception.Message.ShouldBe("not found");
    }
}
