using Shouldly;
using Xunit;

namespace WeaveRepro;

// V6, a probe: T6's class and body with one statement added after the ThrowAsync statement. T6 (b): T1 with six primary constructor parameters, the lambda reading the sixth.
public class SixParametersTestsValues(Harness first, Harness second, Harness third, Harness fourth, Harness fifth, Harness sixth)
    : IClassFixture<Harness>
{
    private Exception? _exception;

    // The probe: names the instance, where a type name alone prints as nothing in a note. A method, so no field moves.
    public override string ToString() => "the " + nameof(SixParametersTestsValues) + " instance";

    [Fact]
    public async Task Six_parameters_lambda_reads_the_sixth()
    {
        await Task.Yield();

        _exception = await Should.ThrowAsync<InvalidOperationException>(() => sixth.Run(() => throw new InvalidOperationException()));

        // The probe: sixth read inside a lambda the label prints, so the note shows the value the weave read for it.
        sixth.ShouldSatisfyAllConditions(h => GC.KeepAlive(sixth));

        _exception.ShouldNotBeNull();
        (first, second).ShouldNotBe((null, null));
    }

    // Not a test. Reads the middle three, so each is a field of the class and the sixth's field comes after them.
    private object Middle() => (third, fourth, fifth);
}
