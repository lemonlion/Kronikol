using LightBDD.Core.Configuration;
using LightBDD.Framework.Configuration;
using LightBDD.XUnit2;
using Kronikol;
using Kronikol.LightBDD.xUnit2;
using KronikolComponentTests.Infrastructure;

[assembly: ConfiguredLightBddScope]

namespace KronikolComponentTests.Infrastructure;

internal class ConfiguredLightBddScopeAttribute : LightBddScopeAttribute
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
