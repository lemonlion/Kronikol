// S15d: a helper that is not async, block body: { return Should.NotThrowAsync(...); }
// Builds: Debug, ReleaseDebugPlus
// An un-awaited assertion task: drawn when the task ends, with its outcome; Track.AssertionTask observes it (plan 4.1 f, R2). R1 drew it passed when the task was made.
// Plain Release is left out: there #211's D-A reads the closure kept on the stack off this, and the woven IL does not verify (plan 3.10). The issue's xUnit v2 suites build Release with debug-plus.
// AssertionPasses: ✓ Should not throw
// AssertionFails: ✗ Should not throw
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
        await Ok(new OrderRequest { Id = "known" });
    }

    public async Task AssertionFails()
    {
        await Ok(new OrderRequest { Id = "missing" });
    }

    private Task Ok(OrderRequest r)
    {
        return Should.NotThrowAsync(() => _client.GetOrderAsync(r));
    }
}
