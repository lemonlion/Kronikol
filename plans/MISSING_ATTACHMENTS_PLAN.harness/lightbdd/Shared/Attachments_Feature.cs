using LightBDD.Core.Results;
using LightBDD.Framework;
using LightBDD.Framework.Reporting;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;

namespace Probe;

[FeatureDescription("Probe: where LightBDD file attachments end up in Kronikol's report")]
public class Attachments_Feature : FeatureFixture
{
    // A valid 1x1 PNG.
    private static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Scenario]
    public async Task A_step_attaches_a_text_file_and_a_png_then_fails()
    {
        await Runner.RunScenarioAsync(
            _ => A_text_file_and_a_png_are_attached_and_the_step_fails());
    }

    private async Task A_text_file_and_a_png_are_attached_and_the_step_fails()
    {
        // Text file: written to disk first, then handed to LightBDD with CreateFromFile (moves it by default).
        var sourceDir = Path.Combine(AppContext.BaseDirectory, "probe-source");
        Directory.CreateDirectory(sourceDir);
        var textSource = Path.Combine(sourceDir, "probe-notes.txt");
        await File.WriteAllTextAsync(textSource, "Probe text attachment\n");

        FileAttachment? text = null;
        await StepExecution.Current.AttachFile(async m => text = await m.CreateFromFile("probe-notes", textSource));

        // PNG: in-memory bytes handed to LightBDD with CreateFromData.
        FileAttachment? png = null;
        await StepExecution.Current.AttachFile(async m => png = await m.CreateFromData("probe-screenshot", "png", Png1x1));

        // Record what LightBDD itself says about each attachment, outside the report.
        var log = Path.Combine(AppContext.BaseDirectory, "probe-attachments.log");
        var stamp = DateTime.UtcNow.ToString("O");
        await File.AppendAllLinesAsync(log,
        [
            $"{stamp}\tpid={Environment.ProcessId}\tcwd={Directory.GetCurrentDirectory()}\tAppContext.BaseDirectory={AppContext.BaseDirectory}\tAppDomain.BaseDirectory={AppDomain.CurrentDomain.BaseDirectory}",
            $"{stamp}\tname={text!.Name}\tFilePath={text.FilePath}\tRelativePath={text.RelativePath}\texists={File.Exists(text.FilePath)}\tsourceStillThere={File.Exists(textSource)}",
            $"{stamp}\tname={png!.Name}\tFilePath={png.FilePath}\tRelativePath={png.RelativePath}\texists={File.Exists(png.FilePath)}",
        ]);

#if PROBE_CONTROL
        // Control C only: hand Kronikol the ABSOLUTE path LightBDD reports (FilePath) through Kronikol's own API.
        Kronikol.Tracking.Track.Attachment(text.FilePath, "probe-notes (Track.Attachment, LightBDD FilePath)");
        Kronikol.Tracking.Track.Attachment(png.FilePath, "probe-screenshot (Track.Attachment, LightBDD FilePath)");
#endif

        throw new InvalidOperationException("Probe failure: this step attached a text file and a PNG, then failed");
    }
}
