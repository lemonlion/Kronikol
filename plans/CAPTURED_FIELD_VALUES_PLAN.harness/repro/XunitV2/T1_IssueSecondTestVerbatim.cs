using Shouldly;
using Xunit;

namespace WeaveRepro;

// T1: the issue's second test as filed; only the method's name is the harness's.
public class PrimaryConstructorParameterTests(Harness first, Harness second, Harness third) : IClassFixture<Harness>
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
