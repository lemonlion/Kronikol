using System.Text.Json;
using Kronikol.Constants;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

[Collection("DiagramsFetcher")]
public class IngestPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-ingest-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    public IngestPipelineTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string WriteCapture(string name, params InteractionRecord[] records)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, records.Select(r => r.ToJson()));
        return path;
    }

    private string WriteTests(params TestRunRecord[] records)
    {
        var path = Path.Combine(_dir, "tests.ndjson");
        File.WriteAllLines(path, records.Select(r => r.ToJson()));
        return path;
    }

    [Fact]
    public void Replays_captures_attributes_by_test_id_and_writes_a_full_report_to_the_output_dir()
    {
        const string testId = "0af7651916cd43dd8448eb211c80319c";
        var (req1, resp1) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:8081/sidekick", "graphql", "web",
            requestContent: """{"query":"query Overview { overview }"}""", responseContent: """{"data":{}}""", statusCode: "200",
            requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(1.2));
        var (req2, resp2) = InteractionRecord.Pair(testId, null, "Query", "http://bq/projects/p/queries", "bigquery", "data-insights",
            requestContent: "SELECT 1", responseContent: "rows", statusCode: "200",
            requestTimestamp: T0.AddSeconds(2), responseTimestamp: T0.AddSeconds(2.5), dependencyCategory: DependencyCategories.BigQuery);
        // Written out of order on purpose: the pipeline must order by timestamp.
        var webFile = WriteCapture("web.ndjson", resp1, req1);
        var bqFile = WriteCapture("bq.ndjson", req2, resp2);
        var testsFile = WriteTests(
            new TestRunRecord { Event = "start", TestId = testId, TestName = "overview › renders", Feature = "overview.spec.ts", Timestamp = T0 },
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 3000, Timestamp = T0.AddSeconds(3) });

        var output = Path.Combine(_dir, "Reports");
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [webFile, bqFile], TestsFile = testsFile, Options = options });

        Assert.True(result.Generated);
        Assert.Equal(4, result.InteractionCount);
        Assert.Equal(1, result.ScenarioCount);
        Assert.Equal(Path.GetFullPath(output), result.ReportsDirectory);
        Assert.True(File.Exists(result.TestRunReportHtml));

        var html = File.ReadAllText(result.TestRunReportHtml);
        Assert.Contains("overview › renders", html);
        Assert.Contains("overview.spec.ts", html);
        Assert.DoesNotContain("data-no-interactions", html); // the calls were attributed

        // The data file carries the interactions under the scenario, in timestamp order, with the name from the tests file.
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var scenario = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
        Assert.Equal(testId, scenario.GetProperty("id").GetString());
        var interactions = scenario.GetProperty("httpInteractions").EnumerateArray().ToArray();
        Assert.Equal(4, interactions.Length);
        Assert.Equal("Request", interactions[0].GetProperty("type").GetString());
        Assert.Equal("http://localhost:8081/sidekick", interactions[0].GetProperty("uri").GetString());
        Assert.Equal("Response", interactions[1].GetProperty("type").GetString());
        Assert.Equal("QUERY", interactions[2].GetProperty("method").GetString());

        // Stored logs carry the normalised name.
        Assert.All(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId), l => Assert.Equal("overview › renders", l.TestName));
    }

    [Fact]
    public void Without_a_tests_file_scenarios_are_synthesised_from_the_captures()
    {
        var (req, resp) = InteractionRecord.Pair("t-only", "Named by capturer", "GET", "http://a/x", "A", "Test", statusCode: "200", requestTimestamp: T0);
        var file = WriteCapture("only.ndjson", req, resp);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "R2");

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], Options = options, DefaultFeatureName = "Captured" });

        Assert.True(result.Generated);
        var feature = Assert.Single(result.Features);
        Assert.Equal("Captured", feature.DisplayName);
        Assert.Equal("Named by capturer", feature.Scenarios.Single().DisplayName);
        Assert.Contains("Named by capturer", File.ReadAllText(result.TestRunReportHtml));
    }

    [Fact]
    public void Empty_input_skips_generation_unless_allowed()
    {
        var file = Path.Combine(_dir, "empty.ndjson");
        File.WriteAllText(file, "\n");
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "R3");

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], Options = options });

        Assert.False(result.Generated);
        Assert.False(File.Exists(result.TestRunReportHtml));
    }

    [Fact]
    public void Redaction_applies_during_replay_so_a_raw_capture_does_not_leak_into_the_report()
    {
        var (req, resp) = InteractionRecord.Pair("t-secret", "Secret", "GET", "http://a/x", "A", "Test", statusCode: "200",
            requestHeaders: [new InteractionHeader("Authorization", "Bearer leaked-in-capture")], requestTimestamp: T0);
        var file = WriteCapture("secret.ndjson", req, resp);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "R4");
        RequestResponseLogger.Redaction = CaptureRedaction.Secrets();

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], Options = options });

        var json = File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.json"));
        Assert.DoesNotContain("leaked-in-capture", json);
        Assert.Contains("[REDACTED]", json);
    }

    [Fact]
    public void Missing_files_throw_clearly()
    {
        Assert.Throws<FileNotFoundException>(() => IngestPipeline.Run(new IngestRequest { InteractionFiles = [Path.Combine(_dir, "nope.ndjson")] }));
        Assert.Throws<FileNotFoundException>(() => IngestPipeline.Run(new IngestRequest { TestsFile = Path.Combine(_dir, "nope-tests.ndjson") }));
    }

    [Fact]
    public void Default_options_suit_external_capture()
    {
        var options = IngestPipeline.DefaultOptions();
        Assert.False(options.InternalFlowTracking);
        Assert.True(options.GenerateComponentDiagram);
        Assert.True(options.CollapseConsecutiveIdenticalCalls);
        Assert.Equal(PlantUmlRendering.BrowserJs, options.PlantUmlRendering);
    }
}
