using Xunit;
using Xunit.Abstractions;

namespace Probe;

public partial class Theories(ITestOutputHelper output)
{
    // Six serializable rows: discovery enumerates each into its own test case. Each sleeps a different
    // time, and row 4 fails after its sleep, so a row's verdict, duration and call identify it.
    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 400)]
    [InlineData(3, 100)]
    [InlineData(4, 800)]
    [InlineData(5, 200)]
    [InlineData(6, 20)]
    public async Task Rows(int row, int ms)
    {
        await ProbeHttp.GetAsync($"/row/{row}/slept/{ms}", output);
        await Task.Delay(ms);
        Assert.NotEqual(4, row);
    }
}
