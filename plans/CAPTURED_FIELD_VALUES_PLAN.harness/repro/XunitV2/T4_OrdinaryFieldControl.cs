using Shouldly;
using Xunit;

namespace WeaveRepro;

// T4, a control: T1 with an ordinary field in place of the primary constructor's third parameter.
public class OrdinaryFieldControlTests(Harness first, Harness second) : IClassFixture<Harness>
{
    private readonly Harness _third = new();
    private Exception? _exception;

    [Fact]
    public async Task Ordinary_field_control()
    {
        await Task.Yield();

        _exception = await Should.ThrowAsync<InvalidOperationException>(() => _third.Run(() => throw new InvalidOperationException()));

        _exception.ShouldNotBeNull();
        (first, second).ShouldNotBe((null, null));
    }
}
