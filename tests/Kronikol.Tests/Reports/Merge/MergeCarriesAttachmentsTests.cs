using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge</c> carries each shard's attachment files beside the merged report. A shard's data file names its
/// files relative to its own folder (<c>attachments/shot.png</c>), and the merge wrote those names unchanged beside a
/// report whose folder held no <c>attachments/</c>: every screenshot of a sharded run was a dead link in the merged
/// report, its digest and <c>kronikol query</c> (found by the #175 audit).
/// </summary>
[Collection("DiagramsFetcher")]
public class MergeCarriesAttachmentsTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-attachments-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// A shard as a run writes it: one failing scenario whose failing step attached <c>shot.png</c> and which attached a
    /// <c>trace</c> of its own, both copied into the shard's <c>attachments/</c>. Returns its data file.
    /// </summary>
    private string Shard(string name, string scenario, byte mark, string? missing = null)
    {
        var captured = Path.Combine(_dir, "captured-" + name);
        Directory.CreateDirectory(captured);
        var shot = Path.Combine(captured, "shot.png");
        File.WriteAllBytes(shot, [137, 80, 78, 71, mark]);
        var trace = Path.Combine(captured, "trace.zip");
        File.WriteAllBytes(trace, [80, 75, 3, 4, mark]);
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = name + "-test",
                        DisplayName = scenario,
                        Result = ExecutionResult.Failed,
                        // Its own message: failures with one message are one cluster, which the digest details once.
                        ErrorMessage = $"{scenario} failed",
                        Attachments = [new FileAttachment("trace", trace, "application/zip")],
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "the card is charged", Status = ExecutionResult.Failed,
                                Attachments = missing is null
                                    ? [new FileAttachment("shot.png", shot, "image/png")]
                                    : [new FileAttachment("shot.png", shot, "image/png"), new FileAttachment("lost.png", missing, "image/png")],
                            },
                        ],
                    },
                ],
            },
        ];
        var directory = Path.Combine(_dir, name);
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = directory,
            PlantUmlRendering = PlantUmlRendering.BrowserJs,
            GenerateMergeableData = true,
            GenerateComponentDiagram = false,
            WriteRunSummaryToConsole = false,
        };
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, Start, Start.AddMinutes(1), options, RunEnvironment.Unrecorded, Environment.GetEnvironmentVariable);
        DefaultDiagramsFetcher.Reset();
        return Path.Combine(directory, "TestRunReport.json");
    }

    private static IEnumerable<FileAttachment> Attachments(MergeableReport report) =>
        report.Features.SelectMany(f => f.Scenarios)
            .SelectMany(s => (s.Attachments ?? []).Concat((s.Steps ?? []).SelectMany(step => step.Attachments ?? [])));

    [Fact]
    public void A_merged_report_links_each_shards_files_in_its_own_attachments_folder()
    {
        var first = Shard("runner1", "Pay", 1);
        var second = Shard("runner2", "Refund", 2);
        // What a shard says: its files relative to its own folder.
        Assert.All(Attachments(MergeableReportReader.ReadFile(first)), a => Assert.StartsWith("attachments/", a.RelativePath, StringComparison.Ordinal));
        var merged = Path.Combine(_dir, "merged");

        var error = new StringWriter();
        var exit = MergeCommand.Run([first, second, "-o", Path.Combine(merged, "Combined.html")], new StringWriter(), error, _ => null);

        Assert.True(exit == 0, error.ToString());
        var attachments = Attachments(MergeableReportReader.ReadFile(Path.Combine(merged, "Combined.json"))).ToArray();
        Assert.Equal(4, attachments.Length);
        Assert.All(attachments, a =>
        {
            Assert.StartsWith("attachments/", a.RelativePath, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(merged, a.RelativePath)), a.RelativePath);
        });
        // Each shard's own bytes, though both named their screenshot shot.png.
        Assert.Equal(new byte[] { 1, 2 }, attachments.Where(a => a.Name == "shot.png").Select(a => File.ReadAllBytes(Path.Combine(merged, a.RelativePath))[^1]).Order().ToArray());
        // And the digest names the files that are there.
        var digest = File.ReadAllText(Path.Combine(merged, "Failures.md"));
        Assert.All(attachments.Where(a => a.Name == "shot.png"), a => Assert.Contains(a.RelativePath, digest, StringComparison.Ordinal));
    }

    [Fact]
    public void A_merge_written_into_a_shards_own_folder_keeps_its_files_where_they_are()
    {
        // The shard's folder is the merged report's: its files are already in attachments/, and copying one onto itself
        // would fail.
        var shard = Shard("runner1", "Pay", 1);
        var folder = Path.GetDirectoryName(shard)!;
        var before = Directory.GetFiles(Path.Combine(folder, "attachments")).Order().ToArray();

        var error = new StringWriter();
        var exit = MergeCommand.Run([shard, "-o", Path.Combine(folder, "Combined.html")], new StringWriter(), error, _ => null);

        Assert.True(exit == 0, error.ToString());
        Assert.Equal(before, Directory.GetFiles(Path.Combine(folder, "attachments")).Order());
        Assert.All(Attachments(MergeableReportReader.ReadFile(Path.Combine(folder, "Combined.json"))), a =>
        {
            Assert.StartsWith("attachments/", a.RelativePath, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(folder, a.RelativePath)), a.RelativePath);
        });
    }

    // ─── #148: a shard's file that is not there (plans/MISSING_ATTACHMENTS_PLAN.md F5, section 4.2) ──────────

    /// <summary>Two shards whose screenshots share a name, the second's deleted after its run wrote it.</summary>
    private (string First, string Second, string Gone) ShardsWithOneScreenshotGone(string? missingInFirst = null)
    {
        var first = Shard("runner1", "Pay", 1, missingInFirst);
        var second = Shard("runner2", "Refund", 2);
        var gone = Path.Combine(Path.GetDirectoryName(second)!, "attachments", "shot.png");
        File.Delete(gone);
        return (first, second, gone);
    }

    private static FileAttachment ScreenshotOf(MergeableReport report, string scenario) =>
        report.Features.SelectMany(f => f.Scenarios).Single(s => s.DisplayName == scenario).Steps!.Single().Attachments!.Single(a => a.Name == "shot.png");

    [Fact]
    public void A_shards_missing_file_never_names_another_shards_file_of_the_same_name()
    {
        // Left relative, the second shard's attachments/shot.png named the first shard's shot.png, which the merge had
        // carried beside the merged report under that very name: the report showed the wrong screenshot.
        var (first, second, gone) = ShardsWithOneScreenshotGone();
        var merged = Path.Combine(_dir, "merged");

        var error = new StringWriter();
        Assert.True(MergeCommand.Run([first, second, "-o", Path.Combine(merged, "Combined.html")], new StringWriter(), error, _ => null) == 0, error.ToString());

        var report = MergeableReportReader.ReadFile(Path.Combine(merged, "Combined.json"));
        var refund = ScreenshotOf(report, "Refund");
        Assert.NotEqual(ScreenshotOf(report, "Pay").RelativePath, refund.RelativePath);
        Assert.False(File.Exists(Path.Combine(merged, refund.RelativePath)), refund.RelativePath);
        Assert.Equal(gone, refund.RelativePath);
    }

    [Fact]
    public void A_merge_says_a_shards_missing_file_in_its_data_file_and_its_labs_page()
    {
        var (first, second, gone) = ShardsWithOneScreenshotGone();
        var merged = Path.Combine(_dir, "merged");

        var error = new StringWriter();
        Assert.True(MergeCommand.Run([first, second, "-o", Path.Combine(merged, "Combined.html")], new StringWriter(), error, _ => null) == 0, error.ToString());

        var message = Assert.Single(MergeableReportReader.ReadFile(Path.Combine(merged, "Combined.json")).Diagnostics, d => d.Kind == DiagnosticKind.AttachmentFailure).Message;
        Assert.StartsWith("1 attachment(s) are not in the report: 1 not found when the report was drawn: ", message, StringComparison.Ordinal);
        Assert.Contains(gone, message, StringComparison.Ordinal);
        Assert.Contains("1 attachment(s) are not in the report: 1 not found when the report was drawn: ", File.ReadAllText(Path.Combine(merged, "Combined.labs.html")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_merged_report_drawn_with_its_diagnostics_section_shows_the_entry()
    {
        var (first, second, _) = ShardsWithOneScreenshotGone();
        var html = Path.Combine(_dir, "merged", "Combined.html");

        MergeableReportRenderer.Render(MergeableReportRenderer.MergeFiles([first, second]), html,
            options: new ReportConfigurationOptions { ShowReportDiagnosticsSection = true });

        Assert.Contains("1 attachment(s) are not in the report: 1 not found when the report was drawn: ", File.ReadAllText(html), StringComparison.Ordinal);
    }

    [Fact]
    public void The_merged_reports_own_entry_is_the_one_the_bang_line_shows()
    {
        // The first shard said its own missing file; the merged report holds neither that one nor the second shard's
        // screenshot. query's ! line shows the first entry of a kind, so the merge's own account comes first.
        var (first, second, _) = ShardsWithOneScreenshotGone(missingInFirst: Path.Combine(_dir, "nowhere", "lost.png"));
        var merged = Path.Combine(_dir, "merged");
        Assert.True(MergeCommand.Run([first, second, "-o", Path.Combine(merged, "Combined.html")], new StringWriter(), new StringWriter(), _ => null) == 0);

        var output = new StringWriter();
        Assert.Equal(0, Kronikol.Query.QueryCommand.Run(["summary", Path.Combine(merged, "Combined.json")], output, new StringWriter()));

        var line = Assert.Single(output.ToString().Split('\n'), l => l.StartsWith("! ", StringComparison.Ordinal) && l.Contains("attachment(s) are not in the report", StringComparison.Ordinal)).TrimEnd('\r');
        Assert.StartsWith("! 2 attachment(s) are not in the report: 2 not found when the report was drawn: ", line, StringComparison.Ordinal);
        Assert.EndsWith("(×2)", line, StringComparison.Ordinal);
    }
}
