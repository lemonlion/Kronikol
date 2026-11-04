using Kronikol.Tracking;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// The adapter's own base class, which is what gives a test its identity (Track.TestIdResolver): a suite without one records
// no step, and StartStep returns before it reads the parameters.
public class Order : Kronikol.xUnit3.DiagrammedComponentTest
{
    [Fact] public void First_passes() => Assert.True(true);

    [Fact] public void Second_calls_a_step_with_an_in_parameter() => new Driver().Takes_an_in_parameter(5);

    [Fact] public void Third_passes() => Assert.True(true);
}

public class Driver
{
    [Step] public void Takes_an_in_parameter(in int x) { }
}
