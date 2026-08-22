using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// <see cref="IngestRequest.HostDiagnostics"/>: what a host already knows about its capture components
/// (a tap whose decoder gave up, oversize payloads skipped) travels into <see cref="IngestResult.Diagnostics"/>,
/// the "Report diagnostics" section of <c>TestRunReport.html</c> and the <c>diagnostics</c> array of
/// <c>TestRunReport.json</c> — a dead tap is a line in the report, not only in a log.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestHostDiagnosticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-host-diag-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 14, 0, 0, TimeSpan.Zero);
    private const string TestId = "0af7651916cd43dd8448eb211c80319c";

    public IngestHostDiagnosticsTests()
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

    private static readonly DiagnosticEntry DeadTap = new(DiagnosticKind.CaptureDegraded,
        "tap-di-redis: decoding disabled on 1 connection(s) — Redis arrows after 14:03:35Z are missing (TapProtocolException: Buffered 8,421,376 undecoded bytes, over the cap)");

    private static readonly DiagnosticEntry HostNote = new(DiagnosticKind.Other, "host note <with markup> & symbols", TestId);

    private IngestRequest Request(string folder, IReadOnlyList<DiagnosticEntry> hostDiagnostics, bool withTraffic = true)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, folder);
        options.GenerateComponentDiagram = false;

        var (req, resp) = InteractionRecord.Pair(TestId, "The overview renders", "GET", "http://data-insights/api/overview", "data-insights", "web",
            statusCode: "200", requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(2));

        return new IngestRequest
        {
            Interactions = withTraffic ? [req, resp] : [],
            TestRecords = withTraffic
                ?
                [
                    new TestRunRecord { Event = "start", TestId = TestId, TestName = "The overview renders", Feature = "Overview", Timestamp = T0 },
                    new TestRunRecord { Event = "end", TestId = TestId, Status = "passed", DurationMs = 3000, Timestamp = T0.AddSeconds(3) },
                ]
                : [],
            Options = options,
            HostDiagnostics = hostDiagnostics,
        };
    }

    private static JsonDocument ReadJson(IngestResult result) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.json")));

    [Fact]
    public void Host_diagnostics_lead_the_result_and_reach_the_html_and_json_report()
    {
        var result = IngestPipeline.Run(Request("Host", [DeadTap, HostNote]));

        Assert.True(result.Generated);
        // Verbatim, and first — ahead of anything the ingest itself recorded.
        Assert.Equal(DeadTap, result.Diagnostics[0]);
        Assert.Equal(HostNote, result.Diagnostics[1]);

        var html = File.ReadAllText(result.TestRunReportHtml);
        Assert.Contains("class=\"report-diagnostics\"", html);
        Assert.Contains("Report diagnostics (", html);
        Assert.Contains("report-diagnostic-kind-capturedegraded", html);
        Assert.Contains("tap-di-redis: decoding disabled on 1 connection(s)", html);
        // Messages are encoded, never injected.
        Assert.Contains("host note &lt;with markup&gt; &amp; symbols", html);
        Assert.DoesNotContain("<with markup>", html);
        Assert.Contains($"[{TestId}]", html);

        using var json = ReadJson(result);
        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.True(diagnostics.Length >= 2);
        Assert.Equal("CaptureDegraded", diagnostics[0].GetProperty("kind").GetString());
        Assert.Equal(DeadTap.Message, diagnostics[0].GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, diagnostics[0].GetProperty("scenarioId").ValueKind);
        Assert.Equal("Other", diagnostics[1].GetProperty("kind").GetString());
        Assert.Equal(TestId, diagnostics[1].GetProperty("scenarioId").GetString());

        // The schema describes the new block.
        var schema = File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.schema.json"));
        using var schemaJson = JsonDocument.Parse(schema);
        Assert.True(schemaJson.RootElement.GetProperty("properties").TryGetProperty("diagnostics", out _));
        var kinds = schemaJson.RootElement.GetProperty("$defs").GetProperty("diagnostic").GetProperty("properties").GetProperty("kind").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("CaptureDegraded", kinds);
    }

    [Fact]
    public void Without_host_diagnostics_nothing_changes_and_the_data_file_mirrors_the_result()
    {
        var result = IngestPipeline.Run(Request("None", []));

        Assert.True(result.Generated);
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == DiagnosticKind.CaptureDegraded);

        using var json = ReadJson(result);
        var written = json.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("message").GetString()).ToArray();
        // Everything the data file lists was recorded (an OutputFailure raised by a parallel output can only be in the result).
        Assert.All(written, message => Assert.Contains(result.Diagnostics, d => d.Message == message));

        var html = File.ReadAllText(result.TestRunReportHtml);
        if (written.Length == 0)
            Assert.DoesNotContain("class=\"report-diagnostics\"", html);
        else
            Assert.Contains("class=\"report-diagnostics\"", html);
    }

    [Fact]
    public void Host_diagnostics_survive_an_ingest_that_generates_nothing()
    {
        var result = IngestPipeline.Run(Request("Empty", [DeadTap], withTraffic: false));

        Assert.False(result.Generated);
        Assert.Contains(DeadTap, result.Diagnostics);
    }

    [Fact]
    public void The_html_block_is_collapsed_summarises_kinds_and_encodes_every_field()
    {
        var html = ReportGenerator.RenderReportDiagnostics(
        [
            new DiagnosticEntry(DiagnosticKind.CaptureDegraded, "tap-a: 1 oversize payload skipped"),
            new DiagnosticEntry(DiagnosticKind.CaptureDegraded, "tap-b: decoding disabled"),
            new DiagnosticEntry(DiagnosticKind.MalformedLine, "file.ndjson:12 <torn>", "<s>"),
        ]);

        Assert.StartsWith("<details class=\"report-diagnostics\"><summary>", html);
        Assert.DoesNotContain("<details class=\"report-diagnostics\" open", html);
        Assert.Contains("Report diagnostics (3: CaptureDegraded ×2, MalformedLine)", html);
        Assert.Contains("&lt;torn&gt;", html);
        Assert.Contains("[&lt;s&gt;]", html);
        Assert.DoesNotContain("<torn>", html);
        Assert.Equal(string.Empty, ReportGenerator.RenderReportDiagnostics([]));
    }
}
