// S39: FluentAssertions: await act.Should().CompleteWithinAsync(timeout); with timeout a value-type local (#211: its default in Release)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Act should complete within async '00:00:30'
// AssertionFails: ✗ Act should complete within async '00:00:00.0500000'
#nullable enable
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Kronikol.Tracking;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run(30_000, () => _client.GetNameAsync());

    public Task AssertionFails() => Run(50, () => new TaskCompletionSource().Task);

    private async Task Run(int milliseconds, Func<Task> act)
    {
        var timeout = TimeSpan.FromMilliseconds(milliseconds);
        await act.Should().CompleteWithinAsync(timeout);
    }
}
