// S15a: a helper that is not async, expression-bodied: Task<T> Fails(r) => Should.ThrowAsync<T>(...); the test awaits it
// Builds: Debug, Release, ReleaseDebugPlus
// An un-awaited assertion task: drawn when the task ends, with its outcome; Track.AssertionTask observes it (plan 4.1 f, R2). R1 drew it passed when the task was made.
// AssertionPasses: ✓ Should throw InvalidOperationException | ✓ E message should be "not found"
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

    public async Task AssertionPasses()
    {
        var e = await Fails(new OrderRequest { Id = "missing" });
        e.Message.ShouldBe("not found");
    }

    public async Task AssertionFails()
    {
        await Fails(new OrderRequest { Id = "known" });
    }

    private Task<InvalidOperationException> Fails(OrderRequest r) => Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(r));
}
