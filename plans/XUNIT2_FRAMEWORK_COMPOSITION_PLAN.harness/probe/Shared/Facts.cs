using Xunit;
using Xunit.Abstractions;

namespace Probe;

public partial class Facts(ITestOutputHelper output)
{
    [Fact]
    public Task Passes() => ProbeHttp.GetAsync("/facts/passes", output);

    [Fact]
    public async Task Fails()
    {
        await ProbeHttp.GetAsync("/facts/fails", output);
        Assert.Equal(1, 2);
    }

    [Fact(DisplayName = "A failing fact with a display name")]
    public async Task FailsWithDisplayName()
    {
        await ProbeHttp.GetAsync("/facts/displayname-fails", output);
        Assert.Equal(1, 2);
    }

    [Fact(DisplayName = "A passing fact with a display name")]
    public Task PassesWithDisplayName() => ProbeHttp.GetAsync("/facts/displayname-passes", output);

    [Fact(Skip = "skipped on purpose")]
    public Task Skipped() => ProbeHttp.GetAsync("/facts/skipped", output);
}
