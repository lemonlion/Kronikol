// S19b: TUnit, a helper returning the un-awaited assertion: StringEqualsAssertion IsOrder(name) => Assert.That(name).IsEqualTo("order");
// Builds: Debug, Release, ReleaseDebugPlus
// References: TUnit.Assertions
// Unwoven: IsOrder: a TUnit assertion the statement does not await runs only when it is awaited
// AssertionPasses: (none)
// AssertionFails: (none)
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Assertions.Conditions;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public async Task AssertionPasses()
    {
        var name = await _client.GetNameAsync();
        await IsOrder(name);
    }

    public async Task AssertionFails()
    {
        await Task.Yield();
        await IsOrder("other");
    }

    private static StringEqualsAssertion IsOrder(string name) => Assert.That(name).IsEqualTo("order");
}
