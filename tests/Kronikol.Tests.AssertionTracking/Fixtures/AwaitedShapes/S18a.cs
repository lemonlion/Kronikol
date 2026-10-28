// S18a: TUnit, an awaited assertion whose result is used: var e = await Assert.That(() => _client.GetOrderAsync(r)).Throws<T>();
// Builds: Debug, Release, ReleaseDebugPlus
// References: TUnit.Assertions
// AssertionPasses: ✓ Client get order async(r) throws ~<InvalidOperationException> | ✓ E message is equal to "not found"
// AssertionFails: ✗ Client get order async(r) throws ~<InvalidOperationException>
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

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
        var e = await Assert.That(() => _client.GetOrderAsync(r)).Throws<InvalidOperationException>();
        await Assert.That(e!.Message).IsEqualTo("not found");
    }
}
