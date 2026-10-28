// S12b: FluentAssertions: (await act.Should().ThrowAsync<T>()).Which.Message.Should().Be("not found"); (one compound statement)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ (await act.Should().ThrowAsync~<InvalidOperationException>()) which message should be "not found"
// AssertionFails: ✗ (await act.Should().ThrowAsync~<InvalidOperationException>()) which message should be "not found"
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using FluentAssertions;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("missing");

    public Task AssertionFails() => Run("known");

    private async Task Run(string id)
    {
        await Task.Yield();
        var request = new OrderRequest { Id = id };
        Func<Task> act = () => _client.GetOrderAsync(request);
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("not found");
    }
}
