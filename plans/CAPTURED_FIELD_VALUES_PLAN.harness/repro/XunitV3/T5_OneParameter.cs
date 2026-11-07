using Kronikol.xUnit3;
using Shouldly;
using Xunit;

namespace WeaveRepro;

// T5 (a): T1 with one primary constructor parameter (the tuple assertion has no first or second to read).
public class OneParameterTests(Harness third) : DiagrammedComponentTest, IClassFixture<Harness>
{
    private Exception? _exception;

    [Fact]
    public async Task One_parameter()
    {
        await Task.Yield();

        _exception = await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));

        _exception.ShouldNotBeNull();
    }
}
