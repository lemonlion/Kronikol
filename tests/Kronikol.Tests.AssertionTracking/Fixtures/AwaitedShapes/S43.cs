// S43: synchronous: Should.Throw<T>(() => new OrdersClient().GetOrder(request)); a lambda that captures only the local (#211: D-A in plain Release)
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
    public void AssertionPasses() => Run("missing");

    public void AssertionFails() => Run("known");

    private void Run(string id)
    {
        var request = new OrderRequest { Id = id };
        Should.Throw<InvalidOperationException>(() => new OrdersClient().GetOrder(request));
    }
}
