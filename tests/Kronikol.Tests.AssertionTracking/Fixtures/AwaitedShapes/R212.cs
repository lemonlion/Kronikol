// R212: #212's reproduction, verbatim but for xUnit ([Fact], its usings, the test framework and TestTracking attributes)
// Builds: Debug, Release, ReleaseDebugPlus
// Type: LabelRepro.LabelTests
// Shared: none
// Awaited_ThrowAsync: ✓ Should throw InvalidOperationException
// Assigned_ThrowAsync: ✓ Should throw InvalidOperationException | ✓ Exception message should be "not found"
// ThrowAsync_returned_by_a_helper: ✓ Should throw InvalidOperationException | ✓ Exception message should be "not found"
// NotThrowAsync: ✓ Should not throw
// Throw: ✓ Should throw InvalidOperationException
// The issue's project has ImplicitUsings; the two it needs are written out here.
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

namespace LabelRepro;

public class LabelTests
{
    private readonly OrdersClient _client = new();

    public async Task Awaited_ThrowAsync()
    {
        await Task.Yield();
        var request = new OrderRequest { Id = "missing" };

        await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
    }

    public async Task Assigned_ThrowAsync()
    {
        await Task.Yield();
        var request = new OrderRequest { Id = "missing" };

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () => await _client.GetOrderAsync(request));

        exception.Message.ShouldBe("not found");
    }

    public async Task ThrowAsync_returned_by_a_helper()
    {
        var exception = await GetOrderShouldFail("missing");

        exception.Message.ShouldBe("not found");
    }

    public async Task NotThrowAsync()
    {
        await Task.Yield();

        await Should.NotThrowAsync(() => _client.GetOrderAsync(new OrderRequest { Id = "known" }));
    }

    public void Throw()
    {
        var request = new OrderRequest { Id = "missing" };

        Should.Throw<InvalidOperationException>(() => _client.GetOrder(request));
    }

    private async Task<InvalidOperationException> GetOrderShouldFail(string id)
    {
        await Task.Yield();
        return await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(new OrderRequest { Id = id }));
    }
}

public sealed class OrderRequest
{
    public string Id { get; init; } = "";
}

public sealed class OrdersClient
{
    public async Task<string> GetOrderAsync(OrderRequest request)
    {
        await Task.Yield();
        return request.Id == "known" ? "order" : throw new InvalidOperationException("not found");
    }

    public string GetOrder(OrderRequest request) =>
        request.Id == "known" ? "order" : throw new InvalidOperationException("not found");
}
