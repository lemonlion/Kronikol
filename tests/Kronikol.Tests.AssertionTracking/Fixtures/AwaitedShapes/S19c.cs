// S19c: Shouldly, a stored task: var t = Should.ThrowAsync<T>(...); await t;
// Builds: Debug, Release, ReleaseDebugPlus
// An un-awaited assertion task: drawn when the task ends, with its outcome; Track.AssertionTask observes it (plan 4.1 f, R2). R1 drew it passed when the task was made.
// AssertionPasses: ✓ Should throw InvalidOperationException
// AssertionFails: ✗ Should throw InvalidOperationException
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

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
        var t = Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(r));
        await t;
    }
}
