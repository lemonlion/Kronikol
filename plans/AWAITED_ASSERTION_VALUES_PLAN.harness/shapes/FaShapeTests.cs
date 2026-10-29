using FluentAssertions;
using Xunit;

namespace Shapes;

public class FaShapeTests
{
    private readonly Client _client = new();

    // P9: FluentAssertions, an argument after Should() from a PLAIN local, the assertion failing on purpose:
    // what value does the failure's label carry for 'message'?
    [Fact]
    public async Task P9_fa_with_message_plain_local_fails()
    {
        var message = "not the message";
        Func<Task> act = () => _client.CallAsync("x");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
    }

    // P10: FluentAssertions, an argument after Should() read through the closure (id is captured by act).
    [Fact]
    public async Task P10_fa_with_message_closure_field()
    {
        var id = "x";
        Func<Task> act = () => _client.CallAsync(id);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(id == "x" ? "expected" : "other");
    }
}
