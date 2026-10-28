// S14b: S1 with no await before it
// Builds: Debug
// Debug only: without the await before it, plain and debug-plus Release read the closure after the await from a local the resumption has lost, #211's defect (plan 3.9, section 8).
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
        var request = new OrderRequest { Id = id };
        await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
    }
}
