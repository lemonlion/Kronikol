using System.Net;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

public class ExportCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-export-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
    private const string TestId = "cafe651916cd43dd8448eb211c80319c";

    public ExportCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string WriteCapture(string name = "web.ndjson")
    {
        var (req, resp) = InteractionRecord.Pair(TestId, "cli › exports", "POST", "http://localhost:8081/sidekick", "graphql", "web",
            requestContent: "{\"q\":1}", responseContent: "{\"ok\":true}", statusCode: "200",
            requestHeaders: [new InteractionHeader("Authorization", "Bearer cli-secret")],
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(30));
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, [req.ToJson(), resp.ToJson()]);
        return path;
    }

    [Fact]
    public void Dry_run_writes_decodable_otlp_json_and_prints_counts()
    {
        var capture = WriteCapture();
        var outFile = Path.Combine(_dir, "export.json");
        var @out = new StringWriter();
        var err = new StringWriter();

        var exit = ExportCommand.Run([capture, "--dry-run", "--out", outFile], @out, err);

        Assert.Equal(0, exit);
        var spans = OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile));
        var span = Assert.Single(spans);
        Assert.Equal("POST", span.Name);
        Assert.Equal(TestId, span.TraceId);
        Assert.Equal("http://localhost:8081/sidekick", span.Attribute("url.full"));
        Assert.Equal("200", span.Attribute("http.response.status_code"));
        Assert.Equal("graphql", span.Attribute("peer.service"));
        Assert.Equal(TestId, span.Attribute("kronikol.test.id"));
        // Bodies stay home unless opted in.
        Assert.Null(span.Attribute("kronikol.request.body"));
        var stdout = @out.ToString();
        Assert.Contains("1 span(s)", stdout);
        Assert.Contains("1 trace(s)", stdout);
        Assert.Contains("dry run", stdout);
    }

    [Fact]
    public void Dry_run_without_out_writes_the_json_to_stdout()
    {
        var capture = WriteCapture();
        var @out = new StringWriter();

        var exit = ExportCommand.Run([capture, "--dry-run"], @out, new StringWriter());

        Assert.Equal(0, exit);
        var json = @out.ToString();
        var start = json.IndexOf("{\"resourceSpans\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "stdout should carry the OTLP/JSON document");
        var line = json[start..].Split('\n')[0].TrimEnd('\r');
        Assert.Single(OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(line)));
    }

    [Fact]
    public void Include_bodies_and_body_cap_reach_the_output()
    {
        var capture = WriteCapture();
        var outFile = Path.Combine(_dir, "export.json");

        var exit = ExportCommand.Run([capture, "--dry-run", "--out", outFile, "--include-bodies", "--body-cap", "4"], new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var span = Assert.Single(OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile)));
        var body = span.Attribute("kronikol.request.body");
        Assert.NotNull(body);
        Assert.Contains("…truncated", body);
    }

    [Fact]
    public void Per_pair_traces_keeps_the_raw_trace_id()
    {
        var capture = WriteCapture();
        var outFile = Path.Combine(_dir, "export.json");

        var exit = ExportCommand.Run([capture, "--dry-run", "--out", outFile, "--per-pair-traces"], new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var span = Assert.Single(OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile)));
        Assert.NotEqual(TestId, span.TraceId); // the pair's own id, not the per-test derivation
        Assert.Equal(32, span.TraceId.Length);
    }

    [Fact]
    public void Export_posts_to_a_live_endpoint()
    {
        using var listener = new HttpListener();
        var port = FreePort();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var received = new List<byte[]>();
        var serving = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using var buffer = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(buffer);
            lock (received) received.Add(buffer.ToArray());
            context.Response.StatusCode = 200;
            context.Response.Close(Encoding.UTF8.GetBytes("{}"), willBlock: false);
        });
        var capture = WriteCapture();
        var @out = new StringWriter();

        var exit = ExportCommand.Run([capture, "--otlp", $"http://localhost:{port}/v1/traces", "--header", "x-token=abc"], @out, new StringWriter());

        Assert.Equal(0, exit);
        Assert.True(serving.Wait(TimeSpan.FromSeconds(10)));
        byte[] body;
        lock (received) body = Assert.Single(received);
        Assert.Single(OtlpTraceReader.ReadJson(body));
        Assert.Contains("Exported 1 span(s)", @out.ToString());
        listener.Stop();
    }

    [Fact]
    public void An_unreachable_collector_is_a_runtime_failure()
    {
        var capture = WriteCapture();
        var err = new StringWriter();

        var exit = ExportCommand.Run([capture, "--otlp", $"http://localhost:{FreePort()}/v1/traces"], new StringWriter(), err);

        Assert.Equal(1, exit);
        Assert.Contains("failed", err.ToString());
    }

    [Fact]
    public void Usage_errors()
    {
        var err = new StringWriter();
        Assert.Equal(2, ExportCommand.Run([], new StringWriter(), err));
        Assert.Contains("No inputs", err.ToString());

        // No endpoint and no dry run.
        err = new StringWriter();
        Assert.Equal(2, ExportCommand.Run(["x.ndjson"], new StringWriter(), err));
        Assert.Contains("--otlp", err.ToString());

        // Bad endpoint.
        Assert.Equal(2, ExportCommand.Run(["x.ndjson", "--otlp", "not a uri"], new StringWriter(), new StringWriter()));

        // Malformed header.
        Assert.Equal(2, ExportCommand.Run(["x.ndjson", "--otlp", "http://x/v1/traces", "--header", "novalue"], new StringWriter(), new StringWriter()));

        // Bad body cap.
        Assert.Equal(2, ExportCommand.Run(["x.ndjson", "--otlp", "http://x/v1/traces", "--body-cap", "zero"], new StringWriter(), new StringWriter()));

        // --out without --dry-run.
        Assert.Equal(2, ExportCommand.Run(["x.ndjson", "--otlp", "http://x/v1/traces", "--out", "f.json"], new StringWriter(), new StringWriter()));

        // Unknown option.
        Assert.Equal(2, ExportCommand.Run(["x.ndjson", "--dry-run", "--frobnicate"], new StringWriter(), new StringWriter()));

        // Missing values.
        Assert.Equal(2, ExportCommand.Run(["x.ndjson", "--otlp"], new StringWriter(), new StringWriter()));
        Assert.Equal(2, ExportCommand.Run(["x.ndjson", "--dry-run", "--redact-header"], new StringWriter(), new StringWriter()));

        // Help is exit 0.
        var @out = new StringWriter();
        Assert.Equal(0, ExportCommand.Run(["--help"], @out, new StringWriter()));
        Assert.Contains("kronikol export", @out.ToString());
    }

    [Fact]
    public void Missing_capture_files_are_a_runtime_failure()
    {
        var err = new StringWriter();
        var exit = ExportCommand.Run([Path.Combine(_dir, "absent"), "--dry-run"], new StringWriter(), err);
        Assert.Equal(1, exit);
        Assert.Contains("No matching capture files", err.ToString());
    }

    [Fact]
    public void Redaction_flags_are_accepted_and_default_on()
    {
        // Header redaction has nothing to redact in v1 output (headers are never exported), but the
        // flags must parse and the pipeline must run redacted by default — the NDJSON path is the one
        // capture path where nothing has redacted yet.
        var capture = WriteCapture();
        Assert.Equal(0, ExportCommand.Run([capture, "--dry-run", "--redact-header", "x-custom-token"], new StringWriter(), new StringWriter()));
        Assert.Equal(0, ExportCommand.Run([capture, "--dry-run", "--no-redact"], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void Span_sourced_echoes_are_skipped_and_counted()
    {
        var (req, resp) = InteractionRecord.Pair(TestId, null, "GET", "http://api/x", "backend", "web",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        req = req with { CapturedBy = InteractionMerger.SpanSource };
        resp = resp with { CapturedBy = InteractionMerger.SpanSource };
        var path = Path.Combine(_dir, "spans.ndjson");
        File.WriteAllLines(path, [req.ToJson(), resp.ToJson()]);
        var outFile = Path.Combine(_dir, "export.json");
        var @out = new StringWriter();

        var exit = ExportCommand.Run([path, "--dry-run", "--out", outFile], @out, new StringWriter());

        Assert.Equal(0, exit);
        Assert.Empty(OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile)));
        Assert.Contains("2 record(s) skipped", @out.ToString());

        var included = ExportCommand.Run([path, "--dry-run", "--out", outFile, "--include-span-sourced"], new StringWriter(), new StringWriter());
        Assert.Equal(0, included);
        Assert.Single(OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile)));
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
