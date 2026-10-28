// S16a: FluentAssertions: (await act.Should().ThrowAsync<T>()).Which.Message.Should().Be(...), the trailing Be holding, then not (F5)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ (await act.Should().ThrowAsync~<InvalidOperationException>()) which message should be 'not found'
// AssertionFails: ✗ (await act.Should().ThrowAsync~<InvalidOperationException>()) which message should be 'other'
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using FluentAssertions;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("not found");

    public Task AssertionFails() => Run("other");

    private async Task Run(string expected)
    {
        await Task.Yield();
        var request = new OrderRequest { Id = "missing" };
        Func<Task> act = () => _client.GetOrderAsync(request);
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(expected);
    }
}
