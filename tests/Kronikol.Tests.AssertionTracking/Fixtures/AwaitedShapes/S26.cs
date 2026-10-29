// S26: a statement that waits for its assertion's task itself: Should.ThrowAsync<T>(...).GetAwaiter().GetResult(); and .Wait(); (not observed: its catch draws the failure)
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

    public void AssertionPasses()
    {
        var request = new OrderRequest { Id = "missing" };
        Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request)).GetAwaiter().GetResult();
    }

    public void AssertionFails()
    {
        var request = new OrderRequest { Id = "known" };
        Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request)).Wait();
    }
}
