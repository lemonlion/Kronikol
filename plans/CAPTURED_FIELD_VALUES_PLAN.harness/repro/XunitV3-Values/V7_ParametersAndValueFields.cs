using Kronikol.xUnit3;
using Shouldly;
using Xunit;

namespace WeaveRepro;

// V7, a probe: T7's class and body with one statement added after the ThrowAsync statement. T7 (c): T1 with a long and an int field beside the three primary constructor parameters.
public class ParametersAndValueFieldsTestsValues(Harness first, Harness second, Harness third) : DiagrammedComponentTest, IClassFixture<Harness>
{
    private long _count = 7;
    private int _flag = 1;
    private Exception? _exception;

    // The probe: names the instance, where a type name alone prints as nothing in a note. A method, so no field moves.
    public override string ToString() => "the " + nameof(ParametersAndValueFieldsTestsValues) + " instance";

    [Fact]
    public async Task Parameters_and_value_fields()
    {
        await Task.Yield();

        _exception = await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));

        // The probe: third read inside a lambda the label prints, so the note shows the value the weave read for it.
        third.ShouldSatisfyAllConditions(h => GC.KeepAlive(third));

        _exception.ShouldNotBeNull();
        (first, second).ShouldNotBe((null, null));
    }
}
