using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tests.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// #148 through <see cref="IngestPipeline"/> and <c>kronikol ingest</c> (<c>plans/MISSING_ATTACHMENTS_PLAN.md</c>, the
/// harness's <c>ingest/</c> cases): a tests file's attachment whose file is not there was linked where the record said
/// and nothing said so, a path with a NUL character ended the ingest with no report, and <c>--clean-attachments</c>
/// deleted the very files a capture pointed at before they were read.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestMissingAttachmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-ingest-attachments-" + Guid.NewGuid().ToString("N"));

    public IngestMissingAttachmentTests()
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

    private string Reports => PathOf("out");

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    /// <summary>A tests file of one failed test holding <paramref name="attachmentLines"/> (each a JSON object's members).</summary>
    private string TestsFile(params string[] attachmentLines)
    {
        var lines = new List<string> { """{"event":"start","testId":"t1","testName":"checkout shows a receipt","feature":"checkout.spec.ts","timestamp":"2026-10-19T10:00:00Z"}""" };
        lines.AddRange(attachmentLines.Select(members => $$"""{"event":"attachment","testId":"t1",{{members}}}"""));
        lines.Add("""{"event":"end","testId":"t1","status":"failed","durationMs":9000,"error":"expected 1 got 2","timestamp":"2026-10-19T10:00:09Z"}""");
        var path = PathOf("tests.ndjson");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string Attachment(string name, string path) => $"\"name\":{Json(name)},\"path\":{Json(path)},\"mediaType\":\"image/png\"";

    private IngestResult Ingest(string testsFile, string? attachmentsBase = null, bool clean = false, bool allowEmpty = false)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Reports;
        options.HistoryFilePath = PathOf("history.ndjson");
        options.WriteRunSummaryToConsole = false;
        return IngestPipeline.Run(new IngestRequest
        {
            TestsFile = testsFile,
            AttachmentsBase = attachmentsBase,
            CleanAttachments = clean,
            AllowEmpty = allowEmpty,
            DecodedAttachmentsRoot = PathOf("decoded"),
            Options = options,
        });
    }

    private static string TheEntry(IngestResult result) =>
        Assert.Single(result.Diagnostics, e => e.Kind == DiagnosticKind.AttachmentFailure).Message;

    private string Written(string relative)
    {
        var path = PathOf(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    [Fact]
    public void Missing_capture_paths_are_one_entry_that_names_the_base_relative_paths_were_read_against()
    {
        // The harness's case1, the issue's own: the reporter's relative paths read against a base that lacks
        // test-results/, beside a file that is there, an absolute path to nowhere, an attachments/ path and a blank one.
        var attachmentsBase = PathOf("base");
        Written("base/present.png");
        var result = Ingest(TestsFile(
            Attachment("present.png", Path.Combine(attachmentsBase, "present.png")),
            Attachment("missing-absolute.png", PathOf("nowhere", "test-results", "a-retry1", "test-failed-1.png")),
            Attachment("missing-relative.png", "tests/test-results/a-retry1/test-failed-2.png"),
            Attachment("prefixed.png", "attachments/gone.png"),
            Attachment("blank", "")), attachmentsBase);

        var message = TheEntry(result);

        Assert.StartsWith("4 attachment(s) are not in the report: 3 not found when the report was drawn, 1 has a path that is not usable: ", message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(attachmentsBase, "tests", "test-results", "a-retry1", "test-failed-2.png"), message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(attachmentsBase, "attachments", "gone.png"), message, StringComparison.Ordinal);
        Assert.Contains("(blank path)", message, StringComparison.Ordinal);
        Assert.EndsWith("Relative paths were resolved against the attachments base, " + attachmentsBase + ".", message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Reports, "attachments", "present.png")));
    }

    [Fact]
    public void A_blank_path_is_counted_as_not_usable()
    {
        var result = Ingest(TestsFile(Attachment("blank", "")));

        Assert.Equal("1 attachment(s) are not in the report: 1 has a path that is not usable: (blank path).", TheEntry(result));
    }

    [Fact]
    public void A_path_with_a_NUL_character_does_not_stop_the_ingest()
    {
        // The harness's case2: an unhandled ArgumentException from Path.GetFullPath, and no report.
        IngestResult? result = null;
        var exception = Record.Exception(() => result = Ingest(TestsFile(Attachment("nul.png", "shot\0.png"))));

        Assert.Null(exception);
        Assert.True(File.Exists(Path.Combine(Reports, "TestRunReport.html")));
        Assert.StartsWith("1 attachment(s) are not in the report: 1 has a path that is not usable: ", TheEntry(result!), StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_attachments_base_from_a_library_caller_does_not_stop_the_ingest()
    {
        // The CLI makes --attachments-base absolute; IngestRequest.AttachmentsBase was taken as given, and a relative one
        // made Path.GetFullPath throw for every relative attachment path. It is read against the working directory, as
        // the CLI reads it.
        var relativeBase = "kronikol-relative-base-" + Guid.NewGuid().ToString("N");
        var shot = Path.Combine(Directory.GetCurrentDirectory(), relativeBase, "shots", "shot.png");
        Directory.CreateDirectory(Path.GetDirectoryName(shot)!);
        File.WriteAllBytes(shot, [137, 80, 78, 71]);
        try
        {
            IngestResult? result = null;
            var exception = Record.Exception(() => result = Ingest(TestsFile(Attachment("shot.png", "shots/shot.png")), attachmentsBase: relativeBase));

            Assert.Null(exception);
            Assert.True(result!.Generated);
            Assert.True(File.Exists(Path.Combine(Reports, "attachments", "shot.png")));
            Assert.DoesNotContain(result.Diagnostics, e => e.Kind == DiagnosticKind.AttachmentFailure);
        }
        finally
        {
            Directory.Delete(Path.Combine(Directory.GetCurrentDirectory(), relativeBase), recursive: true);
        }
    }

    [Fact]
    public void Clean_attachments_says_which_files_it_emptied_before_the_report_read_them()
    {
        // The harness's case3: the capture points into the report's own attachments/ folder, which --clean-attachments
        // empties before the copier runs.
        var pre = Written("out/attachments/pre.png");

        var message = TheEntry(Ingest(TestsFile(Attachment("pre.png", pre)), clean: true));

        Assert.StartsWith("1 attachment(s) are not in the report: 1 not found when the report was drawn: ", message, StringComparison.Ordinal);
        Assert.Contains(pre, message, StringComparison.Ordinal);
        Assert.Contains("--clean-attachments emptied the report's attachments/ folder before these were read.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_attachments_on_a_pass_with_no_scenarios_says_what_it_deleted()
    {
        // No scenario: no report is written, and the report already in the folder keeps its links to what was deleted.
        Written("out/attachments/old.png");
        var empty = PathOf("empty.ndjson");
        File.WriteAllText(empty, "");

        var result = Ingest(empty, clean: true, allowEmpty: true);

        Assert.False(File.Exists(Path.Combine(Reports, "attachments", "old.png")));
        Assert.StartsWith("1 attachment file(s) deleted from ", TheEntry(result), StringComparison.Ordinal);
        Assert.Contains("by --clean-attachments, and this pass wrote no report", TheEntry(result), StringComparison.Ordinal);
    }

    [Fact]
    public void The_ingest_command_prints_the_entry_once()
    {
        // kronikol ingest prints every entry it recorded; the run it drives must not print the same line a second time.
        var captures = Directory.CreateDirectory(PathOf("captures")).FullName;
        File.WriteAllText(Path.Combine(captures, "none.ndjson"), "");
        var tests = TestsFile(Attachment("missing.png", PathOf("gone", "missing.png")));

        var output = new StringWriter();
        string console;
        using (var scoped = new ThreadScopedConsole())
        {
            DefaultDiagramsFetcher.Reset();
            Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", Reports], output, new StringWriter()));
            DefaultDiagramsFetcher.Reset();
            console = scoped.Text;
        }

        var both = output + "\n" + console;
        Assert.Single(both.Split('\n'), l => l.Contains("attachment(s) are not in the report", StringComparison.Ordinal));
    }
}
