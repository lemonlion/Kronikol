// S18c: AwesomeAssertions: var a = await act.Should().ThrowAsync<T>(); then a used
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Act should throw async ~<InvalidOperationException> | ✓ A which message should be "not found"
// AssertionFails: ✗ Act should throw async ~<InvalidOperationException>
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using AwesomeAssertions;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("missing");

    public Task AssertionFails() => Run("known");

    private async Task Run(string id)
    {
        await Task.Yield();
        var r = new OrderRequest { Id = id };
        Func<Task> act = () => _client.GetOrderAsync(r);
        var a = await act.Should().ThrowAsync<InvalidOperationException>();
        a.Which.Message.Should().Be("not found");
    }
}
