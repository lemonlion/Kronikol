using LightBDD.Core.Configuration;
using LightBDD.Framework.Configuration;
using LightBDD.XUnit3;
using Kronikol;
using Kronikol.LightBDD.xUnit3;
using KronikolComponentTests.Infrastructure;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(ConfiguredLightBddScope))]

namespace KronikolComponentTests.Infrastructure;

public class ConfiguredLightBddScope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "SERVICE_NAME Specifications",
            SeparateSetup = true,
        });

        configuration.ProgressNotifierConfiguration().Clear();

        configuration.ExecutionExtensionsConfiguration()
            .RegisterGlobalTearDown("dispose factory", BaseFixture.DisposeFactory);
    }
}
