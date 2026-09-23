using System.Text;
using Kronikol.Ingestion;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

// TEMPORARY PROBE for plans/INGEST_FEED_PLAN.md S3 (prototype worktree only). Ingests the capture the
// LightBDD xUnit3 example projected at run end, with that suite's own report options, so the two
// TestRunReport.json files can be compared like for like by compare-reports.py.
[Collection("DiagramsFetcher")]
public class P1RealSuiteIngestTests
{
    [Fact]
    public void Ingest_the_projected_capture_with_the_suites_options()
    {
        var capture = Environment.GetEnvironmentVariable("P1_NDJSON");
        var output = Environment.GetEnvironmentVariable("P1_OUT");
        if (string.IsNullOrEmpty(capture) || string.IsNullOrEmpty(output))
            return; // inert unless asked

        var sb = new StringBuilder();
        var options = new ReportConfigurationOptions
        {
            SpecificationsTitle = "Dessert Provider Specifications",
            SeparateSetup = true,
            ReportsFolderPath = output,
            WriteRunSummaryToConsole = false,
        };
        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            Options = options,
            CallTreeOrdering = false,
        });
        sb.AppendLine($"Replayed={result.InteractionCount} scenarios={result.ScenarioCount} generated={result.Generated} dir={result.ReportsDirectory}");
        sb.AppendLine("diagnostics: " + string.Join(" | ", result.Diagnostics.Select(d => $"{d.Kind}: {d.Message}")));
        var markers = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.IsDiagramMarker).ToArray();
        sb.AppendLine($"marker logs in the store after replay: {markers.Length}; by kind: " +
                      string.Join(", ", markers.GroupBy(m => m.MarkerKind).Select(g => $"{g.Key}={g.Count()}")));
        File.WriteAllText(Path.Combine(output, "p1-real-suite-ingest.txt"), sb.ToString());
    }
}
