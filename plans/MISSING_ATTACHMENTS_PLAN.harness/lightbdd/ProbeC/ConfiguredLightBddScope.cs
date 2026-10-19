using Kronikol;
using Kronikol.LightBDD.xUnit3;
using LightBDD.Core.Configuration;
using LightBDD.Framework.Reporting;
using LightBDD.XUnit3;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(Probe.ConfiguredLightBddScope))]

namespace Probe;

// Control C: configuration B (same attachments folder) plus Track.Attachment(FilePath) in the step. Configuration B: a LightBDD attachments folder that is NOT
// Kronikol's reports folder: AppContext.BaseDirectory\LightBddAttachments, a sibling of Reports.
public class ConfiguredLightBddScope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "LightBDD attachments probe C (control)"
        });

        configuration.ReportWritersConfiguration()
            .UpdateFileAttachmentsManager(new FileAttachmentsManager("~/LightBddAttachments"));
    }
}
