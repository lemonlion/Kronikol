// S4: return await Should.ThrowAsync<T>(...) in an async helper the test awaits (#212's returned row)
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
        var e = await GetOrderShouldFail(id);
        e.Message.ShouldBe("not found");
    }

    private async Task<InvalidOperationException> GetOrderShouldFail(string id)
    {
        await Task.Yield();
        return await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(new OrderRequest { Id = id }));
    }
}
