// S46: synchronous, a test class whose first field is a long: Should.Throw<T>(() => new OrdersClient().GetOrder(request)); (#211: D-A read the long as a reference and crashed the test process in plain Release)
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
    private readonly long _number = 0x0000_1234_5678_9ABC;

    public long Number => _number;

    public void AssertionPasses() => Run("missing");

    public void AssertionFails() => Run("known");

    private void Run(string id)
    {
        var request = new OrderRequest { Id = id };
        Should.Throw<InvalidOperationException>(() => new OrdersClient().GetOrder(request));
    }
}
