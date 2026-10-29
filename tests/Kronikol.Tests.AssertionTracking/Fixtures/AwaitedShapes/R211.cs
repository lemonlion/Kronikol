// R211: #211's reproduction, verbatim but for xUnit ([Fact], its usings, the test framework and TestTracking attributes)
// Builds: Debug, Release, ReleaseDebugPlus
// Type: WeaveRepro.ReproTests
// Shared: none
// ThrowAsync_is_the_first_await: ✓ Should throw InvalidOperationException | ✓ Exception message should be "expected"
// Unassigned_ThrowAsync_is_the_first_await: ✓ Should throw InvalidOperationException
// Another_await_comes_first: ✓ Should throw InvalidOperationException | ✓ Exception message should be "expected"
// The issue's project has ImplicitUsings; the two it needs are written out here.
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

namespace WeaveRepro;

public class ReproTests
{
    private readonly Client _client = new();

    public async Task ThrowAsync_is_the_first_await()
    {
        var request = new Request { Id = string.Empty };

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await _client.CallAsync(request)
        );

        exception.Message.ShouldBe("expected");   // NullReferenceException in Release
    }

    public async Task Unassigned_ThrowAsync_is_the_first_await()
    {
        var request = new Request { Id = string.Empty };

        await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));   // NullReferenceException in Release
    }

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
