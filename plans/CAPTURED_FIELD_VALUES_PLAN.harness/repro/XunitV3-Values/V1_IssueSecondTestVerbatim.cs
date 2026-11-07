using Kronikol.xUnit3;
using Shouldly;
using Xunit;

namespace WeaveRepro;

// V1, a probe: T1's class and body with one statement added after the ThrowAsync statement. T1: the issue's second test as filed; the method's name is the harness's, and the base class is xUnit v3's tracking.
public class PrimaryConstructorParameterTestsValues(Harness first, Harness second, Harness third) : DiagrammedComponentTest, IClassFixture<Harness>
{
    private Exception? _exception;

    // The probe: names the instance, where a type name alone prints as nothing in a note. A method, so no field moves.
    public override string ToString() => "the " + nameof(PrimaryConstructorParameterTestsValues) + " instance";

    [Fact]
    public async Task Issue_second_test_verbatim()
    {
        await Task.Yield();

        _exception = await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));

        // The probe: third read inside a lambda the label prints, so the note shows the value the weave read for it.
        third.ShouldSatisfyAllConditions(h => GC.KeepAlive(third));

        _exception.ShouldNotBeNull();
        (first, second).ShouldNotBe((null, null));
    }
}
