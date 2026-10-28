// S13: TUnit: var v = await Assert.That(x).IsNotNull();
// Builds: Debug, Release, ReleaseDebugPlus
// References: TUnit.Assertions
// AssertionPasses: ✓ X is not null | ✓ V is equal to "value"
// AssertionFails: ✗ X is not null
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

    public Task AssertionPasses() => Run("value");

    public Task AssertionFails() => Run(null);

    private async Task Run(string? x)
    {
        await Task.Yield();
        var v = await Assert.That(x).IsNotNull();
        await Assert.That(v).IsEqualTo("value");
    }
}
