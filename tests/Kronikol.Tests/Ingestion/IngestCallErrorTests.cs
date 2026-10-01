using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// <c>plans/INGEST_FIDELITY_PLAN.md</c> S2 (T14), from P2: an external capturer's failed call. Its status survived ingest
/// (the diagram, the digest's ranking and the fingerprint read it), its message chain did not, so the data files,
/// <c>kronikol query http</c> and anything reading <c>httpInteractions[].error</c> never saw why the call failed; and an
/// <c>error</c> that was not a string was dropped without a word.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestCallErrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-call-errors-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    public IngestCallErrorTests()
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

    private static string At(double ms) => T0.AddMilliseconds(ms).ToString("O");

    private static string Line(string type, string method, string path, string service, double ms, string extra) =>
        $$"""{"type":"{{type}}","method":"{{method}}","uri":"http://localhost:5000{{path}}","serviceName":"{{service}}","callerName":"superpay-graphql","requestResponseId":"{{path}}","timestamp":"{{At(ms)}}","testId":"p2"{{extra}}}""";

    [Fact]
    public void An_external_capturers_error_reaches_the_data_file_the_diagram_and_query_http()
    {
        var capture = Path.Combine(_dir, "p2.ndjson");
        File.WriteAllLines(capture,
        [
            // The in-process shape: the failure half with !Type as its status and the message chain in error.
            Line("Request", "POST", "/charges", "psp", 1000, ""),
            Line("Response", "POST", "/charges", "psp", 1100, ",\"statusCode\":\"!TypeError\",\"error\":\"fetch failed Caused by: connect ECONNREFUSED 127.0.0.1:443\""),
            // An object in error and no status.
            Line("Request", "GET", "/risk/1", "risk", 2000, ""),
            Line("Response", "GET", "/risk/1", "risk", 2100, ",\"error\":{\"code\":\"ECONNRESET\"}"),
            // An error written on the request half.
            Line("Request", "GET", "/ledger", "ledger", 3000, ",\"error\":\"aborted before sending\""),
            Line("Response", "GET", "/ledger", "ledger", 3100, ",\"statusCode\":\"200\""),
        ]);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Reports");
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;

        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "p2", TestName = "reports a provider outage", Timestamp = T0 },
                new TestRunRecord { Event = "end", TestId = "p2", Status = "failed", Error = "expected 502, received 500", Timestamp = T0.AddSeconds(4) },
            ],
            Options = options,
        });

        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == DiagnosticKind.MalformedLine);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.ReportsFolderPath, "TestRunReport.json")));
        var scenario = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
        var calls = scenario.GetProperty("httpInteractions").EnumerateArray().ToArray();
        string? Error(int i) => calls[i].GetProperty("error").GetString();
        string? StatusText(int i) => calls[i].GetProperty("statusText").GetString();

        Assert.Equal("fetch failed Caused by: connect ECONNREFUSED 127.0.0.1:443", Error(1));
        Assert.Equal("!TypeError", StatusText(1));
        Assert.Equal("{\"code\":\"ECONNRESET\"}", Error(3));
        Assert.Equal("!Error", StatusText(3));
        Assert.Equal("aborted before sending", Error(4));
        Assert.Null(Error(5));

        var diagram = ReportPayloadText.Of(scenario.GetProperty("diagrams")[0])!;
        Assert.Contains("!TypeError", diagram);
        Assert.Contains("!Error", diagram);

        var onRequests = Assert.Single(result.Diagnostics, d => d.Message.Contains("response half", StringComparison.Ordinal));
        Assert.Contains("1 request record(s)", onRequests.Message);

        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["http", options.ReportsFolderPath, "s0/i1"], output, new StringWriter()));
        Assert.Contains("error fetch failed Caused by: connect ECONNREFUSED 127.0.0.1:443", output.ToString());
    }
}
