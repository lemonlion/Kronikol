using Xunit;
using Xunit.Abstractions;

namespace Probe;

/// <summary>Not IXunitSerializable: discovery cannot pre-enumerate the rows, so the theory runs as one test case.</summary>
public sealed class NonSer(int i)
{
    public int I { get; } = i;
}

public partial class MemberDataNotSerializable(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Items()
    {
        for (var i = 1; i <= 3; i++)
            yield return [new NonSer(i)];
    }

    [Theory]
    [MemberData(nameof(Items))]
    public Task NonSerializableRows(NonSer item) => ProbeHttp.GetAsync($"/nonser/{item.I}", output);
}
