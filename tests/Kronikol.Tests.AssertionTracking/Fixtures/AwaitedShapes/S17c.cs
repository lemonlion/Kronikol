// S17c: TUnit, an awaited value inside an awaited assertion: await Assert.That(await _client.GetNameAsync()).IsEqualTo(expected); (F8)
// Builds: Debug, Release, ReleaseDebugPlus
// References: TUnit.Assertions
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
        await Assert.That(await _client.GetNameAsync()).IsEqualTo(expected);
    }
}
