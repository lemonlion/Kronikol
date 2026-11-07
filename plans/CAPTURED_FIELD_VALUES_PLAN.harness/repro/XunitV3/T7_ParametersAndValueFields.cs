using Kronikol.xUnit3;
using Shouldly;
using Xunit;

namespace WeaveRepro;

// T7 (c): T1 with a long and an int field beside the three primary constructor parameters.
public class ParametersAndValueFieldsTests(Harness first, Harness second, Harness third) : DiagrammedComponentTest, IClassFixture<Harness>
{
    private long _count = 7;
    private int _flag = 1;
    private Exception? _exception;

    [Fact]
    public async Task Parameters_and_value_fields()
    {
        await Task.Yield();

        _exception = await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));

        _exception.ShouldNotBeNull();
        (first, second).ShouldNotBe((null, null));
    }
}
