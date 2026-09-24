using System.Net;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tool;
using Kronikol.Tracking;

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
    public void A_projected_stores_marker_records_export_no_span()
    {
        // Rendering control, not telemetry. Until 3.29.0 the writer wrote each marker half as an empty request
        // line, and the export sent every one as a span of its own; as kind: marker records they are restored
        // as the markers they are, and skipped.
        var capture = WriteCapture();
        var opening = new RequestResponseLog(TestId, TestId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = true,
            MarkerKind = DiagramMarkerKind.Step,
            PlantUml = "\nhnote across <<stepDelimiter>> #black:<color:white>Given a basket\n\n",
            Timestamp = T0.AddMilliseconds(-5),
        };
        var closing = opening with { IsOverrideStart = false, IsOverrideEnd = true, PlantUml = null, RequestResponseId = Guid.NewGuid(), Timestamp = T0.AddMilliseconds(-4) };
        File.AppendAllLines(capture, [InteractionRecord.FromLog(opening).ToJson(), InteractionRecord.FromLog(closing).ToJson()]);
        var outFile = Path.Combine(_dir, "export-markers.json");
        var @out = new StringWriter();

        var exit = ExportCommand.Run([capture, "--dry-run", "--out", outFile], @out, new StringWriter());

        Assert.Equal(0, exit);
        Assert.Equal("POST", Assert.Single(OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile))).Name);
        Assert.Contains("1 span(s) in 1 trace(s), 2 record(s) skipped", @out.ToString());
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

        using var closed = new ClosedPort();
        var exit = ExportCommand.Run([capture, "--otlp", $"http://localhost:{closed.Port}/v1/traces"], new StringWriter(), err);

        Assert.Equal(1, exit);
        Assert.Contains("failed", err.ToString());
    }

    /// <summary>
    /// The verdict is not in the interaction capture and cannot be - it is written by the runner when the
    /// test ends. `--tests` is how the CLI learns it, the same companion NDJSON `kronikol ingest` reads.
    /// </summary>
    [Fact]
    public void Tests_ndjson_gives_every_span_of_a_test_its_verdict()
    {
        var capture = WriteCapture();
        var tests = WriteTests("failed");
        var outFile = Path.Combine(_dir, "export.json");

        var exit = ExportCommand.Run([capture, "--tests", tests, "--dry-run", "--out", outFile], new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var span = Assert.Single(OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile)));
        Assert.Equal("Failed", span.Attribute("kronikol.test.result"));
    }

    [Fact]
    public void Without_tests_ndjson_no_span_claims_a_verdict()
    {
        var capture = WriteCapture();
        var outFile = Path.Combine(_dir, "export.json");

        ExportCommand.Run([capture, "--dry-run", "--out", outFile], new StringWriter(), new StringWriter());

        var span = Assert.Single(OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile)));
        Assert.Null(span.Attribute("kronikol.test.result"));
    }

    /// <summary>
    /// The status words a runner writes are not the words the report uses, and the two must not disagree:
    /// the CLI maps them through the same <c>FeatureSynthesizer.MapStatus</c> the ingest does.
    /// </summary>
    [Theory]
    [InlineData("passed", "Passed")]
    [InlineData("ok", "Passed")]
    [InlineData("timedOut", "Failed")]
    [InlineData("skipped", "Skipped")]
    public void A_runners_status_word_becomes_the_result_the_report_would_show(string status, string expected)
    {
        var capture = WriteCapture();
        var outFile = Path.Combine(_dir, "export.json");

        ExportCommand.Run([capture, "--tests", WriteTests(status), "--dry-run", "--out", outFile],
            new StringWriter(), new StringWriter());

        var span = Assert.Single(OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile)));
        Assert.Equal(expected, span.Attribute("kronikol.test.result"));
    }

    [Fact]
    public void A_tests_file_inside_an_input_directory_is_not_read_as_interactions()
    {
        // Both files live in _dir and the input is the directory, so the sweep would otherwise pick the
        // tests file up as a capture and report every line of it malformed.
        WriteCapture();
        WriteTests("passed", "tests.ndjson");
        var outFile = Path.Combine(_dir, "export.json");
        var err = new StringWriter();

        var exit = ExportCommand.Run([_dir, "--tests", Path.Combine(_dir, "tests.ndjson"), "--dry-run", "--out", outFile],
            new StringWriter(), err);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("malformed", err.ToString());
        var span = Assert.Single(OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile)));
        Assert.Equal("Passed", span.Attribute("kronikol.test.result"));
    }

    [Fact]
    public void A_missing_tests_file_is_a_runtime_failure_and_says_so()
    {
        var err = new StringWriter();
        var exit = ExportCommand.Run([WriteCapture(), "--tests", Path.Combine(_dir, "nope.ndjson"), "--dry-run"],
            new StringWriter(), err);

        Assert.Equal(1, exit);
        Assert.Contains("Tests file not found", err.ToString());
    }

    private string WriteTests(string status, string name = "tests-run.ndjson")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path,
        [
            $$"""{"event":"testrun","testId":"__run__","status":"started"}""",
            $$"""{"event":"start","testId":"{{TestId}}","testName":"cli › exports"}""",
            $$"""{"event":"end","testId":"{{TestId}}","status":"{{status}}","durationMs":30}""",
        ]);
        return path;
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

/// <summary>
/// A loopback port that stays closed for as long as this handle lives: bound but never listening, so a
/// connection to it is refused, and reserved, so the OS cannot hand it to a stub server another test
/// starts in the meantime. A port merely observed free and then released is neither: on CI a parallel
/// stub collector was given the released port, and an export to an "unreachable" endpoint succeeded
/// against it.
/// </summary>
public sealed class ClosedPort : IDisposable
{
    private readonly System.Net.Sockets.Socket _socket = new(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);

    public int Port { get; }

    public ClosedPort()
    {
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
    }

    public void Dispose() => _socket.Dispose();
}
