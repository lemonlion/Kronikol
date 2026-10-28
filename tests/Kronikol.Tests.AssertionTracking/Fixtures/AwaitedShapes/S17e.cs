// S17e: FluentAssertions, the awaited value as the argument: actual.Should().Be(await _client.GetNameAsync()); (F8)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Actual should be await _client.GetNameAsync()
// AssertionFails: ✗ Actual should be await _client.GetNameAsync()
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

    private async Task Run(string actual)
    {
        await Task.Yield();
        actual.Should().Be(await _client.GetNameAsync());
    }
}
