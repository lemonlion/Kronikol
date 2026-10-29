using FluentAssertions;
using Kronikol.Tracking;
using Kronikol.xUnit2;
using Shouldly;
using Xunit;

[assembly: TestTracking]
[assembly: TrackAssertions]

namespace Shapes;

public class ShapeTests
{
    private readonly Client _client = new();

    // P2: not the first await; the closure's scope opens after an earlier await (a loop body).
    [Fact]
    public async Task P2_loop_after_an_earlier_await()
    {
        await Task.Yield();
        foreach (var id in new[] { "a", "b" })
        {
            await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(id));
        }
    }

    // P2b: not the first await; a block after an earlier await.
    [Fact]
    public async Task P2b_block_after_an_earlier_await()
    {
        await Task.Yield();
        if (DateTime.Now.Year > 2000)
        {
            var id = "x";
            await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(id));
        }
    }

    // P3: the first await, but the captured variable is read after it, so the compiler hoists the closure.
    [Fact]
    public async Task P3_first_await_variable_used_after()
    {
        var id = "x";
        await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(id));
        id.ShouldBe("x");
    }

    // P4: the first await, captures a local, but the awaited task is already complete (no suspension).
    [Fact]
    public async Task P4_first_await_completes_synchronously()
    {
        var id = "x";
        await Should.ThrowAsync<InvalidOperationException>(() => _client.FailNowAsync(id));
    }

    // P5: FluentAssertions, the delegate in a plain local: no closure read, the local itself.
    [Fact]
    public async Task P5_fa_plain_local_delegate()
    {
        var id = "x";
        Func<Task> act = () => _client.CallAsync(id);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // P6: FluentAssertions, the lambda inline, capturing a local.
    [Fact]
    public async Task P6_fa_inline_lambda_capturing_a_local()
    {
        var id = "x";
        await FluentActions.Awaiting(() => _client.CallAsync(id)).Should().ThrowAsync<InvalidOperationException>();
    }

    // P7: Shouldly on a task held in a plain local.
    [Fact]
    public async Task P7_shouldly_task_in_a_plain_local()
    {
        var task = _client.CallAsync("x");
        await task.ShouldThrowAsync<InvalidOperationException>();
    }

    // P8: a value-type local named by a FAILING awaited assertion: what value does the report carry?
    [Fact]
    public async Task P8_failing_awaited_assertion_names_a_value_type_local()
    {
        var count = 42;
        var task = _client.CountAsync(count);
        await task.ShouldThrowAsync<ArgumentException>();
    }
}

public sealed class Client
{
    public async Task<string> CallAsync(string id)
    {
        await Task.Yield();
        throw new InvalidOperationException("expected");
    }

    public Task<string> FailNowAsync(string id) => Task.FromException<string>(new InvalidOperationException("expected"));

    public async Task<int> CountAsync(int count)
    {
        await Task.Yield();
        throw new InvalidOperationException("count " + count);
    }
}
