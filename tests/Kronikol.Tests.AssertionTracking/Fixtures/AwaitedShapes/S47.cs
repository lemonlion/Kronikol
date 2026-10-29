// S47: FluentAssertions, a value loaded before the statement's only await: actual.Should().Be(copy, await _client.GetNameAsync()); (#211, 4.1 i: copy is carried across the await)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Actual should be 'order', await _client.GetNameAsync()
// AssertionFails: ✗ Actual should be 'other', await _client.GetNameAsync()
#nullable enable
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Kronikol.Tracking;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("order", "order");

    public Task AssertionFails() => Run("order", "other");

    private async Task Run(string actual, string expected)
    {
        await Task.Yield();
        var copy = expected;
        actual.Should().Be(copy, await _client.GetNameAsync());
    }
}
