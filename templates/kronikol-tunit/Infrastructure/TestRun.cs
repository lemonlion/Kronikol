using Kronikol;
using Kronikol.TUnit;
using TUnit.Core;

namespace KronikolComponentTests.Infrastructure;

public class TestRun : DiagrammedTestRun
{
    [Before(Assembly)]
    public static void GlobalSetup(AssemblyHookContext context)
    {
        Setup();
    }

    [After(Assembly)]
    public static void GlobalTeardown(AssemblyHookContext context)
    {
        EndRunTime = DateTime.UtcNow;

        TUnitReportGenerator.CreateStandardReportsWithDiagrams(
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
