// S16b: Shouldly: (await Should.ThrowAsync<T>(...)).Message.ShouldBe(...), the trailing ShouldBe holding, then not (F5)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ (await Should.ThrowAsync~<InvalidOperationException>(() => _client.GetOrderAsync(request))) message should be 'not found'
// AssertionFails: ✗ (await Should.ThrowAsync~<InvalidOperationException>(() => _client.GetOrderAsync(request))) message should be 'other'
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("not found");

    public Task AssertionFails() => Run("other");

    private async Task Run(string expected)
    {
        await Task.Yield();
        var request = new OrderRequest { Id = "missing" };
        (await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request))).Message.ShouldBe(expected);
    }
}
