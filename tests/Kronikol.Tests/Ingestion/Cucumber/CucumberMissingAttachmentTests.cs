using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.Tests.Ingestion.Cucumber.CucumberBypassTests;

namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// #148 for Cucumber messages (<c>plans/MISSING_ATTACHMENTS_PLAN.md</c> F4 and F17): an attachment's <c>url</c> that is not
/// an absolute <c>file:</c> URI was passed on as it was and read against the tool's own folder, while the join's hashing
/// read it against the working directory; and a body that could not be written was dropped with an entry of kind
/// <see cref="DiagnosticKind.Other"/>. A relative <c>url</c> is now read against the attachments base, the rule a tests
/// file's relative path follows, and a body that could not be written is in the run's one attachment entry.
/// </summary>
[Collection("DiagramsFetcher")]
public class CucumberMissingAttachmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cucumber-missing-" + Guid.NewGuid().ToString("N"));

    public CucumberMissingAttachmentTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string PathOf(params string[] parts) => Path.Combine([_dir, .. parts]);

    private string Reports => PathOf("Reports");

    private IngestResult Ingest(Attachment attachment, string? attachmentsBase = null, string? decodedRoot = null)
    {
        var messages = PathOf("messages.ndjson");
        File.WriteAllText(messages, Stream(new Step("the receipt shows", "PASSED", Attachments: [attachment])));
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Reports;
        options.HistoryFilePath = PathOf("history.ndjson");
        options.WriteRunSummaryToConsole = false;
        return IngestPipeline.Run(new IngestRequest
        {
            CucumberMessagesFiles = [messages],
            AttachmentsBase = attachmentsBase,
            DecodedAttachmentsRoot = decodedRoot ?? PathOf("decoded"),
            Options = options,
        });
    }

    private static FileAttachment OnlyAttachment(IngestResult result) =>
        Assert.Single(result.Features.SelectMany(f => f.Scenarios).SelectMany(s =>
            (s.Attachments ?? []).Concat((s.Steps ?? []).SelectMany(step => step.Attachments ?? []))));

    private string Written(string relative)
    {
        var path = PathOf(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    [Fact]
    public void A_relative_url_is_read_against_the_attachments_base()
    {
        Written("base/shot.png");

        var result = Ingest(new Attachment("shot.png", "", MediaType: "image/png", Url: "shot.png"), attachmentsBase: PathOf("base"));

        Assert.True(File.Exists(Path.Combine(Reports, "attachments", "shot.png")));
        Assert.Equal("attachments/shot.png", OnlyAttachment(result).RelativePath);
        Assert.DoesNotContain(result.Diagnostics, e => e.Kind == DiagnosticKind.AttachmentFailure);
    }

    [Fact]
    public void A_relative_url_under_attachments_is_read_against_the_base_too_not_taken_for_this_runs_copy()
    {
        Written("base/attachments/shot.png");

        var result = Ingest(new Attachment("shot.png", "", MediaType: "image/png", Url: "attachments/shot.png"), attachmentsBase: PathOf("base"));

        Assert.True(File.Exists(Path.Combine(Reports, "attachments", "shot.png")));
        Assert.Equal("attachments/shot.png", OnlyAttachment(result).RelativePath);
    }

    [Fact]
    public void A_body_that_is_not_base64_is_in_the_attachment_entry_not_an_other()
    {
        var result = Ingest(new Attachment("shot.png", "this is not base64!", Encoding: "BASE64", MediaType: "image/png"));

        Assert.StartsWith("1 attachment(s) are not in the report: 1 could not be written from the Cucumber messages (FormatException: ",
            Assert.Single(result.Diagnostics, e => e.Kind == DiagnosticKind.AttachmentFailure).Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, e => e.Kind == DiagnosticKind.Other && e.Message.Contains("could not be written", StringComparison.Ordinal));
    }

    [Fact]
    public void A_body_that_cannot_be_written_is_in_the_attachment_entry_not_an_other()
    {
        // The folder the bodies are decoded into cannot be made: a file holds its parent's name.
        var blocked = PathOf("blocked");
        File.WriteAllText(blocked, "a file, not a folder");

        var result = Ingest(new Attachment("shot.png", "iVBORw0KGgo=", Encoding: "BASE64", MediaType: "image/png"), decodedRoot: blocked);

        var message = Assert.Single(result.Diagnostics, e => e.Kind == DiagnosticKind.AttachmentFailure).Message;
        Assert.StartsWith("1 attachment(s) are not in the report: 1 could not be written from the Cucumber messages (", message, StringComparison.Ordinal);
        Assert.Contains("shot.png", message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, e => e.Kind == DiagnosticKind.Other && e.Message.Contains("could not be written", StringComparison.Ordinal));
    }
}
