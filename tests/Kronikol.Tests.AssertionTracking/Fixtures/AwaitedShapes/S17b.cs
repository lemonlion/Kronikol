// S17b: FluentAssertions, an assertion on an awaited value: (await _client.GetNameAsync()).Should().Be(expected); (F8)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ (await _client.GetNameAsync()) should be 'order'
// AssertionFails: ✗ (await _client.GetNameAsync()) should be 'other'
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using FluentAssertions;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("order");

    public Task AssertionFails() => Run("other");

    private async Task Run(string expected)
    {
        await Task.Yield();
        (await _client.GetNameAsync()).Should().Be(expected);
    }
}
