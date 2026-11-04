using Kronikol.Tracking;
using Kronikol.xUnit3;
using Rev4;
using Xunit;

[assembly: TrackSteps]

namespace Rev4.Reentrant;

public class T : DiagrammedComponentTest
{
    [GivenStep] public void GivenStep() { }

    [Fact]
    public void Resolver_that_reads_the_phase()
    {
        var inner = Track.TestIdResolver!;
        Track.TestIdResolver = () =>
        {
            // A resolver that logs what it sees, or asks anything that reads the phase.
            _ = TestPhaseContext.Current;
            return inner();
        };
        try
        {
            GivenStep();
            Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
        }
        finally
        {
            Track.TestIdResolver = inner;
        }
    }
}
