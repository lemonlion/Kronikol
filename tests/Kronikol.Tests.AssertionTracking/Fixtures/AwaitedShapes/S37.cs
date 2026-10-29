// S37: FluentAssertions: await act.Should().ThrowAsync<T>().WithMessage(message); with message a plain local (#211, P9: 'null' in Release)
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

    private async Task Run(string expected)
    {
        var message = expected;
        Func<Task> act = () => _client.GetOrderAsync(new OrderRequest { Id = "missing" });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
    }
}
