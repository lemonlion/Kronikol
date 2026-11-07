using Shouldly;
using Xunit;

namespace WeaveRepro;

// T6 (b): T1 with six primary constructor parameters, the lambda reading the sixth.
public class SixParametersTests(Harness first, Harness second, Harness third, Harness fourth, Harness fifth, Harness sixth)
    : IClassFixture<Harness>
{
    private Exception? _exception;

    [Fact]
    public async Task Six_parameters_lambda_reads_the_sixth()
    {
        await Task.Yield();

        _exception = await Should.ThrowAsync<InvalidOperationException>(() => sixth.Run(() => throw new InvalidOperationException()));

        _exception.ShouldNotBeNull();
        (first, second).ShouldNotBe((null, null));
    }

    // Not a test. Reads the middle three, so each is a field of the class and the sixth's field comes after them.
    private object Middle() => (third, fourth, fifth);
}
