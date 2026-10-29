using Shouldly;
using Xunit;

namespace Shapes;

// P12: an async test that never uses this, with a lambda that captures one local.
public class NoThisShapeTests
{
    [Fact]
    public async Task P12_async_closure_without_this()
    {
        var id = "a1";
        await Should.ThrowAsync<InvalidOperationException>(() => Task.FromException(new InvalidOperationException(id)));
    }
}
