using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// The labs page of a merged report (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md sections 3.6 and 3.7):
/// <see cref="MergeableReportRenderer.Render"/> follows the options as a run does, so the merged report carries no
/// history or diagnostics by default, and writes the page beside it, named after it, when the merge read history or
/// its shards recorded diagnostics. F3: the shards' diagnostics reach the HTML at all, where 4.5.0 dropped them.
/// </summary>
public class MergedLabsPageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-merged-labs").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static readonly DateTime Start = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static MergeableReport Report(params DiagnosticEntry[] diagnostics) => new()
    {
        Suite = "MergeSuite",
        StartTime = Start,
        EndTime = Start.AddMinutes(2),
        Features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = "m1", DisplayName = "Place order", Result = ExecutionResult.Failed, ErrorMessage = "oops", Duration = TimeSpan.FromMilliseconds(50) }]
            }
        ],
        Diagnostics = diagnostics
    };

    private static readonly DiagnosticEntry Dropped = new(DiagnosticKind.CaptureDegraded, "shard 2: a tap dropped 3 segments");

    private string Render(MergeableReport report, ReportConfigurationOptions? options = null, HistoryVerdicts? history = null) =>
        MergeableReportRenderer.Render(report, Path.Combine(_dir, "Combined.html"), options: options, history: history);

    [Fact]
    public void The_shards_diagnostics_are_listed_on_the_page_beside_the_merged_report()
    {
        Render(Report(Dropped));

        var page = File.ReadAllText(Path.Combine(_dir, "Combined.labs.html"));
        Assert.Contains("<details class=\"report-diagnostics\" open>", page);
        Assert.Contains("shard 2: a tap dropped 3 segments", page);
        Assert.DoesNotContain("report-diagnostic", File.ReadAllText(Path.Combine(_dir, "Combined.html")));
    }

    [Fact]
    public void A_merged_report_that_asks_for_the_diagnostics_section_lists_the_shards_diagnostics()
    {
        Render(Report(Dropped), new ReportConfigurationOptions { ShowReportDiagnosticsSection = true });

        var html = File.ReadAllText(Path.Combine(_dir, "Combined.html"));
        Assert.Contains("<details class=\"report-diagnostics\"", html);
        Assert.Contains("shard 2: a tap dropped 3 segments", html);
    }

    [Fact]
    public void A_merge_with_nothing_for_the_page_writes_none_and_neither_does_one_told_not_to()
    {
        Render(Report());
        Assert.False(File.Exists(Path.Combine(_dir, "Combined.labs.html")));

        Render(Report(Dropped), new ReportConfigurationOptions { GenerateLabsReport = false });
        Assert.False(File.Exists(Path.Combine(_dir, "Combined.labs.html")));
    }
}
