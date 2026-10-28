// S24c: a suite's own async Shouldly assertion ([ShouldlyMethods]) with ConfigureAwait: await _client.ShouldBeKnownAsync(id).ConfigureAwait(false);
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Client should be known 'known'
// AssertionFails: ✗ Client should be known 'missing'
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("known");

    public Task AssertionFails() => Run("missing");

    private async Task Run(string id)
    {
        await Task.Yield();
        await _client.ShouldBeKnownAsync(id).ConfigureAwait(false);
    }
}

[ShouldlyMethods]
public static class OrderAssertions
{
    public static async Task ShouldBeKnownAsync(this OrdersClient client, string id)
    {
        try
        {
            await client.GetOrderAsync(new OrderRequest { Id = id });
        }
        catch (InvalidOperationException e)
        {
            throw new ShouldAssertException($"order {id} should be known, but the client said: {e.Message}");
        }
    }
}
