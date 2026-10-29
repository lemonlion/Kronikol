// S48: FluentAssertions, a value loaded before both of the statement's awaits: actual.Should().Be(copy, await A() + await B()); (#211, 4.1 i: carried at the first await, not the last)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Actual should be 'order', await _client.GetNameAsync() + await _client.GetNameAsync()
// AssertionFails: ✗ Actual should be 'other', await _client.GetNameAsync() + await _client.GetNameAsync()
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
        actual.Should().Be(copy, await _client.GetNameAsync() + await _client.GetNameAsync());
    }
}
