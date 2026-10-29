// S29: an awaited assertion in a try with a finally, its closure made before the try (#211)
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

    public bool Done { get; private set; }

    private async Task Run(string id)
    {
        var request = new OrderRequest { Id = id };
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
        }
        finally
        {
            Done = true;
        }
    }
}
