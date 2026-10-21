using System.Text.RegularExpressions;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge</c> keeps <see cref="Scenario.NotATest"/> (plans/RUN_WINDOW_PLAN.md R1, F7). The data files did not
/// carry the flag, so a merged report counted a shard's fold scenario as a passed test, and its history, read against the
/// very ledger in which the shard's own run had recorded it as <c>N</c>, called it <c>stable: passing</c> (harness P4).
/// </summary>
[Collection("DiagramsFetcher")]
public class NotATestMergeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-not-a-test-" + Guid.NewGuid().ToString("N"));

    public NotATestMergeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static Feature[] Shard() =>
    [
        new Feature
        {
            DisplayName = "Ingested",
            Scenarios = [new Scenario { Id = "session", DisplayName = "Traffic outside any test", Result = ExecutionResult.Passed, NotATest = true, Duration = TimeSpan.FromSeconds(1) }]
        },
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario { Id = "b1", DisplayName = "Lists orders", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(40) },
                new Scenario { Id = "b2", DisplayName = "Shows the menu", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(30) },
            ]
        },
    ];

    [Fact]
    public void A_merged_report_counts_a_shards_fold_scenario_as_no_test_and_its_history_says_not_a_test()
    {
        var start = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        var shards = Path.Combine(_dir, "shards");
        Directory.CreateDirectory(shards);
        File.WriteAllText(Path.Combine(shards, "browser.json"), ReportGenerator.GenerateMergeableReportJson(Shard(), start, start.AddSeconds(9),
            diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null));

        // The ledger the shard's own run wrote, the fold recorded as not a test: harness P4's "NPP".
        var (roster, run) = HistoryRunBuilder.Build(Shard(), [], null, null, new DateTimeOffset(start), new HistoryBuildOptions());
        Assert.Equal("NPP", run.Results);
        var ledger = Path.Combine(_dir, ".kronikol", "history.jsonl");
        HistoryLedgerWriter.Append(ledger, roster, run with { Id = "gh:1:1", At = new DateTimeOffset(start).AddHours(-1) }, "5.2.5");

        var output = Path.Combine(_dir, "Merged.html");
        var error = new StringWriter();
        Assert.True(MergeCommand.Run([shards, "-o", output, "--history", ledger], new StringWriter(), error, _ => null) == 0, error.ToString());

        Assert.Contains("<div class=\"run-stat\"><dt>Scenarios</dt><dd>2</dd></div>", File.ReadAllText(output));
        Assert.Contains("All 2 scenarios passed.", File.ReadAllText(Path.Combine(_dir, "Failures.md")));
        var labs = File.ReadAllText(Path.Combine(_dir, "Merged.labs.html"));
        var row = labs.IndexOf("Traffic outside any test", StringComparison.Ordinal);
        Assert.True(row >= 0, "the labs page holds the fold scenario's history row");
        var label = Regex.Match(labs.Substring(row, Math.Min(300, labs.Length - row)), "aria-label=\"([^\"]*)\"");
        Assert.Equal("stable: not a test: it collects the traffic no test could be given", label.Groups[1].Value);
    }

    [Fact]
    public void A_merged_data_file_writes_the_flag_again()
    {
        var start = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        var shards = Path.Combine(_dir, "shards-data");
        Directory.CreateDirectory(shards);
        File.WriteAllText(Path.Combine(shards, "browser.json"), ReportGenerator.GenerateMergeableReportJson(Shard(), start, start.AddSeconds(9),
            diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null));

        var merged = MergeableReportRenderer.MergeFiles(Directory.GetFiles(shards));

        Assert.True(merged.Features.SelectMany(f => f.Scenarios).Single(s => s.DisplayName == "Traffic outside any test").NotATest);
        Assert.False(merged.Features.SelectMany(f => f.Scenarios).Single(s => s.DisplayName == "Lists orders").NotATest);
    }
}
