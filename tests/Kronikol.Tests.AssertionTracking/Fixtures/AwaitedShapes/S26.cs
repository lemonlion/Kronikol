// S26: a statement that waits for its assertion's task itself: Should.ThrowAsync<T>(...).GetAwaiter().GetResult(); and .Wait(); (not observed: its catch draws the failure)
// Builds: Debug, ReleaseDebugPlus
// Plain Release is left out: there #211's D-A reads the closure kept on the stack off this, and the woven IL does not verify (plan 3.10). The issue's xUnit v2 suites build Release with debug-plus.
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
