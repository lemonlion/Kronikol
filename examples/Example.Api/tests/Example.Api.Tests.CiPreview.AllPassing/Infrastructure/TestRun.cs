using Example.Api.Tests.Component.Shared;
using Example.Api.Tests.Component.Shared.HttpFakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Kronikol;
using Kronikol.xUnit3;
using CowServiceHttpFake = Example.Api.HttpFakes.CowService.Program;

namespace Example.Api.Tests.CiPreview.AllPassing.Infrastructure;

public class TestRun : DiagrammedTestRun, IDisposable
{
    private static WebApplicationFactory<CowServiceHttpFake>? _cowServiceHttpFake;

    public TestRun() => StartHttpFakes();

    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        DisposeHttpFakes();

        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime,
            new ReportConfigurationOptions
            {
                SpecificationsTitle = "CI Preview — All Passing",
                WriteCiSummary = true,
                // The second real shard. Together with CiPreview.FailingWithSteps this is the first
                // sharded run the repository has ever produced: every previous assertion about
                // `kronikol merge` was made against hand-built JSON, because no project turned this on.
                GenerateMergeableData = true,
                // The reports the PR report link lane uploads (.github/workflows/pr-report-link.yml): the step's
                // reports-path and reports-retention-days outputs. Elsewhere on GitHub the two lines go unread.
                PublishCiArtifacts = true
            });
    }

    private void StartHttpFakes()
    {
        DisposeHttpFakes();
        _cowServiceHttpFake = WebApplicationFactoryForSpecificUrl<CowServiceHttpFake>.Create(
            new ConfigurationBuilder().GetComponentTestConfiguration().Get<ComponentTestSettings>()!.CowServiceBaseUrl!);
    }

    private void DisposeHttpFakes()
    {
        try { _cowServiceHttpFake?.Dispose(); } catch { }
    }
}
