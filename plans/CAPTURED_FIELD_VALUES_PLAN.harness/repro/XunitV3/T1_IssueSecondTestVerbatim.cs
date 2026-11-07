using Kronikol.xUnit3;
using Shouldly;
using Xunit;

namespace WeaveRepro;

// T1: the issue's second test as filed; the method's name is the harness's, and the base class is xUnit v3's tracking.
public class PrimaryConstructorParameterTests(Harness first, Harness second, Harness third) : DiagrammedComponentTest, IClassFixture<Harness>
{
    private Exception? _exception;

    [Fact]
    public async Task Issue_second_test_verbatim()
    {
        await Task.Yield();

        _exception = await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));

        _exception.ShouldNotBeNull();
        (first, second).ShouldNotBe((null, null));
    }
}
