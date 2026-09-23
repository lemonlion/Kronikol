using Example.Api.Tests.Component.LightBDD.xUnit3.Infrastructure;
using Example.Api.Tests.Component.Shared;
using Example.Api.Tests.Component.Shared.HttpFakes;
using LightBDD.Core.Configuration;
using LightBDD.Framework.Configuration;
using LightBDD.XUnit3;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Kronikol;
using Kronikol.LightBDD.xUnit3;
using Xunit.v3;
using CowServiceHttpFake = Example.Api.HttpFakes.CowService.Program;

[assembly: TestPipelineStartup(typeof(ConfiguredLightBddScope))]
namespace Example.Api.Tests.Component.LightBDD.xUnit3.Infrastructure;

public class ConfiguredLightBddScope : LightBddScope
{
    private static WebApplicationFactory<CowServiceHttpFake>? _cowServiceHttpFake;

    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        // When run by the integration test project, configuration is provided via environment variables.
        // Otherwise, the hardcoded values below serve as a readable example for users.
        var reportOptions = IntegrationTestConfiguration.IsIntegrationTestMode
            ? IntegrationTestConfiguration.GetReportConfigurationOptions()
            : new ReportConfigurationOptions
            {
                SpecificationsTitle = "Dessert Provider Specifications",
                SeparateSetup = true,
            };

        configuration.CreateStandardReportsWithDiagrams(reportOptions);

        // To stop the output repeating the step name for each step
        configuration.ProgressNotifierConfiguration().Clear();

        configuration.ExecutionExtensionsConfiguration()
                .RegisterGlobalTearDown("dispose factory", BaseFixture.DisposeFactory)
                .RegisterGlobalTearDown("project ndjson", ProjectNdjson)
                .RegisterGlobalSetUp("http fakes", StartHttpFakes, DisposeHttpFakes);
    }

    /// <summary>
    /// Test-only, inert unless <c>KRONIKOL_PROJECT_NDJSON</c> names a file: at run end, write every entry
    /// of the in-process store through the NDJSON writer, so the capture can be ingested and compared
    /// with this run's own report (plans/INGEST_FEED_PLAN.md S3). Not a library feature: a sink on the
    /// logger is roadmap 14.1's to design.
    /// </summary>
    private static void ProjectNdjson()
    {
        if (Environment.GetEnvironmentVariable("KRONIKOL_PROJECT_NDJSON") is not { Length: > 0 } path)
            return;
        using var writer = new Kronikol.Ingestion.NdjsonInteractionWriter(path);
        foreach (var log in Kronikol.Tracking.RequestResponseLogger.RequestAndResponseLogs)
            writer.Log(log);
    }

    private void StartHttpFakes()
    {
        DisposeHttpFakes();

        _cowServiceHttpFake = WebApplicationFactoryForSpecificUrl<CowServiceHttpFake>.Create(Settings.CowServiceBaseUrl!);
    }

    private void DisposeHttpFakes()
    {
        try
        {
            _cowServiceHttpFake?.Dispose();
        }
        catch { /* ignored */ }
    }

    private ComponentTestSettings Settings { get; } = new ConfigurationBuilder().GetComponentTestSettings();
}
