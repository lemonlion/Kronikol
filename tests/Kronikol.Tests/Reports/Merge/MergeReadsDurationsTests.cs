using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Tool;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// A merge reads a scenario's duration back as the run wrote it (plans/WARM_UP_PLAN.md section 8). The data file writes
/// an unknown duration as 0 (its schema says so), and until 4.7.3 the merge read that 0 back as a measured zero, so
/// every scenario without a duration had a "0ms" badge in the merged report where the shard's own report had none.
/// </summary>
public class MergeReadsDurationsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-merge-durations").FullName;
    private static readonly DateTime Start = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_scenario_without_a_duration_has_no_badge_after_a_merge()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "timed", DisplayName = "Timed scenario", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(1500) },
                    new Scenario { Id = "untimed", DisplayName = "Untimed scenario", Result = ExecutionResult.Skipped }
                ]
            }
        ];
        File.WriteAllText(Path.Combine(_directory, "shard.json"), ReportGenerator.GenerateMergeableReportJson(features, Start, Start.AddMinutes(1),
            Array.Empty<DiagramAsCode>().ToLookup(d => d.TestRuntimeId, d => d.CodeBehind), [], internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null, diagnostics: null, trackedLogs: []));

        var error = new StringWriter();
        var exit = MergeCommand.Run([_directory, "-o", Path.Combine(_directory, "Combined.html")], new StringWriter(), error);
        Assert.True(exit == 0, error.ToString());
        var html = File.ReadAllText(Path.Combine(_directory, "Combined.html"));

        Assert.Equal(["1.5s"], Regex.Matches(html, "<span class=\"duration-badge [^\"]*\">([^<]*)</span>").Select(m => m.Groups[1].Value));
        Assert.Single(Regex.Matches(html, "data-duration-ms=\""));
        // The data file still writes the unknown duration as 0, as its schema says.
        Assert.Contains("\"durationSeconds\": 0", File.ReadAllText(Path.Combine(_directory, "Combined.json")), StringComparison.Ordinal);
    }
}
