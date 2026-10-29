// S51: a static helper whose lambda reads the class's field through a parameter: Should.Throw<T>(() => probe._client.GetOrder(request)); (#211: a static method has no this, and the weave read the field off its first parameter, an int)
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

    public void AssertionPasses() => Run(1, this, "missing");

    public void AssertionFails() => Run(1, this, "known");

    private static void Run(int attempt, Probe probe, string id)
    {
        var request = new OrderRequest { Id = id };
        Should.Throw<InvalidOperationException>(() => probe._client.GetOrder(request));
    }
}
