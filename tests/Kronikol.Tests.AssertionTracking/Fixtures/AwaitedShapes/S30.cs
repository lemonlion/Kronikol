// S30: an awaited assertion in a foreach after an await, its closure made in the loop (#211, P2)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Should throw InvalidOperationException | ✓ Should throw InvalidOperationException
// AssertionFails: ✓ Should throw InvalidOperationException | ✗ Should throw InvalidOperationException
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("missing", "absent");

    public Task AssertionFails() => Run("missing", "known");

    private async Task Run(params string[] ids)
    {
        await Task.Yield();
        foreach (var id in ids)
        {
            var request = new OrderRequest { Id = id };
            await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
        }
    }
}
