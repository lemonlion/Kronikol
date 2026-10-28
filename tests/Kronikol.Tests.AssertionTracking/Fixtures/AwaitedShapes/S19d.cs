// S19d: an expression-bodied test returning the assertion's task: public Task Test() => Should.ThrowAsync<T>(...);
// Builds: Debug, Release, ReleaseDebugPlus
// An un-awaited assertion task: R1 keeps today's note, drawn as passed when the task is made (pinned), until R2 observes the task (plan 4.1 f).
// AssertionPasses: ✓ Should throw InvalidOperationException
// AssertionFails: ✓ Should throw InvalidOperationException
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    private readonly OrderRequest _missing = new() { Id = "missing" };
    private readonly OrderRequest _known = new() { Id = "known" };

    public Task AssertionPasses() => Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(_missing));

    public Task AssertionFails() => Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(_known));
}
