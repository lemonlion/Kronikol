// S44: a generic base class: awaited and synchronous assertions in Base<T>, run through Probe : Base<int> (#211: D-B in every build, D-A in plain Release)
// Builds: Debug, Release, ReleaseDebugPlus
// AsyncPasses: ✓ Should throw InvalidOperationException
// AsyncFails: ✗ Should throw InvalidOperationException
// SyncPasses: ✓ Should throw InvalidOperationException
// SyncFails: ✗ Should throw InvalidOperationException
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe : Base<int>
{
}

public abstract class Base<T>
{
    protected readonly OrdersClient _client = new();

    public Task AsyncPasses() => Run("missing");

    public Task AsyncFails() => Run("known");

    public void SyncPasses() => RunSync("missing");

    public void SyncFails() => RunSync("known");

    private async Task Run(string id)
    {
        var request = new OrderRequest { Id = id };
        await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
    }

    private void RunSync(string id)
    {
        var request = new OrderRequest { Id = id };
        Should.Throw<InvalidOperationException>(() => _client.GetOrder(request));
    }
}
