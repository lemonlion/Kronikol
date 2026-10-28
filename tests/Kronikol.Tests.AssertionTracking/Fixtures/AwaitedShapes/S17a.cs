// S17a: Shouldly, an assertion on an awaited value: (await _client.GetNameAsync()).ShouldBe(expected); (F8)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ (await _client.GetNameAsync()) should be 'order'
// AssertionFails: ✗ (await _client.GetNameAsync()) should be 'other'
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("order");

    public Task AssertionFails() => Run("other");

    private async Task Run(string expected)
    {
        await Task.Yield();
        (await _client.GetNameAsync()).ShouldBe(expected);
    }
}
