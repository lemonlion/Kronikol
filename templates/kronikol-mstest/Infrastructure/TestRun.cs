using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kronikol;
using Kronikol.MSTest;

namespace KronikolComponentTests.Infrastructure;

[TestClass]
public class TestRun : DiagrammedTestRun
{
    [AssemblyInitialize]
    public static void AssemblySetup(TestContext context)
    {
        Setup();
    }

    [AssemblyCleanup]
    public static void AssemblyTeardown()
    {
        EndRunTime = DateTime.UtcNow;

        MSTestReportGenerator.CreateStandardReportsWithDiagrams(
            TestContexts,
            StartRunTime,
            EndRunTime,
            new ReportConfigurationOptions
            {
                SpecificationsTitle = "SERVICE_NAME Specifications",
                SeparateSetup = true,
            });
    }
}
