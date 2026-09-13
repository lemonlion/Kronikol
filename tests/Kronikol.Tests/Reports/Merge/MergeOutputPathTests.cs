using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge -o /some/where/Combined.html</c> is asked for a path, and it wrote somewhere else.
///
/// <para><see cref="MergeableReportRenderer.Render"/> reduced the caller's path to its file name, let
/// <c>GenerateHtmlReport</c> write it under <c>&lt;BaseDirectory&gt;/Reports</c>, and then copied it to
/// the destination. The copy meant the requested file did appear — so nothing looked wrong — while every
/// merge also left the report in the tool's own install directory, and two merges of different shards to
/// different destinations raced each other over one intermediate name.</para>
///
/// <para>That race was invisible for exactly as long as an unwritable output was silently salvaged to
/// <c>&lt;name&gt;2.html</c>. It surfaced the moment a failed write started being reported, which is what
/// reporting it is for: this suite's own merge tests began failing intermittently on a file neither of
/// them had asked to share.</para>
/// </summary>
public class MergeOutputPathTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-mergepath-" + Guid.NewGuid().ToString("N"));

    public MergeOutputPathTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static MergeableReport OneReport(string feature) =>
        MergeableReportReader.Parse(ReportGenerator.GenerateMergeableReportJson(
            [
                new Feature
                {
                    DisplayName = feature,
                    Scenarios = [new Scenario { Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Passed }]
                }
            ],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            Array.Empty<DefaultDiagramsFetcher.DiagramAsCode>().ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            componentRelationships: [],
            internalFlowSegmentData: null,
            wholeTestFlow: null,
            WholeTestFlowVisualization.None,
            ciMetadata: null));

    [Fact]
    public void The_merged_report_is_written_where_it_was_asked_for()
    {
        var wanted = Path.Combine(_dir, "nested", "Combined.html");

        var written = MergeableReportRenderer.Render(OneReport("Orders"), wanted, title: "Combined");

        Assert.Equal(Path.GetFullPath(wanted), Path.GetFullPath(written));
        Assert.True(File.Exists(wanted), $"nothing at {wanted}");
        Assert.Contains("Combined", File.ReadAllText(wanted), StringComparison.Ordinal);
    }

    [Fact]
    public void It_does_not_also_leave_a_copy_in_the_default_reports_directory()
    {
        // The littering half. A merge run from an installed tool put its output beside the binary, every
        // time, under whatever name the caller had chosen.
        var stray = Path.Combine(AppContext.BaseDirectory, "Reports", "MergePathProbe.html");
        if (File.Exists(stray)) File.Delete(stray);

        MergeableReportRenderer.Render(OneReport("Orders"), Path.Combine(_dir, "MergePathProbe.html"), title: "Combined");

        Assert.False(File.Exists(stray), $"a copy was left at {stray}");
    }

    [Fact]
    public void Two_merges_to_two_destinations_do_not_share_an_intermediate_file()
    {
        // The race, as a fact rather than as an intermittent failure somewhere else. Same file NAME, two
        // destinations: before the fix both went through one path under the base directory.
        var first = Path.Combine(_dir, "a", "Combined.html");
        var second = Path.Combine(_dir, "b", "Combined.html");

        Parallel.Invoke(
            () => MergeableReportRenderer.Render(OneReport("Orders"), first, title: "First"),
            () => MergeableReportRenderer.Render(OneReport("Inventory"), second, title: "Second"));

        Assert.Contains("First", File.ReadAllText(first), StringComparison.Ordinal);
        Assert.Contains("Second", File.ReadAllText(second), StringComparison.Ordinal);
    }
}
