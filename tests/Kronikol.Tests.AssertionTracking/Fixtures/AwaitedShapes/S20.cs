// S20: a second await after the assertion's, in one statement: (await Should.ThrowAsync<T>(...)).Message.ShouldBe(await _client.GetExpectedMessageAsync()); (refused)
// Builds: Debug, Release, ReleaseDebugPlus
// Unwoven: Run: an await in the statement comes after the assertion
// AssertionPasses: (none)
// AssertionFails: (none)
// MessageDiffers: (none)
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public Task AssertionPasses() => Run("missing", "not found");

    public Task AssertionFails() => Run("known", "not found");

    public Task MessageDiffers() => Run("missing", "other");

    private async Task Run(string id, string expected)
    {
        await Task.Yield();
        var r = new OrderRequest { Id = id };
        _client.ExpectedMessage = expected;
        (await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(r))).Message.ShouldBe(await _client.GetExpectedMessageAsync());
    }
}
