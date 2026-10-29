// S35: an awaited assertion in an async local function, its parameter captured: async Task Check(OrderRequest request) => await Should.ThrowAsync<T>(…); (#211)
// Builds: Debug, Release, ReleaseDebugPlus
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
        await Check(new OrderRequest { Id = id });

        async Task Check(OrderRequest request) =>
            await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
    }
}
