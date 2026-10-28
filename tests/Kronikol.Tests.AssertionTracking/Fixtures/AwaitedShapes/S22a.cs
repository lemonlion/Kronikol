// S22a: FluentAssertions scope: using (new AssertionScope()) { await act.Should().ThrowAsync<T>(); }
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Act should throw async ~<InvalidOperationException>
// AssertionFails: ✗ Act should throw async ~<InvalidOperationException>
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using FluentAssertions;
using FluentAssertions.Execution;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("missing");

    public Task AssertionFails() => Run("known");

    private async Task Run(string id)
    {
        await Task.Yield();
        Func<Task> act = () => _client.GetOrderAsync(new OrderRequest { Id = id });
        using (new AssertionScope())
        {
            var a = await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
