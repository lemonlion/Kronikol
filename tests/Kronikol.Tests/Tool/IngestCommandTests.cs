using Kronikol.Ingestion;
using Kronikol.Reports;
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
        // Browser rendering defaults to the worker path.
        Assert.Contains("var WORKERS_REQUESTED = 4;", html);
    }

    [Fact]
    public void Browser_render_workers_option_is_validated_and_reaches_the_report()
    {
        var err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--browser-render-workers", "many"], new StringWriter(), err));
        Assert.Contains("--browser-render-workers needs an integer >= 0", err.ToString());
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--browser-render-workers", "-1"], new StringWriter(), new StringWriter()));
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--browser-render-workers"], new StringWriter(), new StringWriter()));

        const string testId = "0a1b2c3d4e5f60718293a4b5c6d7e8f9";
        var captures = Path.Combine(_dir, "captures-workers");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/health", "web", "web",
            requestContent: "", responseContent: "{\"ok\":true}", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › workers", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var output = Path.Combine(_dir, "out-workers");

        var exit = IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", output, "--browser-render-workers", "0"], new StringWriter(), err);

        Assert.Equal(0, exit);
        var html = File.ReadAllText(Path.Combine(output, "TestRunReport.html"));
        Assert.Contains("var WORKERS_REQUESTED = 0;", html);
        Assert.DoesNotContain("var WORKERS_REQUESTED = 4;", html);
    }

    [Fact]
    public void Note_format_option_is_validated_and_reaches_the_report()
    {
        var err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--note-format", "toml"], new StringWriter(), err));
        Assert.Contains("--note-format needs json or yaml", err.ToString());
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--note-format"], new StringWriter(), new StringWriter()));

        const string testId = "1b2c3d4e5f60718293a4b5c6d7e8f90a";
        var captures = Path.Combine(_dir, "captures-note-format");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/health", "web", "web",
            requestContent: "", responseContent: "{\"ok\":true}", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › note format", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);

        // The flag seeds the browser's starting format: the collapsible-notes script inlined into the report
        // carries the chosen value where its __NOTE_FORMAT_DEFAULT__ token stood.
        var yaml = Path.Combine(_dir, "out-yaml");
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", yaml, "--note-format", "yaml"], new StringWriter(), err));
        var yamlHtml = File.ReadAllText(Path.Combine(yaml, "TestRunReport.html"));
        Assert.Contains("window._noteFormatDefault = 'yaml'", yamlHtml);
        Assert.DoesNotContain("window._noteFormatDefault = 'json'", yamlHtml);

        var json = Path.Combine(_dir, "out-json");
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", json, "--note-format", "json"], new StringWriter(), err));
        Assert.Contains("window._noteFormatDefault = 'json'", File.ReadAllText(Path.Combine(json, "TestRunReport.html")));

        var unset = Path.Combine(_dir, "out-default");
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", unset], new StringWriter(), err));
        // Without the flag, ingest keeps the library's default: YAML from 4.0.0.
        Assert.Contains("window._noteFormatDefault = 'yaml'", File.ReadAllText(Path.Combine(unset, "TestRunReport.html")));

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--note-format <json|yaml>", usage.ToString());
    }

    [Fact]
    public void Headers_option_is_validated_and_reaches_the_report()
    {
        // plans/V4_PLAN.md R7: 4.0.0 starts reports with headers hidden, and ingest had no way to choose either.
        var err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--headers", "maybe"], new StringWriter(), err));
        Assert.Contains("--headers needs shown or hidden", err.ToString());
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--headers"], new StringWriter(), new StringWriter()));

        const string testId = "2c3d4e5f60718293a4b5c6d7e8f90a1b";
        var captures = Path.Combine(_dir, "captures-headers");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/health", "web", "web",
            requestContent: "", responseContent: "{\"ok\":true}", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › headers", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);

        // The flag seeds the page's starting state: the script's flag and every headers button, which say the same.
        string Run(string name, params string[] flag)
        {
            var output = Path.Combine(_dir, name);
            Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", output, .. flag], new StringWriter(), err));
            return File.ReadAllText(Path.Combine(output, "TestRunReport.html"));
        }
        static string[] Buttons(string html) => System.Text.RegularExpressions.Regex
            .Matches(html, "data-toggle=\"headers\" data-shown=\"(true|false)\"").Select(m => m.Groups[1].Value).ToArray();

        // Without the flag, ingest keeps the library's default: hidden from 4.0.0.
        foreach (var hidden in new[] { Run("out-headers-hidden", "--headers", "hidden"), Run("out-headers-unset") })
        {
            Assert.Contains("window._headersHidden = true;", hidden);
            Assert.NotEmpty(Buttons(hidden));
            Assert.All(Buttons(hidden), shown => Assert.Equal("false", shown));
        }

        var shownHtml = Run("out-headers-shown", "--headers", "shown");
        Assert.Contains("window._headersHidden = false;", shownHtml);
        Assert.NotEmpty(Buttons(shownHtml));
        Assert.All(Buttons(shownHtml), shown => Assert.Equal("true", shown));

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--headers <shown|hidden>", usage.ToString());
    }

    /// <summary>
    /// <c>--separate-setup</c> (plans/INGEST_FIDELITY_PLAN.md T1): the Setup partition was unreachable from the command
    /// line, and <c>--phase-from-steps</c>, which said it gave it, only tagged the calls. A Given step with a call, then a
    /// When step with a call: the flag draws the partition around the first, and without it nothing is drawn.
    /// </summary>
    [Fact]
    public void Separate_setup_flag_reaches_the_report()
    {
        const string testId = "5e7a6d4c3b2a19087f6e5d4c3b2a1908";
        var captures = Path.Combine(_dir, "captures-separate-setup");
        Directory.CreateDirectory(captures);
        var (cardRequest, cardResponse) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:5000/cards/1", "cards", "web",
            responseContent: "{\"ok\":true}", statusCode: "200", requestTimestamp: T0.AddMilliseconds(1500), responseTimestamp: T0.AddMilliseconds(1600));
        var (chargeRequest, chargeResponse) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:5000/charges", "psp", "web",
            responseContent: "{\"ok\":true}", statusCode: "200", requestTimestamp: T0.AddMilliseconds(3500), responseTimestamp: T0.AddMilliseconds(3600));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [cardRequest.ToJson(), cardResponse.ToJson(), chargeRequest.ToJson(), chargeResponse.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › charges a card", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "step", TestId = testId, Text = "a saved card", Keyword = "Given", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(1000) }.ToJson(),
            new TestRunRecord { Event = "step", TestId = testId, Text = "the card is charged", Keyword = "When", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(3000) }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) }.ToJson(),
        ]);

        string Diagram(string name, params string[] flags)
        {
            var output = Path.Combine(_dir, name);
            var err = new StringWriter();
            Assert.True(0 == IngestCommand.Run([captures, "--tests", tests, "-o", output, .. flags], new StringWriter(), err), err.ToString());
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
            return ReportPayloadText.Of(json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("diagrams")[0])!;
        }

        var separated = Diagram("out-separate-setup", "--separate-setup");
        Assert.Contains("partition #F6F6F6 Setup", separated);
        var setup = Kronikol.Tests.Ingestion.IngestSetupBoundaryTests.PartitionOf(separated);
        Assert.Contains("/cards/1", setup);
        Assert.DoesNotContain("/charges", setup);

        Assert.DoesNotContain("partition", Diagram("out-no-separate-setup"));
        Assert.DoesNotContain("partition", Diagram("out-phase-from-steps-only", "--phase-from-steps"));

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--separate-setup", usage.ToString());
        // The usage said --phase-from-steps separates setup traffic; the partition is --separate-setup's.
        Assert.DoesNotContain("so setup traffic can be separated", usage.ToString());
    }

    /// <summary>
    /// <c>--source-root</c> (plans/INGEST_FIDELITY_PLAN.md T9): a tests record's absolute <c>sourceFile</c> under it is
    /// written relative to it, so the report carries the repository's path and not the runner's; the default is the
    /// directory the command runs in. The root is a prefix, not a directory this machine must have: a Windows runner's
    /// paths ingest on Linux.
    /// </summary>
    [Fact]
    public void Source_root_option_is_validated_and_makes_source_paths_relative()
    {
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--source-root"], new StringWriter(), new StringWriter()));

        const string testId = "50c7e2a1b3d4f5a6978877665544332a";
        var checkout = Path.Combine(_dir, "checkout");
        var captures = Path.Combine(_dir, "captures-source-root");
        Directory.CreateDirectory(captures);
        Directory.CreateDirectory(checkout);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/health", "web", "web",
            responseContent: "{\"ok\":true}", statusCode: "200", requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › sources", SourceFile = Path.Combine(checkout, "tests", "health.test.ts"), SourceLine = 7, Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var output = Path.Combine(_dir, "out-source-root");

        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", output, "--source-root", checkout], new StringWriter(), new StringWriter()));

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var scenario = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
        Assert.Equal("tests/health.test.ts", scenario.GetProperty("sourceFile").GetString());
        Assert.Equal(7, scenario.GetProperty("sourceLine").GetInt32());

        // A Windows runner's root, named on whichever machine ingests.
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › sources", SourceFile = @"D:\a\repo\repo\tests\health.test.ts", SourceLine = 7, Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var windows = Path.Combine(_dir, "out-source-root-windows");
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", windows, "--source-root", @"D:\a\repo\repo"], new StringWriter(), new StringWriter()));
        using var fromWindows = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(windows, "TestRunReport.json")));
        Assert.Equal("tests/health.test.ts", fromWindows.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("sourceFile").GetString());

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--source-root <dir>", usage.ToString());
    }

    /// <summary>
    /// <c>--payloads compressed</c> writes a large body compressed in place (#85, <c>CompressTestRunReportPayloads</c>);
    /// without the flag ingest writes what the library writes by default. Ingest takes a curated set of flags, so
    /// without this one an ingested report could not have them.
    /// </summary>
    [Fact]
    public void Payloads_option_is_validated_and_reaches_the_report()
    {
        var err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--payloads", "zip"], new StringWriter(), err));
        Assert.Contains("--payloads needs plain or compressed", err.ToString());
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--payloads"], new StringWriter(), new StringWriter()));

        const string testId = "2b2c3d4e5f60718293a4b5c6d7e8f90a";
        var captures = Path.Combine(_dir, "captures-payloads");
        Directory.CreateDirectory(captures);
        var large = "{\"items\":[" + string.Join(",", Enumerable.Range(0, ReportPayloads.Threshold / 20).Select(i => $"{{\"sku\":\"W-{i}\",\"qty\":{i}}}")) + "]}";
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/stock", "web", "web",
            requestContent: "", responseContent: large, statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › payloads", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);

        // Without the flag, ingest keeps the library's default: compressed from 4.0.0.
        foreach (var (folder, flag) in new[] { ("out-compressed", new[] { "--payloads", "compressed" }), ("out-unset", Array.Empty<string>()) })
        {
            var compressed = Path.Combine(_dir, folder);
            Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", compressed, .. flag], new StringWriter(), err));
            var compressedJson = File.ReadAllText(Path.Combine(compressed, "TestRunReport.json"));
            Assert.Contains("\"$z\"", compressedJson, StringComparison.Ordinal);
            Assert.Contains("\"formatVersion\": 2", compressedJson, StringComparison.Ordinal);
        }

        var plain = Path.Combine(_dir, "out-plain");
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", plain, "--payloads", "plain"], new StringWriter(), err));
        var plainJson = File.ReadAllText(Path.Combine(plain, "TestRunReport.json"));
        Assert.DoesNotContain("\"$z\"", plainJson, StringComparison.Ordinal);
        Assert.Contains("\"formatVersion\": 1", plainJson, StringComparison.Ordinal);

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--payloads <plain|compressed>", usage.ToString());
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

        // A file of nothing but garbage: the torn lines are skipped, so there is simply nothing to report.
        var bad = Path.Combine(_dir, "bad.ndjson");
        File.WriteAllText(bad, "{not json\n");
        err = new StringWriter();
        Assert.Equal(1, IngestCommand.Run([bad, "-o", Path.Combine(_dir, "o")], new StringWriter(), err));
        Assert.Contains("Nothing to report", err.ToString());

        // --strict brings back the hard failure, for a pipeline that wants a garbage producer to be loud.
        err = new StringWriter();
        Assert.Equal(1, IngestCommand.Run([bad, "--strict", "-o", Path.Combine(_dir, "o")], new StringWriter(), err));
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

    [Fact]
    public void Ingest_command_carries_host_diagnostics_into_the_report()
    {
        const string testId = "cafe651916cd43dd8448eb211c80319d";
        var captures = Path.Combine(_dir, "captures");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/overview", "graphql", "web",
            statusCode: "200", requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(30));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › diagnostics", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var output = Path.Combine(_dir, "out");
        var @out = new StringWriter();
        var err = new StringWriter();

        var exit = IngestCommand.Run(
        [
            captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", output,
            "--diagnostic", "CaptureDegraded:tap-di-redis: decoding disabled on 1 connection(s)",
            "--diagnostic", "free text without a kind",
            "--diagnostic", "NoSuchKind: still free text",
        ], @out, err);

        Assert.Equal(0, exit);
        var printed = @out.ToString();
        Assert.Contains("CaptureDegraded: tap-di-redis: decoding disabled on 1 connection(s)", printed);
        Assert.Contains("Other: free text without a kind", printed);
        Assert.Contains("Other: NoSuchKind: still free text", printed);

        // The HTML section is off unless the run asks for it; the data file carries the diagnostic either way.
        var html = File.ReadAllText(Path.Combine(output, "TestRunReport.html"));
        Assert.DoesNotContain("<summary>Report diagnostics (", html);
        Assert.DoesNotContain("tap-di-redis: decoding disabled on 1 connection(s)", html);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        Assert.Contains(json.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("kind").GetString() == "CaptureDegraded");

        var shown = Path.Combine(_dir, "shown");
        Assert.Equal(0, IngestCommand.Run(
        [
            captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", shown, "--diagnostics-section",
            "--diagnostic", "CaptureDegraded:tap-di-redis: decoding disabled on 1 connection(s)",
        ], new StringWriter(), new StringWriter()));
        var shownHtml = File.ReadAllText(Path.Combine(shown, "TestRunReport.html"));
        Assert.Contains("<summary>Report diagnostics (", shownHtml);
        Assert.Contains("tap-di-redis: decoding disabled on 1 connection(s)", shownHtml);

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--diagnostic <kind>:<msg>", usage.ToString());
        Assert.Contains("--diagnostics-section", usage.ToString());
    }

    [Fact]
    public void Ingest_command_rejects_a_diagnostic_without_a_message()
    {
        var err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--diagnostic", "CaptureDegraded:"], new StringWriter(), err));
        Assert.Contains("--diagnostic needs", err.ToString());

        err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--diagnostic"], new StringWriter(), err));
        Assert.Contains("Missing value", err.ToString());
    }

    [Theory]
    [InlineData("CaptureDegraded:tap: gave up", DiagnosticKind.CaptureDegraded, "tap: gave up")]
    [InlineData("capturedegraded: tap: gave up ", DiagnosticKind.CaptureDegraded, "tap: gave up")]
    [InlineData("MalformedLine:file:12", DiagnosticKind.MalformedLine, "file:12")]
    [InlineData("just a message", DiagnosticKind.Other, "just a message")]
    [InlineData("NotAKind: message", DiagnosticKind.Other, "NotAKind: message")]
    [InlineData(":leading colon", DiagnosticKind.Other, ":leading colon")]
    public void Diagnostic_values_parse_as_kind_colon_message(string value, DiagnosticKind kind, string message)
    {
        Assert.True(IngestCommand.TryParseDiagnostic(value, out var entry));
        Assert.Equal(kind, entry.Kind);
        Assert.Equal(message, entry.Message);
        Assert.Null(entry.ScenarioId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Other:")]
    [InlineData("CaptureDegraded:   ")]
    public void Diagnostic_values_without_a_message_are_refused(string value)
    {
        Assert.False(IngestCommand.TryParseDiagnostic(value, out _));
    }

    [Fact]
    public void Render_local_is_refused_as_a_usage_error_the_command_line_can_act_on()
    {
        // PlantUmlRendering.Local needs a LocalDiagramRenderer delegate, which only the library API can set;
        // through the tool it used to escape as an unhandled InvalidOperationException after the
        // "Ingesting …" lines, with no output directory and the runtime's crash status.
        var output = Path.Combine(_dir, "out-local");
        var err = new StringWriter();

        var exit = IngestCommand.Run(["x.ndjson", "-o", output, "--render", "local"], new StringWriter(), err);

        Assert.Equal(2, exit);
        Assert.Contains("--render local needs a LocalDiagramRenderer delegate, which only the library API can supply.", err.ToString());
        Assert.Contains("Use nodejs for offline SVG (needs node on PATH) or browserjs (the default).", err.ToString());
        Assert.False(Directory.Exists(output));

        // The value stays accepted by the parser (removing an accepted value is v4's), and every list of
        // modes says the same thing about it.
        Assert.True(IngestCommand.TryParseRender("local", out var local));
        Assert.Equal(PlantUmlRendering.Local, local);
        err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--render", "crayon"], new StringWriter(), err));
        Assert.Contains("expected browserjs|nodejs|server; local is library-only", err.ToString());
        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("local is library-only", usage.ToString());
    }

    [Fact]
    public void The_specification_files_are_blank_after_a_failed_run_and_the_command_says_so()
    {
        const string testId = "b1ank651916cd43dd8448eb211c80319c";
        var captures = Path.Combine(_dir, "captures-blank");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/health", "web", "web",
            requestContent: "", responseContent: "{\"ok\":true}", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");

        // A red run: both specification files are written blank (the rule Generated-Reports documents for
        // in-process runs), and the command says so instead of leaving two empty files unexplained.
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › fails", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "failed", Error = "expected 1 got 2", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var red = Path.Combine(_dir, "out-red");
        var @out = new StringWriter();
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", red], @out, new StringWriter()));
        Assert.Equal(0, new FileInfo(Path.Combine(red, "Specifications.html")).Length);
        Assert.Equal(0, new FileInfo(Path.Combine(red, "Specifications.yml")).Length);
        Assert.Contains("Specifications.html and Specifications.yml are blank: 1 of 1 scenario(s) failed, and the specification is only published from a green run.", @out.ToString());

        // A green run: both populated, and nothing to say.
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › passes", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var green = Path.Combine(_dir, "out-green");
        @out = new StringWriter();
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", green], @out, new StringWriter()));
        Assert.True(new FileInfo(Path.Combine(green, "Specifications.html")).Length > 0);
        Assert.True(new FileInfo(Path.Combine(green, "Specifications.yml")).Length > 0);
        Assert.DoesNotContain("are blank", @out.ToString());
    }

    [Fact]
    public void An_ingest_does_not_warn_that_activity_diagrams_will_be_empty()
    {
        // The ingest path turns internal-flow tracking off (there are no in-process spans to show), so the
        // generator's "InternalFlowSpanStore has 0 spans" warning is about a feature this run never had.
        const string testId = "f10w651916cd43dd8448eb211c80319c";
        var captures = Path.Combine(_dir, "captures-flow");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/health", "web", "web",
            requestContent: "", responseContent: "{\"ok\":true}", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › flow", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var output = Path.Combine(_dir, "out-flow");

        // The generator prints its diagnostics to the console, not the command's writer; captured for this
        // thread only, because the console is process-wide and those lines name no directory to scope by.
        string console;
        using (var scoped = new Kronikol.Tests.Reports.ThreadScopedConsole())
        {
            Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", output], new StringWriter(), new StringWriter()));
            console = scoped.Text;
        }

        Assert.Contains("Report diagnostics:", console); // the capture saw this run's diagnostics at all
        Assert.DoesNotContain("InternalFlowSpanStore", console);
    }

    // ─── --spans (plans/INGEST_FIDELITY_PLAN.md S5: T18, T20) ───

    private const string SpikeTrace = "eb7b756166d44b0d1306973d5a79985f";
    private static readonly DateTimeOffset SpikeSecond = DateTimeOffset.FromUnixTimeMilliseconds(1790755298000);
    private static readonly string SpikeSpans = Path.Combine(AppContext.BaseDirectory, "TestData", "Ingest", "otel-jest-v2.spans.jsonl");

    /// <summary>The spike's GraphQL call and its tests file, in <paramref name="name"/> under the test's directory.</summary>
    private (string Captures, string Tests) SpikeCapture(string name)
    {
        const string testId = "c2a7e3d1b4f6058a9c1d2e3f4a5b6c7d";
        var captures = Path.Combine(_dir, name);
        Directory.CreateDirectory(captures);
        var (request, response) = InteractionRecord.Pair(testId, null, "POST", "http://payments.local/graphql", "Payments GraphQL", "Jest",
            responseContent: "{\"data\":{}}", statusCode: "200",
            requestTimestamp: SpikeSecond.AddMilliseconds(365), responseTimestamp: SpikeSecond.AddMilliseconds(449),
            activityTraceId: SpikeTrace, activitySpanId: "c6103b1e3b3c954e");
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [request.ToJson(), response.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "charges a card", Timestamp = SpikeSecond.AddMilliseconds(300) }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", Timestamp = SpikeSecond.AddMilliseconds(500) }.ToJson(),
        ]);
        return (captures, tests);
    }

    [Fact]
    public void Spans_option_draws_internal_flow_from_otlp_json_lines()
    {
        var (captures, tests) = SpikeCapture("captures-spans");
        var output = Path.Combine(_dir, "out-spans");
        var @out = new StringWriter();
        var err = new StringWriter();

        string console;
        using (var scoped = new Kronikol.Tests.Reports.ThreadScopedConsole())
        {
            Assert.True(0 == IngestCommand.Run([captures, "--tests", tests, "--spans", SpikeSpans, "-o", output], @out, err), err.ToString());
            console = scoped.Text;
        }

        Assert.Contains("Spans: 16 span(s) from 1 file(s)", @out.ToString());
        var html = File.ReadAllText(Path.Combine(output, "TestRunReport.html"));
        Assert.Contains("id=\"iflow-segments\"", html);
        Assert.Contains("Spans supplied to the ingest: 16 span(s).", console);
        Assert.DoesNotContain("InternalFlowSpanStore", console);
        // The start-up span is in no call's flow, and the command prints the diagnostic that says so.
        Assert.Contains("1 supplied span(s) are in no call's internal flow", @out.ToString());

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--spans <file|dir|glob>", usage.ToString());
    }

    [Fact]
    public void A_spans_file_inside_an_input_directory_is_read_as_spans_only()
    {
        var (captures, tests) = SpikeCapture("captures-spans-inside");
        var spans = Path.Combine(captures, "spans.jsonl");
        File.Copy(SpikeSpans, spans);
        var @out = new StringWriter();
        var err = new StringWriter();

        Assert.True(0 == IngestCommand.Run([captures, "--tests", tests, "--spans", spans, "-o", Path.Combine(_dir, "out-spans-inside")], @out, err), err.ToString());

        Assert.Contains("Ingesting 1 capture file(s):", @out.ToString());
        Assert.DoesNotContain("malformed line", @out.ToString());
        Assert.Contains("Spans: 16 span(s) from 1 file(s)", @out.ToString());
    }

    [Fact]
    public void A_spans_directory_gives_only_the_files_that_hold_spans()
    {
        // One folder for everything a Jest harness writes: --spans names it, and takes the OTLP/JSON files only, where its
        // patterns alone would have taken the interaction captures and the tests file as span files too.
        var (captures, tests) = SpikeCapture("captures-one-folder");
        File.Copy(SpikeSpans, Path.Combine(captures, "spans-1.jsonl"));
        var @out = new StringWriter();
        var err = new StringWriter();

        Assert.True(0 == IngestCommand.Run([captures, "--tests", tests, "--spans", captures, "-o", Path.Combine(_dir, "out-one-folder")], @out, err), err.ToString());

        Assert.Contains("Ingesting 1 capture file(s):", @out.ToString());
        Assert.Contains("Spans: 16 span(s) from 1 file(s)", @out.ToString());
        Assert.DoesNotContain("malformed line", @out.ToString());
        Assert.DoesNotContain("Skipped", @out.ToString());
    }

    [Fact]
    public void A_stray_otlp_file_among_the_inputs_is_skipped_with_one_line_of_advice()
    {
        var (captures, tests) = SpikeCapture("captures-stray-spans");
        var stray = Path.Combine(captures, "spans.jsonl");
        File.Copy(SpikeSpans, stray);
        var @out = new StringWriter();
        var err = new StringWriter();

        Assert.True(0 == IngestCommand.Run([captures, "--tests", tests, "-o", Path.Combine(_dir, "out-stray-spans")], @out, err), err.ToString());

        var advice = @out.ToString().Split('\n').Where(l => l.Contains("--spans", StringComparison.Ordinal)).ToArray();
        Assert.Equal($"Skipped {stray}: it holds OpenTelemetry spans (resourceSpans), not interaction records; pass it with --spans to draw them as internal flow.",
            Assert.Single(advice).TrimEnd('\r'));
        Assert.Contains("Ingesting 1 capture file(s):", @out.ToString());
        Assert.DoesNotContain("malformed line", @out.ToString());
    }

    [Fact]
    public void A_torn_span_line_is_counted_and_strict_fails_on_it()
    {
        var (captures, tests) = SpikeCapture("captures-torn-spans");
        var spans = Path.Combine(_dir, "torn-spans.jsonl");
        File.WriteAllLines(spans, [File.ReadAllText(SpikeSpans).TrimEnd(), "{\"resourceSpans\":[{\"scopeSpans\""]);

        var @out = new StringWriter();
        var err = new StringWriter();
        Assert.True(0 == IngestCommand.Run([captures, "--tests", tests, "--spans", spans, "-o", Path.Combine(_dir, "out-torn")], @out, err), err.ToString());
        Assert.Contains("1 malformed line(s) skipped:", @out.ToString());
        Assert.Contains($"{spans}:2: ", @out.ToString());
        Assert.Contains("Spans: 16 span(s) from 1 file(s)", @out.ToString());

        err = new StringWriter();
        Assert.Equal(1, IngestCommand.Run([captures, "--tests", tests, "--spans", spans, "--strict", "-o", Path.Combine(_dir, "out-torn-strict")], new StringWriter(), err));
        Assert.Contains($"Failed to read a span file: {spans}:2: ", err.ToString());
    }

    [Fact]
    public void Spans_option_usage_errors()
    {
        var (captures, _) = SpikeCapture("captures-spans-usage");

        var err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run([captures, "--spans"], new StringWriter(), err));
        Assert.Contains("Missing value for --spans", err.ToString());

        var empty = Path.Combine(_dir, "no-spans-here");
        Directory.CreateDirectory(empty);
        err = new StringWriter();
        Assert.Equal(1, IngestCommand.Run([captures, "--spans", empty, "-o", Path.Combine(_dir, "out-no-spans")], new StringWriter(), err));
        Assert.Contains("No span files found", err.ToString());
    }
}
