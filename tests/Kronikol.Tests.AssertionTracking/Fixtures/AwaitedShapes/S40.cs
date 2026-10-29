// S40: TUnit: await Assert.That(async () => await _client.GetNameAsync()).IsEqualTo(copy); with copy a plain local, after a suspension (#211)
// Builds: Debug, Release, ReleaseDebugPlus
// References: TUnit.Assertions
// AssertionPasses: ✓ Client get name async() is equal to 'order'
// AssertionFails: ✗ Client get name async() is equal to 'other'
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
        var copy = expected;
        await Assert.That(async () => await _client.GetNameAsync()).IsEqualTo(copy);
    }
}
