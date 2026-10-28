// S9b: return Should.Throw<T>(...) from a synchronous helper (F6: a Debug return never recorded its pass)
// Builds: Debug, ReleaseDebugPlus
// Plain Release is left out: there #211's D-A reads the closure kept on the stack off this, and the woven IL does not verify (plan 3.10). The issue's xUnit v2 suites build Release with debug-plus.
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

    public void AssertionPasses() => Run("missing");

    public void AssertionFails() => Run("known");

    private void Run(string id)
    {
        var e = GetOrderShouldFail(id);
        e.Message.ShouldBe("not found");
    }

    private InvalidOperationException GetOrderShouldFail(string id)
    {
        return Should.Throw<InvalidOperationException>(() => _client.GetOrder(new OrderRequest { Id = id }));
    }
}
