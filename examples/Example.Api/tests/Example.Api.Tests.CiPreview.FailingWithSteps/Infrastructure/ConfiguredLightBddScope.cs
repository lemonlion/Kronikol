using Example.Api.Tests.CiPreview.FailingWithSteps.Infrastructure;
using Example.Api.Tests.Component.Shared;
using Example.Api.Tests.Component.Shared.HttpFakes;
using Kronikol;
using Kronikol.LightBDD.xUnit3;
using LightBDD.Core.Configuration;
using LightBDD.Framework.Configuration;
using LightBDD.XUnit3;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit.v3;
using CowServiceHttpFake = Example.Api.HttpFakes.CowService.Program;

[assembly: TestPipelineStartup(typeof(ConfiguredLightBddScope))]

namespace Example.Api.Tests.CiPreview.FailingWithSteps.Infrastructure;

public class ConfiguredLightBddScope : LightBddScope
{
    private static WebApplicationFactory<CowServiceHttpFake>? _cowServiceHttpFake;

    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "CI Preview — Failing Inside a Step",
            WriteCiSummary = true,
            // The other half of this fixture's job: nothing in the repository had ever written a
            // mergeable report, so every `kronikol merge` assertion rested on hand-built shards.
            GenerateMergeableData = true
        });

        configuration.ProgressNotifierConfiguration().Clear();

        configuration.ExecutionExtensionsConfiguration()
            .RegisterGlobalTearDown("dispose factory", BaseFixture.DisposeFactory)
            .RegisterGlobalSetUp("http fakes", StartHttpFakes, DisposeHttpFakes);
    }

    private void StartHttpFakes()
    {
        DisposeHttpFakes();
        _cowServiceHttpFake = WebApplicationFactoryForSpecificUrl<CowServiceHttpFake>.Create(Settings.CowServiceBaseUrl!);
    }

    private void DisposeHttpFakes()
    {
        try { _cowServiceHttpFake?.Dispose(); }
        catch { /* ignored */ }
    }

    private ComponentTestSettings Settings { get; } = new ConfigurationBuilder().GetComponentTestSettings();
}
