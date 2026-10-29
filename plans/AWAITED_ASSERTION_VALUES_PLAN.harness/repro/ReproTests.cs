using Kronikol.Tracking;
using Kronikol.xUnit2;
using Shouldly;
using Xunit;

[assembly: TestTracking]
[assembly: TrackAssertions]

namespace WeaveRepro;

public class ReproTests
{
    private readonly Client _client = new();

    [Fact]
    public async Task ThrowAsync_is_the_first_await()
    {
        var request = new Request { Id = string.Empty };

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await _client.CallAsync(request)
        );

        exception.Message.ShouldBe("expected");   // NullReferenceException in Release
    }

    [Fact]
    public async Task Unassigned_ThrowAsync_is_the_first_await()
    {
        var request = new Request { Id = string.Empty };

        await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));   // NullReferenceException in Release
    }

    [Fact]
    public async Task Another_await_comes_first()
    {
        await Task.Yield();
        var request = new Request { Id = string.Empty };

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await _client.CallAsync(request)
        );

        exception.Message.ShouldBe("expected");   // passes
    }
}

public sealed class Request
{
    public string Id { get; init; } = "";
}

public sealed class Client
{
    public async Task<string> CallAsync(Request request)
    {
        await Task.Yield();
        throw new InvalidOperationException("expected");
    }
}
