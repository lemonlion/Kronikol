// S41: TUnit: await Assert.That(async () => await Echo(pair.Actual)).IsEqualTo(pair.Expected); reading the delegate's closure after a suspension (#211)
// Builds: Debug, Release, ReleaseDebugPlus
// References: TUnit.Assertions
// AssertionPasses: ✓ Echo(pair.Actual) is equal to 'order'
// AssertionFails: ✗ Echo(pair.Actual) is equal to 'other'
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

[assembly: TrackAssertions]

public class Probe
{
    public Task AssertionPasses() => Run("order", "order");

    public Task AssertionFails() => Run("order", "other");

    private async Task Run(string actual, string expected)
    {
        var pair = new Pair { Actual = actual, Expected = expected };
        await Assert.That(async () => await Echo(pair.Actual)).IsEqualTo(pair.Expected);
    }

    private static async Task<string> Echo(string text)
    {
        await Task.Yield();
        return text;
    }
}

public sealed class Pair
{
    public string Actual { get; init; } = "";

    public string Expected { get; init; } = "";
}
