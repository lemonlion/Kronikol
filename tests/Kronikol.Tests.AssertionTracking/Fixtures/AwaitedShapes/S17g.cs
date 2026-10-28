// S17g: TUnit, a local read between the statement's two awaits: await Assert.That(await _client.GetNameAsync()).IsEqualTo(copy); (F8)
// Builds: Debug, Release, ReleaseDebugPlus
// References: TUnit.Assertions
// A statement first woven by #212 shows only the values it reads after its merge point: a closure or local read before the await is left out in every build (in plain Release it is gone after the resumption, #211).
// AssertionPasses: ✓ Await _client.GetNameAsync() is equal to copy
// AssertionFails: ✗ Await _client.GetNameAsync() is equal to copy
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("order");

    public Task AssertionFails() => Run("other");

    private async Task Run(string expected)
    {
        await Task.Yield();
        var copy = expected;
        await Assert.That(await _client.GetNameAsync()).IsEqualTo(copy);
    }
}
