// S19a: FluentAssertions, a helper returning the un-awaited task: Task<ExceptionAssertions<T>> Fails(act) => act.Should().ThrowAsync<T>();
// Builds: Debug, Release, ReleaseDebugPlus
// An un-awaited assertion task: R1 keeps today's note, drawn as passed when the task is made (pinned), until R2 observes the task (plan 4.1 f).
// AssertionPasses: ✓ Act should throw async ~<InvalidOperationException>
// AssertionFails: ✓ Act should throw async ~<InvalidOperationException>
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using FluentAssertions;
using FluentAssertions.Specialized;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public async Task AssertionPasses()
    {
        await Fails(() => _client.GetOrderAsync(new OrderRequest { Id = "missing" }));
    }

    public async Task AssertionFails()
    {
        await Fails(() => _client.GetOrderAsync(new OrderRequest { Id = "known" }));
    }

    private Task<ExceptionAssertions<InvalidOperationException>> Fails(Func<Task> act) => act.Should().ThrowAsync<InvalidOperationException>();
}
