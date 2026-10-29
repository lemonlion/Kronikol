// S9b: return Should.Throw<T>(...) from a synchronous helper (F6: a Debug return never recorded its pass)
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
