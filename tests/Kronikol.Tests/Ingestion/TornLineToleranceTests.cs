using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// A capturer killed mid-write leaves a truncated last line. Losing an entire run's report to it —
/// which is what used to happen — helps nobody, so malformed lines are skipped, counted and reported.
/// </summary>
[Collection("DiagramsFetcher")]
public class TornLineToleranceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-torn-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
    private const string TestId = "33333333333333333333333333333333";

    public TornLineToleranceTests()
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

    [Fact]
    public void Without_a_collector_a_malformed_line_still_throws_with_its_line_number()
    {
        var reader = new StringReader("{\"type\":\"Request\",\"uri\":\"http://x/\",\"serviceName\":\"a\",\"callerName\":\"b\",\"testId\":\"t\"}\n{tor");

        var ex = Assert.Throws<FormatException>(() => NdjsonInteractionReader.Read(reader, "capture.ndjson"));
        Assert.Contains("capture.ndjson:2", ex.Message);
    }

    [Fact]
    public void With_a_collector_the_torn_line_is_skipped_and_described()
    {
        var malformed = new List<MalformedLine>();
        var reader = new StringReader(
            "{\"type\":\"Request\",\"uri\":\"http://x/\",\"serviceName\":\"a\",\"callerName\":\"b\",\"testId\":\"t\"}\n"
            + "\n"
            + "{\"type\":\"Response\",\"uri\":\"http://x/\",\"serviceName\":\"a\",\"callerName\":\"b\",\"testId\":\"t\"}\n"
            + "{\"type\":\"Request\",\"uri\":\"http://x/tr");

        var records = NdjsonInteractionReader.Read(reader, "capture.ndjson", malformed);

        Assert.Equal(2, records.Count);
        var torn = Assert.Single(malformed);
        Assert.Equal("capture.ndjson", torn.Source);
        // Line 4: the blank line is skipped but still counted, so the number matches what an editor shows.
        Assert.Equal(4, torn.LineNumber);
        Assert.StartsWith("{\"type\":\"Request\"", torn.Excerpt);
    }

    [Fact]
    public void The_excerpt_is_capped_so_a_huge_torn_line_cannot_flood_the_output()
    {
        var malformed = new List<MalformedLine>();
        NdjsonInteractionReader.Read(new StringReader("{" + new string('x', 5000)), "capture.ndjson", malformed);

        var torn = Assert.Single(malformed);
        Assert.Equal(MalformedLine.ExcerptLength + 1, torn.Excerpt.Length); // 80 characters plus the ellipsis
        Assert.EndsWith("…", torn.Excerpt);
    }

    [Fact]
    public void The_tests_reader_tolerates_torn_lines_the_same_way()
    {
        var malformed = new List<MalformedLine>();
        var records = NdjsonTestRunReader.Read(
            new StringReader("{\"event\":\"start\",\"testId\":\"t\"}\n{\"event\":\"en"), "tests.jsonl", malformed);

        Assert.Single(records);
        Assert.Single(malformed);

        Assert.Throws<FormatException>(() =>
            NdjsonTestRunReader.Read(new StringReader("{\"event\":\"en"), "tests.jsonl"));
    }

    [Fact]
    public void A_run_killed_mid_write_still_produces_a_report_from_every_complete_line()
    {
        var (request, response) = InteractionRecord.Pair(TestId, "overview", "GET", "http://localhost/overview", "api", "web",
            responseContent: "{}", statusCode: "200", requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(2));

        var capture = Path.Combine(_dir, "capture.ndjson");
        // The last line is what the process managed to flush before it died.
        File.WriteAllText(capture, request.ToJson() + "\n" + response.ToJson() + "\n" + response.ToJson()[..40]);

        var tests = Path.Combine(_dir, "tests.jsonl");
        File.WriteAllText(tests,
            new TestRunRecord { Event = "start", TestId = TestId, TestName = "overview renders", Timestamp = T0 }.ToJson()
            + "\n{\"event\":\"en");

        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Reports");
        options.GenerateComponentDiagram = false;

        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            TestsFile = tests,
            Options = options,
        });

        Assert.True(result.Generated);
        Assert.True(File.Exists(result.TestRunReportHtml));
        Assert.Equal(2, result.InteractionCount);
        Assert.Equal("overview renders", result.Features[0].Scenarios[0].DisplayName);

        var malformed = result.Diagnostics.Where(d => d.Kind == DiagnosticKind.MalformedLine).ToArray();
        Assert.Equal(2, malformed.Length); // one per file
        Assert.Contains(malformed, d => d.Message.Contains("capture.ndjson:3"));
        Assert.Contains(malformed, d => d.Message.Contains("tests.jsonl:2"));
    }

    [Fact]
    public void Strict_parsing_restores_the_throw_for_a_pipeline_that_wants_a_bad_producer_to_be_loud()
    {
        var capture = Path.Combine(_dir, "strict.ndjson");
        File.WriteAllText(capture, "{not json\n");

        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Strict");

        Assert.Throws<FormatException>(() => IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            Options = options,
            StrictParsing = true,
        }));
    }
}
