// S11: var e = await Should.ThrowAsync<T>(...); read after a later await (kept in a field in Release too)
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
        var e = await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
        await Task.Yield();
        e.Message.ShouldBe("not found");
    }
}
