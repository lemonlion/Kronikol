using Kronikol;
using Kronikol.xUnit2;

// The reporting test framework generates the Kronikol reports automatically after all tests complete.
[assembly: Xunit.TestFramework("Kronikol.xUnit2.ReportingTestFramework", "Kronikol.xUnit2")]

namespace KronikolComponentTests.Infrastructure;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public TestRun()
    {
        ReportLifecycle.Options = new ReportConfigurationOptions
        {
            SpecificationsTitle = "SERVICE_NAME Specifications",
            SeparateSetup = true,
        };
    }

    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
    }
}
