// S9: var e = Should.Throw<T>(() => _client.GetOrder(request)); (synchronous, assigned)
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
        var request = new OrderRequest { Id = id };
        var e = Should.Throw<InvalidOperationException>(() => _client.GetOrder(request));
        e.Message.ShouldBe("not found");
    }
}
