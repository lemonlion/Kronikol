using Xunit;
using Xunit.Abstractions;

namespace Kronikol.Tests.xUnit2.Fixtures;

public partial class Facts(ITestOutputHelper output)
{
    [Fact]
    public Task Passes() => FixtureHttp.GetAsync("/facts/passes", output);

    [Fact]
    public async Task Fails()
    {
        await FixtureHttp.GetAsync("/facts/fails", output);
        Assert.Equal(1, 2);
    }

    [Fact(DisplayName = "A failing fact with a display name")]
    public async Task FailsWithDisplayName()
    {
        await FixtureHttp.GetAsync("/facts/displayname-fails", output);
        await Task.Delay(120);
        Assert.Equal(1, 2);
    }

    [Fact(DisplayName = "A passing fact with a display name")]
    public Task PassesWithDisplayName() => FixtureHttp.GetAsync("/facts/displayname-passes", output);

    [Fact(Skip = "skipped on purpose")]
    public Task Skipped() => FixtureHttp.GetAsync("/facts/skipped", output);
}

/// <summary>Six serializable rows, each its own test case. Each sleeps its own time and row 4 fails after its sleep,
/// so a row's verdict, duration and call say which row they belong to.</summary>
public partial class Theories(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 400)]
    [InlineData(3, 100)]
    [InlineData(4, 800)]
    [InlineData(5, 200)]
    [InlineData(6, 20)]
    public async Task Rows(int row, int ms)
    {
        await FixtureHttp.GetAsync($"/row/{row}/slept/{ms}", output);
        await Task.Delay(ms);
        Assert.NotEqual(4, row);
    }
}

/// <summary>Not serializable, so discovery cannot enumerate the rows: the theory is one test case, and its rows
/// share its UniqueID. Each row is still its own ITest.</summary>
public sealed class NonSer(int i)
{
    public int I { get; } = i;

    public override string ToString() => $"NonSer {I}";
}

public partial class NonSerializableRows(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Items()
    {
        for (var i = 1; i <= 3; i++)
            yield return [new NonSer(i)];
    }

    [Theory]
    [MemberData(nameof(Items))]
    public async Task Rows(NonSer item)
    {
        await FixtureHttp.GetAsync($"/nonser/{item.I}", output);
        await Task.Delay(30 * item.I);
    }
}

/// <summary>Sentence-like display names: the formatter once named the first "2" and threw on the second.</summary>
public partial class DisplayNameDots(ITestOutputHelper output)
{
    [Fact(DisplayName = "Order API returns 404 for v1.2")]
    public Task DottedVersion() => FixtureHttp.GetAsync("/dots/version-1-2", output);

    [Fact(DisplayName = "Does the thing.")]
    public Task TrailingDot() => FixtureHttp.GetAsync("/dots/trailing-dot", output);
}
