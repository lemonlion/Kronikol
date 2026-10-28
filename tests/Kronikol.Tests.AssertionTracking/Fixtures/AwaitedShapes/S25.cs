// S25: an if's branch, whose statement ends with the jump past the else (F6's kind): if (flag) a.ShouldBe(expected); else a.ShouldBe(0);
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ A should be '1'
// AssertionFails: ✗ A should be '2'
#nullable enable
using System;
using System.Threading.Tasks;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

public class Probe
{
    private readonly OrdersClient _client = new();

    public void AssertionPasses() => Run(true, 1);

    public void AssertionFails() => Run(true, 2);

    private void Run(bool flag, int expected)
    {
        var a = 1;
        if (flag)
            a.ShouldBe(expected);
        else
            a.ShouldBe(0);
    }
}
