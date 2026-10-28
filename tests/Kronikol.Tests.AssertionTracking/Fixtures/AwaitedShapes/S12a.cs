// S12a: FluentAssertions: var a = await act.Should().ThrowAsync<T>();
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Act should throw async ~<InvalidOperationException> | ✓ A which message should be "not found"
// AssertionFails: ✗ Act should throw async ~<InvalidOperationException>
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
        var a = await act.Should().ThrowAsync<InvalidOperationException>();
        a.Which.Message.Should().Be("not found");
    }
}
