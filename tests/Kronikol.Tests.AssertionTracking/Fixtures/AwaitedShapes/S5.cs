// S5: InvalidOperationException e = await Should.ThrowAsync<T>(...); (a declaration with its type)
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
        InvalidOperationException e = await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
        e.Message.ShouldBe("not found");
    }
}
