using System.Diagnostics;
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
    public void Ingest_finds_the_history_ledger_from_where_it_is_run_not_from_the_tools_folder()
    {
        // plans/PHASE_FROM_STEPS_PLAN.md section 8.1 (F6): the ledger was looked for above the tool's own folder first,
        // so a tool built inside a checkout read and appended to that checkout's ledger whatever directory it was run
        // from. This test's bin is inside the repository's checkout, so the tool beside it reproduces that.
        var project = Path.Combine(_dir, "project");
        Directory.CreateDirectory(Path.Combine(project, ".kronikol"));
        var captures = PhaseRepro("ledger", stepDurationMs: 10);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = project,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "Kronikol.Tool.dll"), "ingest", captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", Path.Combine(project, "Reports") })
            start.ArgumentList.Add(arg);
        // History on, and a local run: on CI the ledger is left to the history action, so nothing would be written.
        foreach (var name in start.Environment.Keys.ToArray())
        {
            if (name is "KRONIKOL_HISTORY" or "CI" or "TF_BUILD"
                || name.StartsWith("GITHUB_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("RUNNER_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("BUILD_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("SYSTEM_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("AGENT_", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(name);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "kronikol ingest did not finish");
        Assert.Equal(0, process.ExitCode);

        var ledger = Path.Combine(project, ".kronikol", "history.jsonl");
        Assert.True(File.Exists(ledger), "No ledger above the directory the ingest ran in; it was looked for elsewhere. " + output.Result + error.Result);
        Assert.NotEmpty(File.ReadAllLines(ledger).Where(l => l.Length > 0));
    }

    private const string PhaseLinePrefix = "--phase-from-steps: ";

    [Fact]
    public void Ingest_prints_the_phase_count_as_a_line_after_replayed()
    {
        // #130: the count was a diagnostic of kind Other, printed among the diagnostics and kept in every query answer.
        var captures = PhaseRepro("phase-line", stepDurationMs: 10);
        var @out = new StringWriter();

        Assert.Equal(0, IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", Path.Combine(_dir, "out-phase-line"), "--phase-from-steps"], @out, new StringWriter()));

        var lines = @out.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var replayed = Array.FindIndex(lines, l => l.StartsWith("Replayed ", StringComparison.Ordinal));
        Assert.True(replayed >= 0, @out.ToString());
        Assert.Equal(PhaseLinePrefix + "4 interaction record(s) took the phase of the step their call started in.", lines[replayed + 1]);
        Assert.DoesNotContain(lines, l => l.TrimStart().StartsWith("Other:", StringComparison.Ordinal));
    }

    [Fact]
    public void Ingest_prints_a_phase_count_of_zero()
    {
        // A step with no durationMs is an instant, so calls made after it fall in no window: zero is the count worth seeing.
        var captures = PhaseRepro("phase-zero", stepDurationMs: null, firstCallAtMs: 5);
        var @out = new StringWriter();

        Assert.Equal(0, IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", Path.Combine(_dir, "out-phase-zero"), "--phase-from-steps"], @out, new StringWriter()));

        var lines = @out.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains(PhaseLinePrefix + "0 interaction record(s) took the phase of the step their call started in.", lines);
    }

    [Fact]
    public void Ingest_without_phase_from_steps_prints_no_phase_line()
    {
        var captures = PhaseRepro("phase-off", stepDurationMs: 10);
        var @out = new StringWriter();

        Assert.Equal(0, IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", Path.Combine(_dir, "out-phase-off")], @out, new StringWriter()));

        Assert.DoesNotContain(PhaseLinePrefix, @out.ToString());
        Assert.Contains("Replayed ", @out.ToString());
    }

    [Fact]
    public void The_usage_says_a_response_takes_its_requests_phase()
    {
        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("a response takes its request's phase", usage.ToString());
    }

    private const string WindowLinePrefix = "--attribute-by-window: ";

    [Fact]
    public void Ingest_prints_the_window_attribution_count_as_a_line_after_replayed()
    {
        var captures = PhaseRepro("window-line", stepDurationMs: 10, callTestId: "session");
        var @out = new StringWriter();

        Assert.Equal(0, IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", Path.Combine(_dir, "out-window-line"), "--attribute-by-window", "session"], @out, new StringWriter()));

        var lines = @out.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var replayed = Array.FindIndex(lines, l => l.StartsWith("Replayed ", StringComparison.Ordinal));
        Assert.True(replayed >= 0, @out.ToString());
        Assert.Equal(WindowLinePrefix + "4 interaction record(s) attributed to a test by time window.", lines[replayed + 1]);
        Assert.DoesNotContain(lines, l => l.Contains("UnattributedInteractions", StringComparison.Ordinal));
    }

    [Fact]
    public void Ingest_prints_a_window_attribution_count_of_zero()
    {
        // Every call already names its test, so the pass has nothing to do; zero says it ran.
        var captures = PhaseRepro("window-zero", stepDurationMs: 10);
        var @out = new StringWriter();

        Assert.Equal(0, IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", Path.Combine(_dir, "out-window-zero"), "--attribute-by-window"], @out, new StringWriter()));

        Assert.Contains(WindowLinePrefix + "0 interaction record(s) attributed to a test by time window.", @out.ToString().Split('\n').Select(l => l.TrimEnd('\r')));
    }

    [Fact]
    public void Ingest_without_attribute_by_window_prints_no_window_line()
    {
        var captures = PhaseRepro("window-off", stepDurationMs: 10);
        var @out = new StringWriter();

        Assert.Equal(0, IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", Path.Combine(_dir, "out-window-off")], @out, new StringWriter()));

        Assert.DoesNotContain(WindowLinePrefix, @out.ToString());
    }

    [Fact]
    public void The_usage_says_window_attribution_prints_its_count()
    {
        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("print how many it attributed", usage.ToString());
    }

    /// <summary>
    /// #130's repro as files: one test whose When step (10 ms, or an instant with no duration) makes two calls, the second
    /// a database read answered 2 s later.
    /// </summary>
    private string PhaseRepro(string name, int? stepDurationMs, double firstCallAtMs = 1, string? callTestId = null)
    {
        const string testId = "places-an-order";
        var captures = Path.Combine(_dir, "captures-" + name);
        Directory.CreateDirectory(captures);
        var (aRequest, aResponse) = InteractionRecord.Pair(callTestId ?? testId, null, "POST", "http://localhost:8081/orders", "api", "web",
            statusCode: "201", requestTimestamp: T0.AddMilliseconds(firstCallAtMs), responseTimestamp: T0.AddMilliseconds(firstCallAtMs + 7));
        var (bRequest, bResponse) = InteractionRecord.Pair(callTestId ?? testId, null, "GET", "http://localhost:5432/orders/1", "db", "web",
            statusCode: "200", requestTimestamp: T0.AddMilliseconds(firstCallAtMs + 2), responseTimestamp: T0.AddSeconds(2));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [aRequest.ToJson(), bRequest.ToJson(), aResponse.ToJson(), bResponse.ToJson()]);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "orders › places an order", Feature = "orders.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "step", TestId = testId, Keyword = "When", Text = "the order is placed", DurationMs = stepDurationMs, Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 10, Timestamp = T0.AddMilliseconds(10) }.ToJson(),
        ]);
        return captures;
    }

    [Fact]
    public void The_pointer_names_only_the_files_this_ingest_wrote()
    {
        // A Failures.md this ingest cannot replace, left by something earlier. No report of this run sits
        // beside it, so no rotation moves it. Until 4.5.1 the pointer named the files that existed, so it
        // handed the reader the earlier digest under this ingest's heading.
        var output = Path.Combine(_dir, "out-stale");
        Directory.CreateDirectory(output);
        var stale = Path.Combine(output, "Failures.md");
        const string earlier = "# Failures — from an earlier run\n";
        File.WriteAllText(stale, earlier);
        File.SetAttributes(stale, FileAttributes.ReadOnly);
        try
        {
            const string testId = "5eed651916cd43dd8448eb211c80319c";
            var captures = Path.Combine(_dir, "captures-stale");
            Directory.CreateDirectory(captures);
            var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/health", "api", "web",
                statusCode: "200", requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(30));
            File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
            File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
            [
                new TestRunRecord { Event = "start", TestId = testId, TestName = "health › answers", Feature = "health.spec.ts", Timestamp = T0 }.ToJson(),
                new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 100, Timestamp = T0.AddSeconds(1) }.ToJson(),
            ]);
            var @out = new StringWriter();

            var exit = IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", output], @out, new StringWriter());

            Assert.SkipWhen(ReportHtml.Read(stale) != earlier, "this user writes through a read-only attribute (root), so the digest was replaced");
            Assert.Equal(0, exit);
            var pointer = @out.ToString().Split('\n').Single(l => l.StartsWith("Kronikol: reports written to ", StringComparison.Ordinal));
            Assert.Contains("TestRunReport.html", pointer);
            Assert.DoesNotContain("Failures.md", pointer);
        }
        finally
        {
            File.SetAttributes(stale, FileAttributes.Normal);
        }
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
        var html = ReportHtml.Read(Path.Combine(output, "TestRunReport.html"));
        // Written compressed (plans/HTML_COMPRESSION_PLAN.md R1, tests 1 and 7): no gzip in the page frame but the search
        // index, and every diagram token resolves to its source.
        var file = File.ReadAllText(Path.Combine(output, "TestRunReport.html"));
        Assert.True(Kronikol.Reports.HtmlReportFile.IsCompressed(file));
        var frames = Kronikol.Reports.CompressedPage.Read(file);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(frames.Page, "H4sI[A-Za-z0-9+/]{20,}"));
        Assert.True(frames.DiagramCount >= 1);
        Assert.All(Enumerable.Range(0, frames.DiagramCount), n => Assert.StartsWith("@startuml", frames.Diagram(n), StringComparison.Ordinal));
        Assert.Contains("CLI ingest", html);
        Assert.Contains("cli › renders", html);
        Assert.Contains("web.ndjson", @out.ToString());
        Assert.DoesNotContain("tests.ndjson", @out.ToString().Split('\n').First(l => l.Contains("Ingesting")));
        // Secure by default: credential header redacted at ingest.
        Assert.DoesNotContain("cli-secret", ReportFile.ReadAny(Path.Combine(output, "TestRunReport.json")));
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
        var html = ReportHtml.Read(Path.Combine(output, "TestRunReport.html"));
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
        var yamlHtml = ReportHtml.Read(Path.Combine(yaml, "TestRunReport.html"));
        Assert.Contains("window._noteFormatDefault = 'yaml'", yamlHtml);
        Assert.DoesNotContain("window._noteFormatDefault = 'json'", yamlHtml);

        var json = Path.Combine(_dir, "out-json");
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", json, "--note-format", "json"], new StringWriter(), err));
        Assert.Contains("window._noteFormatDefault = 'json'", ReportHtml.Read(Path.Combine(json, "TestRunReport.html")));

        var unset = Path.Combine(_dir, "out-default");
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", unset], new StringWriter(), err));
        // Without the flag, ingest keeps the library's default: YAML from 4.0.0.
        Assert.Contains("window._noteFormatDefault = 'yaml'", ReportHtml.Read(Path.Combine(unset, "TestRunReport.html")));

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
            return ReportHtml.Read(Path.Combine(output, "TestRunReport.html"));
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
            using var json = System.Text.Json.JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(output, "TestRunReport.json")));
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

        using var json = System.Text.Json.JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(output, "TestRunReport.json")));
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
        using var fromWindows = System.Text.Json.JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(windows, "TestRunReport.json")));
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

        // Without the flag, ingest keeps the library's default: compressed from 4.0.0. The rule is the JSON file's (a .krkl
        // holds every payload as text), so each ingest here asks for the JSON.
        foreach (var (folder, flag) in new[] { ("out-compressed", new[] { "--payloads", "compressed" }), ("out-unset", Array.Empty<string>()) })
        {
            var compressed = Path.Combine(_dir, folder);
            Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", compressed, "--data-file", "json", .. flag], new StringWriter(), err));
            var compressedJson = ReportHtml.Read(Path.Combine(compressed, "TestRunReport.json"));
            Assert.Contains("\"$z\"", compressedJson, StringComparison.Ordinal);
            Assert.Contains("\"formatVersion\": 2", compressedJson, StringComparison.Ordinal);
        }

        var plain = Path.Combine(_dir, "out-plain");
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", plain, "--data-file", "json", "--payloads", "plain"], new StringWriter(), err));
        var plainJson = ReportHtml.Read(Path.Combine(plain, "TestRunReport.json"));
        Assert.DoesNotContain("\"$z\"", plainJson, StringComparison.Ordinal);
        Assert.Contains("\"formatVersion\": 1", plainJson, StringComparison.Ordinal);

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--payloads <plain|compressed>", usage.ToString());
        // Obsolete from 5.0.0 with the option it sets: a .krkl, the default data file, holds every payload as text.
        var help = usage.ToString();
        var payloadsHelp = help[help.IndexOf("--payloads <", StringComparison.Ordinal)..help.IndexOf("--data-file <", StringComparison.Ordinal)];
        Assert.Contains("(--data-file json)", payloadsHelp, StringComparison.Ordinal);
        Assert.Contains("removed in v6", payloadsHelp, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--data-file krkl</c> writes the report's data file compressed whole, <c>TestRunReport.krkl</c>, in place of the
    /// JSON (<c>CompressTestRunReport</c>, plans/REPORT_FORMAT_PLAN.md section 5, R4 item 7): every payload as text whatever
    /// <c>--payloads</c> says, and the pointer ingest prints names the file it wrote.
    /// </summary>
    [Fact]
    public void Data_file_option_is_validated_and_writes_a_krkl()
    {
        var err = new StringWriter();
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--data-file", "zip"], new StringWriter(), err));
        Assert.Contains("--data-file needs json or krkl", err.ToString());
        Assert.Equal(2, IngestCommand.Run(["x.ndjson", "--data-file"], new StringWriter(), new StringWriter()));

        const string testId = "3c3d4e5f60718293a4b5c6d7e8f90a1b";
        var captures = Path.Combine(_dir, "captures-data-file");
        Directory.CreateDirectory(captures);
        var large = "{\"items\":[" + string.Join(",", Enumerable.Range(0, ReportPayloads.Threshold / 20).Select(i => $"{{\"sku\":\"W-{i}\",\"qty\":{i}}}")) + "]}";
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/stock", "web", "web",
            requestContent: "", responseContent: large, statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › data file", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);

        var krkl = Path.Combine(_dir, "out-krkl");
        var output = new StringWriter();
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", krkl, "--data-file", "krkl", "--payloads", "compressed"], output, err));

        Assert.False(File.Exists(Path.Combine(krkl, "TestRunReport.json")), "the JSON was written beside the .krkl");
        var decoded = ReportFile.Read(Path.Combine(krkl, "TestRunReport.krkl"));
        Assert.StartsWith("{\"formatVersion\":3,", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("\"$z\"", decoded, StringComparison.Ordinal);
        Assert.Contains("W-1", decoded, StringComparison.Ordinal);
        Assert.Contains("TestRunReport.krkl", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("TestRunReport.krkl", RunManifest.TryRead(krkl)!.Files);

        var json = Path.Combine(_dir, "out-json");
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", json, "--data-file", "json"], new StringWriter(), err));
        Assert.True(File.Exists(Path.Combine(json, "TestRunReport.json")));
        Assert.False(File.Exists(Path.Combine(json, "TestRunReport.krkl")));

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--data-file <json|krkl>", usage.ToString());
    }

    /// <summary>
    /// <c>--schema</c> writes the data file's schema beside it, <c>TestRunReport.schema.json</c>
    /// (<c>GenerateTestRunReportSchema</c>), which from 5.0.0 a run writes only when asked: without the flag an ingest
    /// writes none. Nothing in Kronikol reads it; it is for a consumer who validates the data file.
    /// </summary>
    [Fact]
    public void Schema_option_writes_the_schema_beside_the_report_and_without_it_there_is_none()
    {
        const string testId = "4d4e5f60718293a4b5c6d7e8f90a1b2c";
        var captures = Path.Combine(_dir, "captures-schema");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/stock", "web", "web",
            requestContent: "", responseContent: "{\"sku\":\"W-1\"}", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › schema", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        var err = new StringWriter();

        var asked = Path.Combine(_dir, "out-schema");
        Assert.True(IngestCommand.Run([captures, "--tests", tests, "-o", asked, "--schema"], new StringWriter(), err) == 0, err.ToString());

        var schema = Path.Combine(asked, "TestRunReport.schema.json");
        Assert.True(File.Exists(schema), "--schema wrote no TestRunReport.schema.json beside the report");
        // It describes the data file this ingest wrote: that file validates against it.
        var problems = Reports.SchemaValidationTests.Validate(schema, ReportFile.Resolve(Path.Combine(asked, "TestRunReport.json")));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Contains("TestRunReport.schema.json", RunManifest.TryRead(asked)!.Files);

        var unasked = Path.Combine(_dir, "out-no-schema");
        Assert.True(IngestCommand.Run([captures, "--tests", tests, "-o", unasked], new StringWriter(), err) == 0, err.ToString());
        Assert.True(File.Exists(Path.Combine(unasked, "TestRunReport.html")));
        Assert.False(File.Exists(Path.Combine(unasked, "TestRunReport.schema.json")), "the ingest wrote a schema nobody asked for");

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("  --schema ", usage.ToString());
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
        Assert.Contains("Traffic outside any test", ReportHtml.Read(Path.Combine(output, "TestRunReport.html")));
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

        // Since 5.0.0 the HTML report lists it in its Diagnostics tab (plans/OPTION_C_PLAN.md S3); the data file carries
        // the diagnostic either way, and the labs page beside the report lists it, named by the pointer after the report.
        var html = ReportHtml.Read(Path.Combine(output, "TestRunReport.html"));
        Assert.Contains("id=\"tab-diagnostics\"", html);
        Assert.Contains(">Report diagnostics (", html);
        Assert.Contains("tap-di-redis: decoding disabled on 1 connection(s)", html);
        var page = ReportHtml.Read(Path.Combine(output, "TestRunReport.labs.html"));
        Assert.Contains("<details class=\"report-diagnostics\" open>", page);
        Assert.Contains("tap-di-redis: decoding disabled on 1 connection(s)", page);
        Assert.True(printed.IndexOf("TestRunReport.labs.html", StringComparison.Ordinal) > printed.IndexOf("TestRunReport.html", StringComparison.Ordinal),
            $"the pointer does not list the page after the report:\n{printed}");
        using var json = System.Text.Json.JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(output, "TestRunReport.json")));
        Assert.Contains(json.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("kind").GetString() == "CaptureDegraded");

        // --no-diagnostics-section leaves the tab out, and nothing of it is in the file
        var hidden = Path.Combine(_dir, "hidden");
        Assert.Equal(0, IngestCommand.Run(
        [
            captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", hidden, "--no-diagnostics-section",
            "--diagnostic", "CaptureDegraded:tap-di-redis: decoding disabled on 1 connection(s)",
        ], new StringWriter(), new StringWriter()));
        var hiddenHtml = ReportHtml.Read(Path.Combine(hidden, "TestRunReport.html"));
        Assert.DoesNotContain("report-diagnostic", hiddenHtml);
        Assert.DoesNotContain("tap-di-redis: decoding disabled on 1 connection(s)", hiddenHtml);

        // a 4.x command line that asked for the section runs unchanged and has it
        var shown = Path.Combine(_dir, "shown");
        Assert.Equal(0, IngestCommand.Run(
        [
            captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", shown, "--diagnostics-section",
            "--diagnostic", "CaptureDegraded:tap-di-redis: decoding disabled on 1 connection(s)",
        ], new StringWriter(), new StringWriter()));
        var shownHtml = ReportHtml.Read(Path.Combine(shown, "TestRunReport.html"));
        Assert.Contains(">Report diagnostics (", shownHtml);
        Assert.Contains("tap-di-redis: decoding disabled on 1 connection(s)", shownHtml);

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--diagnostic <kind>:<msg>", usage.ToString());
        Assert.Contains("--no-diagnostics-section", usage.ToString());
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
    public void An_ingest_whose_specifications_are_blank_for_a_result_with_no_verdict_says_why()
    {
        // A test that started and never ended is reported Passed as a default, and since 4.11.1 a default blanks the
        // specifications as a failure does. The line explaining blank files counted failures only, so here it was silent.
        const string testId = "dfa171651916cd43dd8448eb211c80319c";
        var captures = Path.Combine(_dir, "captures-defaulted");
        Directory.CreateDirectory(captures);
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/health", "web", "web",
            requestContent: "", responseContent: "{\"ok\":true}", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(5));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        var tests = Path.Combine(captures, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › never ends", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
        ]);
        var output = Path.Combine(_dir, "out-defaulted");
        var @out = new StringWriter();

        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", output], @out, new StringWriter()));

        Assert.Equal(0, new FileInfo(Path.Combine(output, "Specifications.html")).Length);
        Assert.Contains("Specifications.html and Specifications.yml are blank: 1 of 1 scenario(s) have no verdict, only a default, and the specification is only published from a green run.", @out.ToString());

        // A failure and a default together: the line names both.
        const string failingId = "fa11ed651916cd43dd8448eb211c80319c";
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "cli › never ends", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "start", TestId = failingId, TestName = "cli › fails", Feature = "cli.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = failingId, Status = "failed", Error = "expected 1 got 2", DurationMs = 10, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);
        @out = new StringWriter();
        Assert.Equal(0, IngestCommand.Run([captures, "--tests", tests, "-o", Path.Combine(_dir, "out-defaulted-and-failed")], @out, new StringWriter()));
        Assert.Contains("are blank: 1 of 2 scenario(s) failed, and 1 of 2 have no verdict, only a default, and the specification", @out.ToString());
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
        var html = ReportHtml.Read(Path.Combine(output, "TestRunReport.html"));
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
        File.WriteAllLines(spans, [ReportHtml.Read(SpikeSpans).TrimEnd(), "{\"resourceSpans\":[{\"scopeSpans\""]);

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
