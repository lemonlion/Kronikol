using Xunit;
using Xunit.Abstractions;

namespace Probe;

public partial class CtorThrows
{
    private readonly ITestOutputHelper _output;

    public CtorThrows(ITestOutputHelper output)
    {
        _output = output;
        throw new InvalidOperationException("the test class constructor throws on purpose");
    }

    [Fact]
    public Task Never_runs() => ProbeHttp.GetAsync("/ctor/never", _output);
}
