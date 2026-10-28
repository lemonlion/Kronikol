// S22b: TUnit scope: using (Assert.Multiple()) { var v = await Assert.That(x).IsEqualTo(y); }
// Builds: Debug, Release, ReleaseDebugPlus
// References: TUnit.Assertions
// AssertionPasses: ✓ 1 is equal to '1'
// AssertionFails: ✗ 1 is equal to '2'
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

    public Task AssertionPasses() => Run(1);

    public Task AssertionFails() => Run(2);

    private async Task Run(int expected)
    {
        await Task.Yield();
        using (Assert.Multiple())
        {
            var v = await Assert.That(1).IsEqualTo(expected);
        }
    }
}
