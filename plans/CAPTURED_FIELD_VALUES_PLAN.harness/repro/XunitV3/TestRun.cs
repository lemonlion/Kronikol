using Kronikol;
using Kronikol.xUnit3;
using Xunit;

namespace WeaveRepro;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(
            TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions { SpecificationsTitle = "XunitV3Repro" });
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class DiagrammedTestCollection : ICollectionFixture<TestRun> { }
