using Shouldly;
using Xunit;

namespace Shapes;

// P11: a synchronous test whose lambda captures one local and nothing else. An optimized build keeps the closure on
// the stack only (newobj; dup; stfld; ldftn), never in a local.
public class SyncShapeTests
{
    private readonly object _first = new Marker();

    [Fact]
    public void P11_sync_closure_kept_on_the_stack()
    {
        var id = "a1";
        Should.Throw<InvalidOperationException>(() => Fail(id));
    }

    private static void Fail(string id) => throw new InvalidOperationException(id);
}

public sealed class Marker
{
    public override string ToString() => "the test class's first field";
}
