using Kronikol.Ingestion;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

[Collection("DiagramsFetcher")]
public class IngestCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-ingest-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    public IngestCommandTests()
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
    public void Ingest_command_round_trips_fixture_ndjson_to_a_report()
    {
        const string testId = "cafe651916cd43dd8448eb211c80319c";
        var captures = Path.Combine(_dir, "captures");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:8081/sidekick", "graphql", "web",
            requestContent: "{}", responseContent: "{}", statusCode: "200",
            requestHeaders: [new InteractionHeader("Authorization", "Bearer cli-secret"), new InteractionHeader("Accept", "*/*")],
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(30));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › renders", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 100, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var output = Path.Combine(_dir, "out");
        var @out = new StringWriter();
        var err = new StringWriter();

        var exit = IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", output, "-t", "CLI ingest"], @out, err);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(output, "TestRunReport.html")), "Stderr: " + err);
        var html = File.ReadAllText(Path.Combine(output, "TestRunReport.html"));
        Assert.Contains("CLI ingest", html);
        Assert.Contains("cli › renders", html);
        Assert.Contains("web.ndjson", @out.ToString());
        Assert.DoesNotContain("tests.ndjson", @out.ToString().Split('\n').First(l => l.Contains("Ingesting")));
        // Secure by default: credential header redacted at ingest.
        Assert.DoesNotContain("cli-secret", File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
    }

    [Fact]
    public void Ingest_command_usage_errors()
    {
        var err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run([], new StringWriter(), err));
        Assert.Contains("No inputs", err.ToString());

        err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--bogus"], new StringWriter(), err));
        Assert.Contains("Unknown option", err.ToString());

        err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--render", "crayon"], new StringWriter(), err));
        Assert.Contains("Unknown render mode", err.ToString());

        Assert.Equal(0, IngestCommand.Run(["--help"], new StringWriter(), err));
    }

    [Fact]
    public void Ingest_command_reports_missing_inputs_and_malformed_lines()
    {
        var err = new StringWriter();
        Assert.Equal(1, IngestCommand.Run([Path.Combine(_dir, "missing-dir")], new StringWriter(), err));
        Assert.Contains("No matching capture files", err.ToString());

        var bad = Path.Combine(_dir, "bad.ndjson");
        File.WriteAllText(bad, "{not json\n");
        err = new StringWriter();
        Assert.Equal(1, IngestCommand.Run([bad, "-o", Path.Combine(_dir, "o")], new StringWriter(), err));
        Assert.Contains("Failed to read", err.ToString());
    }

    [Fact]
    public void Render_mode_parsing()
    {
        Assert.True(IngestCommand.TryParseRender("NodeJs", out var node));
        Assert.Equal(PlantUmlRendering.NodeJs, node);
        Assert.True(IngestCommand.TryParseRender("server", out var server));
        Assert.Equal(PlantUmlRendering.Server, server);
        Assert.False(IngestCommand.TryParseRender("x", out _));
    }

    [Fact]
    public void Fold_unknown_and_chronological_flags_reach_the_pipeline()
    {
        const string testId = "cafe651916cd43dd8448eb211c80319c";
        var captures = Path.Combine(_dir, "captures");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://a/known", "A", "Test", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddSeconds(1));
        var (wReq, wResp) = InteractionRecord.Pair("warm-up-trace", null, "GET", "http://a/warm", "A", "Test", statusCode: "200",
            requestTimestamp: T0.AddSeconds(2), responseTimestamp: T0.AddSeconds(3));
        File.WriteAllLines(Path.Combine(captures, "c.ndjson"), [req.ToJson(), resp.ToJson(), wReq.ToJson(), wResp.ToJson()]);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › known", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var output = Path.Combine(_dir, "out");
        var @out = new StringWriter();
        var err = new StringWriter();

        var exit = IngestCommand.Run(
            [captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", output, "--fold-unknown", "Traffic outside any test", "--chronological"],
            @out, err);

        Assert.Equal(0, exit);
        Assert.Contains("into 2 scenario(s)", @out.ToString());
        Assert.Contains("Traffic outside any test", File.ReadAllText(Path.Combine(output, "TestRunReport.html")));
        Assert.Contains("--fold-unknown", Usage());
        Assert.Contains("--chronological", Usage());

        static string Usage()
        {
            var w = new StringWriter();
            IngestCommand.PrintUsage(w);
            return w.ToString();
        }
    }
}
