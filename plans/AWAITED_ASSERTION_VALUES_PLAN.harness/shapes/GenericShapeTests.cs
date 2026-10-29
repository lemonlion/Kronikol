using Shouldly;
using Xunit;

namespace Shapes;

// P13: an awaited assertion in a generic async method of the test class, whose lambda captures this and a local.
public class GenericShapeTests
{
    private readonly Client _client = new();

    [Fact]
    public async Task P13_generic_async_method() => await Generic("a1");

    private async Task Generic<T>(T id)
    {
        var copy = id;
        await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(copy!.ToString()!));
    }
}
