// S52: a static helper whose expression tree reads the class's field through a parameter: names.Should().Contain(n => n == probe._name); (#211: a static method has no this, and the weave read the field off its first parameter, an int; the label still reads _name through the closure's probe)
// Builds: Debug, Release, ReleaseDebugPlus
// AssertionPasses: ✓ Names should contain [ n => n == 'order' ]
// AssertionFails: ✗ Names should contain [ n => n == 'order' ]
#nullable enable
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Kronikol.Tracking;

[assembly: TrackAssertions]

public class Probe
{
    private readonly string _name = "order";

    public void AssertionPasses() => Run(1, this, new[] { "order", "other" });

    public void AssertionFails() => Run(1, this, new[] { "other" });

    private static void Run(int attempt, Probe probe, string[] names)
    {
        names.Should().Contain(n => n == probe._name);
    }
}
