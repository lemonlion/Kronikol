using Kronikol.xUnit3;
using Shouldly;
using Xunit;

namespace WeaveRepro;

// T3, a control: T1 as a synchronous method, where ldarg.0 is this.
public class SyncMethodControlTests(Harness first, Harness second, Harness third) : DiagrammedComponentTest, IClassFixture<Harness>
{
    private Exception? _exception;

    [Fact]
    public void Sync_method_control()
    {
        _exception = Should.Throw<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()).GetAwaiter().GetResult());

        _exception.ShouldNotBeNull();
        (first, second).ShouldNotBe((null, null));
    }
}
