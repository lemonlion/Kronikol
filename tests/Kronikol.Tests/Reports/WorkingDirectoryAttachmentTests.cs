using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// #148 F20 (<c>plans/MISSING_ATTACHMENTS_PLAN.md</c> section 2.4): <c>Track.Attachment</c> kept a relative path as it was
/// given, and the copier read it against the binary folder at the end of the run, while the test wrote the file relative
/// to the working directory when it attached it. The two differ under NUnit's <c>WorkDirectory</c> (measured on VSTest and
/// on Microsoft.Testing.Platform) and after a test moves the working directory, and every such attachment was lost
/// without a word. A relative path is now read when it is attached: the working directory first, then the binary folder.
/// </summary>
[Collection(WorkingDirectoryCollection.Name)]
public class WorkingDirectoryAttachmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-working-directory-" + Guid.NewGuid().ToString("N"));
    private readonly string _testId = "working-directory-" + Guid.NewGuid().ToString("N");
    private readonly string _previous = Directory.GetCurrentDirectory();

    public WorkingDirectoryAttachmentTests() => Directory.CreateDirectory(Path.Combine(_dir, "work"));

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previous);
        StepCollector.ClearSteps(_testId);
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string Reports => Path.Combine(_dir, "Reports");

    private FileAttachment CopiedAttachment()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Working directory",
                Scenarios = [new Scenario { Id = _testId, DisplayName = "Attaches a file", Result = ExecutionResult.Passed, Attachments = StepCollector.GetScenarioAttachments(_testId) }],
            },
        ];
        ReportGenerator.CopyAttachmentsToReportsFolder(features, Reports);
        return features[0].Scenarios[0].Attachments!.Single();
    }

    [Fact]
    public void A_relative_path_attached_while_the_working_directory_is_elsewhere_is_copied()
    {
        // The wiki's own example: File.WriteAllText("response.json", …) and Track.Attachment("response.json"), with the
        // working directory away from the binary folder, as NUnit's WorkDirectory puts it.
        Directory.SetCurrentDirectory(Path.Combine(_dir, "work"));
        File.WriteAllText("response.json", "{\"ok\":true}");
        StepCollector.AddAttachment(_testId, "response.json", null);
        // The report is drawn at the end of the run, wherever the working directory is by then.
        Directory.SetCurrentDirectory(_previous);

        var attachment = CopiedAttachment();

        Assert.Equal("attachments/response.json", attachment.RelativePath);
        Assert.Equal("{\"ok\":true}", File.ReadAllText(Path.Combine(Reports, "attachments", "response.json")));
    }

    [Fact]
    public void A_relative_path_to_a_file_beside_the_binaries_is_copied_whatever_the_working_directory()
    {
        // A fixture shipped beside the binaries (TestData/x.json) is still found when the working directory is elsewhere.
        var name = "working-directory-" + Guid.NewGuid().ToString("N") + ".json";
        var besideTheBinaries = Path.Combine(AppContext.BaseDirectory, name);
        File.WriteAllText(besideTheBinaries, "{}");
        try
        {
            Directory.SetCurrentDirectory(Path.Combine(_dir, "work"));
            StepCollector.AddAttachment(_testId, name, null);
            Directory.SetCurrentDirectory(_previous);

            Assert.Equal("attachments/" + name, CopiedAttachment().RelativePath);
        }
        finally
        {
            File.Delete(besideTheBinaries);
        }
    }

    [Fact]
    public void A_relative_path_rewritten_after_it_was_attached_is_said_to_have_changed()
    {
        // Read when attached, so its length and last write are noted then, as an absolute path's are.
        Directory.SetCurrentDirectory(Path.Combine(_dir, "work"));
        File.WriteAllText("log.json", "{}");
        StepCollector.AddAttachment(_testId, "log.json", null);
        File.WriteAllText("log.json", "{\"lines\":2}");
        Directory.SetCurrentDirectory(_previous);

        var collector = new ReportDiagnosticsCollector();
        FileAttachment attachment;
        using (ReportDiagnosticsScope.Begin(collector))
            attachment = CopiedAttachment();

        Assert.Equal("attachments/log.json", attachment.RelativePath);
        Assert.StartsWith("1 attachment(s) changed after it was attached, and the report holds the version found when it was drawn: ",
            Assert.Single(collector.Entries, e => e.Kind == DiagnosticKind.AttachmentFailure).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_path_found_in_neither_folder_names_both_in_the_entry()
    {
        Directory.SetCurrentDirectory(Path.Combine(_dir, "work"));
        StepCollector.AddAttachment(_testId, "never-written.json", null);
        Directory.SetCurrentDirectory(_previous);

        var collector = new ReportDiagnosticsCollector();
        using (ReportDiagnosticsScope.Begin(collector))
            CopiedAttachment();

        var message = Assert.Single(collector.Entries, e => e.Kind == DiagnosticKind.AttachmentFailure).Message;
        Assert.StartsWith("1 attachment(s) are not in the report: 1 not found when the report was drawn: ", message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_dir, "work", "never-written.json"), message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(AppContext.BaseDirectory, "never-written.json"), message, StringComparison.Ordinal);
    }
}
