using Xunit;
using Xunit.Abstractions;

namespace Probe;

public sealed class ThrowingFixture
{
    public ThrowingFixture() => throw new InvalidOperationException("the class fixture constructor throws on purpose");
}

public partial class FixtureThrows(ThrowingFixture fixture, ITestOutputHelper output) : IClassFixture<ThrowingFixture>
{
    [Fact]
    public Task Never_runs()
    {
        _ = fixture;
        return ProbeHttp.GetAsync("/fixture/never", output);
    }
}
