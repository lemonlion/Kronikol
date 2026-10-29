// S17g: TUnit, a local read between the statement's two awaits: await Assert.That(await _client.GetNameAsync()).IsEqualTo(copy); (F8)
// Builds: Debug, Release, ReleaseDebugPlus
// References: TUnit.Assertions
// copy is read after the inner await and before the assertion's own: every build draws its value (#211).
// AssertionPasses: ✓ Await _client.GetNameAsync() is equal to 'order'
// AssertionFails: ✗ Await _client.GetNameAsync() is equal to 'other'
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
