// S38: FluentAssertions: await act.Should().ThrowAsync<T>().WithMessage(request.Id); reading act's closure (#211, P10)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Act should throw async ~<InvalidOperationException> with message 'not found'
// AssertionFails: ✗ Act should throw async ~<InvalidOperationException> with message 'other'
#nullable enable
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Kronikol.Tracking;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("not found");

    public Task AssertionFails() => Run("other");

    private async Task Run(string id)
    {
        var request = new OrderRequest { Id = id };
        Func<Task> act = () => _client.GetOrderAsync(request);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(request.Id);
    }
}
