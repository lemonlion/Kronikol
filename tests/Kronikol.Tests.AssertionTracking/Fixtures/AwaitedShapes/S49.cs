// S49: await Should.NotThrowAsync(async () => { await _client.GetNameAsync(); request.Id.ShouldNotBeEmpty(); }); its closure read after the await (#211)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Request id should not be empty | ✓ Should not throw
// AssertionFails: ✗ Request id should not be empty | ✗ Should not throw
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

    public Task AssertionFails() => Run("");

    private async Task Run(string id)
    {
        var request = new OrderRequest { Id = id };
        await Should.NotThrowAsync(async () => { await _client.GetNameAsync(); request.Id.ShouldNotBeEmpty(); });
    }
}
