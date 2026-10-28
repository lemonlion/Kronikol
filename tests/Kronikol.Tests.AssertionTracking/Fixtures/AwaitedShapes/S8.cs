// S8: var e = await Should.ThrowAsync<T>(...).ConfigureAwait(false);
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Should throw InvalidOperationException | ✓ E message should be "not found"
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
        var e = await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request)).ConfigureAwait(false);
        e.Message.ShouldBe("not found");
    }
}
