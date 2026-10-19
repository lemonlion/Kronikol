using Kronikol;
using Kronikol.LightBDD.xUnit3;
using LightBDD.Core.Configuration;
using LightBDD.XUnit3;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(Probe.ConfiguredLightBddScope))]

namespace Probe;

// Configuration A: Kronikol's documented setup, verbatim, with nothing about attachments.
// LightBddScope.Configure() starts from WithFrameworkDefaults(), which already calls
// RegisterDefaultFileAttachmentManager(): a FileAttachmentsManager at "~\Reports" (AppContext.BaseDirectory\Reports),
// LightBDD's documented default. Kronikol's ReportsFolderPath is left at its default "Reports"
// (AppDomain.CurrentDomain.BaseDirectory\Reports), so the two folders coincide.
public class ConfiguredLightBddScope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "LightBDD attachments probe A"
        });
    }
}
