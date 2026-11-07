using Kronikol.xUnit3;
using Shouldly;
using Xunit;

namespace WeaveRepro;

// V5, a probe: T5's class and body with one statement added after the ThrowAsync statement. T5 (a): T1 with one primary constructor parameter (the tuple assertion has no first or second to read).
public class OneParameterTestsValues(Harness third) : DiagrammedComponentTest, IClassFixture<Harness>
{
    private Exception? _exception;

    // The probe: names the instance, where a type name alone prints as nothing in a note. A method, so no field moves.
    public override string ToString() => "the " + nameof(OneParameterTestsValues) + " instance";

    [Fact]
    public async Task One_parameter()
    {
        await Task.Yield();

        _exception = await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));

        // The probe: third read inside a lambda the label prints, so the note shows the value the weave read for it.
        third.ShouldSatisfyAllConditions(h => GC.KeepAlive(third));

        _exception.ShouldNotBeNull();
    }
}
