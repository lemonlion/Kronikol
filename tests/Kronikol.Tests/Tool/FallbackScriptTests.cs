using System.Net;
using System.Text.RegularExpressions;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The skill ships <c>scripts/query.py</c> for machines with no .NET tool. It is a fallback, and the
/// failure mode of a fallback is that nobody exercises it: it can rot for a year and only be found by the
/// one person who reaches for it, at the moment they had already run out of other options.
///
/// <para>So it is run against the same report as the real tool and made to agree. Not on formatting - the
/// two deliberately print differently - but on the numbers, which is what "degrades to fewer answers, not
/// to wrong ones" has to mean.</para>
/// </summary>
public class FallbackScriptTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-fallback").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string ScriptPath => Path.Combine(
        RepoRoot, "templates", "skills", "kronikol-test-debugging", "scripts", "query.py");

    // ─── The script's own documentation ────────────────────────

    [Fact]
    public void The_docstring_names_exactly_the_commands_the_script_registers()
    {
        var source = File.ReadAllText(ScriptPath);

        var registered = Regex.Matches(source, @"^    ""([a-z]+)"": cmd_", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(registered);

        var docstring = source[..source.IndexOf("\"\"\"", source.IndexOf("\"\"\"", StringComparison.Ordinal) + 3,
            StringComparison.Ordinal)];

        var advertised = Regex.Matches(docstring, @"^    python query\.py ([a-z]+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(registered, advertised);
    }

    [Fact]
    public void The_docstring_does_not_promise_a_command_the_script_lacks()
    {
        var source = File.ReadAllText(ScriptPath);
        var registered = Regex.Matches(source, @"^    ""([a-z]+)"": cmd_", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // The "only in the real tool" list is the script telling the reader where its edges are. A verb on
        // both sides of that line is worse than a missing one: it sends them to the tool they do not have.
        var missing = Regex.Match(source, @"missing from this fallback\):\s*\n((?:\s{4}.+\n)+)");
        Assert.True(missing.Success, "query.py no longer says which commands it does not implement.");

        var claimed = Regex.Matches(missing.Groups[1].Value, @"[a-z]+")
            .Select(m => m.Value)
            .Where(registered.Contains)
            .ToList();

        Assert.True(claimed.Count == 0,
            "query.py lists as unimplemented commands it actually has: " + string.Join(", ", claimed));
    }

    // ─── Agreement with the real tool ──────────────────────────

    [Fact]
    public void It_counts_the_same_scenarios_and_failures_as_the_tool()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        // query.py has no --count, so its numbers are parsed out of the header it prints.
        var summary = PythonProbe.Run(ScriptPath, "summary", report);
        var counts = Regex.Match(summary, @"(\d+) scenarios . (\d+) failed");
        Assert.True(counts.Success, "query.py summary no longer prints 'N scenarios · N failed':\n" + summary);

        Assert.Equal(Tool("summary", report, "--count").Trim(), counts.Groups[1].Value);
        Assert.Equal(Tool("failures", report, "--count").Trim(), counts.Groups[2].Value);
    }

    [Fact]
    public void It_counts_tests_as_the_tool_does_when_a_scenario_is_not_a_test()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        // An ingest's fold, failed: the tool counts it as no test and no failure (plans/RUN_WINDOW_PLAN.md R1).
        var report = WriteReport(withNotATest: true);

        var summary = PythonProbe.Run(ScriptPath, "summary", report);
        var counts = Regex.Match(summary, @"(\d+) scenarios . 1 not a test . (\d+) failed");
        Assert.True(counts.Success, "query.py summary does not count the scenario that is not a test apart:\n" + summary);
        Assert.Equal(Tool("summary", report, "--count").Trim(), counts.Groups[1].Value);
        Assert.Equal(Tool("failures", report, "--count").Trim(), counts.Groups[2].Value);
        var footer = Regex.Match(PythonProbe.Run(ScriptPath, "failures", report), @"(\d+) failed");
        Assert.Equal(Tool("failures", report, "--count").Trim(), footer.Groups[1].Value);
    }

    private static string[] Lines(string output) => output.Replace("\r\n", "\n").Split('\n');

    [Fact]
    public void It_names_a_feature_of_no_test_as_the_tool_does()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        // The fold failed: the feature line said "0 passed, 1 FAILED" where the tool says what it is.
        var report = WriteReport(withNotATest: true);

        var lines = Lines(PythonProbe.Run(ScriptPath, "summary", report));
        Assert.Contains("Ingested  1 not a test", lines);
        Assert.Contains("Ingested  1 not a test", Lines(Tool("summary", report)));
    }

    [Fact]
    public void It_says_all_passed_for_the_tests_alone()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport(withNotATest: true, passing: true);

        Assert.Contains("2 scenarios, all passed · 1 not a test", PythonProbe.Run(ScriptPath, "failures", report), StringComparison.Ordinal);
        Assert.Contains("2 scenarios, all passed · 1 not a test", Tool("failures", report), StringComparison.Ordinal);
    }

    [Fact]
    public void It_finds_a_report_beside_its_retained_runs_and_baseline_as_the_tool_does()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();
        var top = Path.Combine(Path.GetDirectoryName(report)!, "search-" + Guid.NewGuid().ToString("N")[..8]);
        var reports = Path.Combine(top, "bin", "Reports");
        foreach (var copy in new[] { reports, Path.Combine(reports, "runs", "gh_1_1"), Path.Combine(reports, "baseline") })
        {
            Directory.CreateDirectory(copy);
            File.Copy(report, Path.Combine(copy, "TestRunReport.json"));
        }

        // Until 4.27.1 query.py counted three reports here and asked which; the tool takes the one its runs belong to.
        var summary = PythonProbe.Run(ScriptPath, "summary", top);

        Assert.Contains(Tool("summary", top, "--count").Trim() + " scenarios", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Its_failures_footer_agrees_with_the_tool()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        var failures = PythonProbe.Run(ScriptPath, "failures", report);
        var footer = Regex.Match(failures, @"(\d+) failed");
        Assert.True(footer.Success, "query.py failures no longer prints an 'N failed' footer:\n" + failures);

        Assert.Equal(Tool("failures", report, "--count").Trim(), footer.Groups[1].Value);
    }

    [Fact]
    public void It_addresses_scenarios_the_same_way_the_tool_does()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        // An address the fallback prints has to be one the real tool accepts, or the two cannot be used in
        // the same session - and the whole point of the fallback is that you reach for it mid-investigation.
        var address = Regex.Match(PythonProbe.Run(ScriptPath, "summary", report), @"^  (s\d+)\s", RegexOptions.Multiline);
        Assert.True(address.Success, "query.py summary printed no scenario address");

        var scoped = Tool("steps", report, address.Groups[1].Value);
        Assert.Contains("the total is right", scoped, StringComparison.Ordinal);
        Assert.Contains(address.Groups[1].Value, PythonProbe.Run(ScriptPath, "steps", report, address.Groups[1].Value),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The script's docstring claims "the same addressing", and for a while that was untrue in two ways
    /// at once: a step path was parsed for its scenario ordinal and then discarded, so <c>s1/99</c> was
    /// byte-identical to <c>s1</c>; and a malformed ordinal reached <c>int()</c>, so <c>sabc</c> produced
    /// a Python traceback where the tool prints a sentence and exits 2. A fallback that answers something
    /// else, or crashes, is worse than one that is not there.
    /// </summary>
    [Fact]
    public void A_step_address_scopes_the_fallback_the_way_it_scopes_the_tool()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        var whole = PythonProbe.Run(ScriptPath, "steps", report, "s1");
        var scoped = PythonProbe.Run(ScriptPath, "steps", report, "s1/0");

        Assert.Contains("the total is right", whole, StringComparison.Ordinal);
        Assert.Contains("the total is right", scoped, StringComparison.Ordinal);
        Assert.NotEqual(whole, scoped);
        Assert.Contains("scoped to step 0", scoped, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_path_the_scenario_does_not_have_is_refused_by_the_fallback_too()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        var (output, error, exit) = PythonProbe.RunFull(ScriptPath, "steps", report, "s1/99");

        Assert.Equal(2, exit);
        Assert.DoesNotContain("the total is right", output, StringComparison.Ordinal);
        Assert.Contains("99", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_address_is_a_sentence_not_a_traceback()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        var (_, error, exit) = PythonProbe.RunFull(ScriptPath, "steps", report, "sabc");

        Assert.Equal(2, exit);
        Assert.DoesNotContain("Traceback", error, StringComparison.Ordinal);
        Assert.Contains("Not an address", error, StringComparison.Ordinal);
    }

    /// <summary>The cross-run identity, which the fallback has to take for the same reason the tool does.</summary>
    [Fact]
    public void The_fallback_takes_a_stableId_address()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        var stableId = Regex.Match(PythonProbe.Run(ScriptPath, "steps", report, "s1"), @"stableId (\S+)");
        Assert.True(stableId.Success, "query.py steps no longer prints a stableId");

        Assert.Equal(
            PythonProbe.Run(ScriptPath, "steps", report, "s1"),
            PythonProbe.Run(ScriptPath, "steps", report, "sid:" + stableId.Groups[1].Value));
    }

    [Fact]
    public void It_names_the_same_services_as_the_tool()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        var fallback = PythonProbe.Run(ScriptPath, "services", report);
        foreach (var service in new[] { "Pricing", "Inventory" })
            Assert.Contains(service, fallback, StringComparison.Ordinal);
    }

    /// <summary>
    /// A report written with <c>CompressTestRunReportPayloads</c> (#85) holds a large body as
    /// <c>{"$h", "$n", "$z"}</c>. The script inflates it as it loads the file, so a body reads as it does in a plain
    /// report and carries the address the tool prints for it; before, the script saw a dictionary where it expected
    /// text and failed on it.
    /// </summary>
    [Fact]
    public void It_reads_a_compressed_payload_as_the_tool_does()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        // Long enough to be compressed, and longer than the 6,000 bytes the script prints whole, so its listing names
        // the address instead.
        var body = Kronikol.Tests.Reports.PayloadCompressionTests.Json(Math.Max(ReportPayloads.Threshold, 6000) + 1000);
        var report = ReportGenerator.GenerateTestRunReportData(
            Kronikol.Tests.Reports.PayloadCompressionTests.Features(),
            Kronikol.Tests.Reports.PayloadCompressionTests.Start, Kronikol.Tests.Reports.PayloadCompressionTests.End,
            Path.Combine(_directory, "TestRunReport.json"), DataFormat.Json, null,
            Kronikol.Tests.Reports.PayloadCompressionTests.Logs(body, "{\"ok\":true}"),
            diagnostics: null, fullStepDetail: true, ciMetadata: null, suite: null, environment: null, attribution: null,
            compressPayloads: true);
        Assert.Contains("\"$z\"", File.ReadAllText(report), StringComparison.Ordinal);

        var listing = PythonProbe.Run(ScriptPath, "http", report, "s0/i0");
        var keys = PythonProbe.Run(ScriptPath, "http", report, "s0/i0", "--keys");
        var found = PythonProbe.Run(ScriptPath, "grep", report, "abcdefgabcdefg");

        Assert.Contains(Kronikol.Tests.Reports.PayloadCompressionTests.Address(body), listing, StringComparison.Ordinal);
        Assert.Contains(Kronikol.Tests.Reports.PayloadCompressionTests.Address(body), Tool("http", report, "s0/i0"), StringComparison.Ordinal);
        Assert.Contains("note", keys, StringComparison.Ordinal);
        Assert.Contains("s0/i0", found, StringComparison.Ordinal);
    }

    // ─── Harness ───────────────────────────────────────────────

    private string Tool(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    // ─── What a capture capped, and why a call failed (the #168 audit) ─────────

    /// <summary>The checkout's Pricing call answered 500 with the service's reason and a capped batch.</summary>
    private string WriteCappedReport()
    {
        var start = new DateTimeOffset(2026, 1, 1, 10, 1, 0, TimeSpan.Zero);
        var pairId = Guid.NewGuid();
        RequestResponseLog[] logs =
        [
            new("Orders", "t1", "Find ← prices", "{ \"filter\" : { } }", new Uri("mongodb:///shop/prices"), [],
                "Pricing", "Checkout fails on a wrong total", RequestResponseType.Request, Guid.NewGuid(), pairId, false)
            { Timestamp = start },
            new("Orders", "t1", "Find ← prices", "[{ \"total\" : 5 }, { \"total\" : 7 }]\n... (3 more documents not shown)",
                new Uri("mongodb:///shop/prices"), [], "Pricing", "Checkout fails on a wrong total", RequestResponseType.Response,
                Guid.NewGuid(), pairId, false, HttpStatusCode.InternalServerError)
            { Timestamp = start.AddMilliseconds(20), Error = "E11000 duplicate key error collection: shop.prices" },
        ];
        var written = ReportGenerator.GenerateTestRunReportData(
            Features(),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Capped_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, logs);

        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    [Fact]
    public void It_reads_a_capped_body_as_the_tool_reads_it()
    {
        // A capped batch is a complete document and a footnote; the fallback called it text and died on --path.
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteCappedReport();

        var keys = PythonProbe.Run(ScriptPath, "http", report, "s1/i1", "--keys");
        Assert.Contains("$[0].total", keys);
        Assert.DoesNotContain("not JSON", keys);

        Assert.Contains("7", PythonProbe.Run(ScriptPath, "http", report, "s1/i1", "--path", "$[1].total"));

        var body = PythonProbe.Run(ScriptPath, "http", report, "s1/i1").ReplaceLineEndings("\n");
        Assert.Contains("\"total\": 7", body);
        Assert.Contains("]\n... (3 more documents not shown)", body);
    }

    [Fact]
    public void It_says_why_a_call_failed_as_the_tool_does()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteCappedReport();

        var output = PythonProbe.Run(ScriptPath, "http", report, "s1/i1");

        Assert.Contains("error E11000 duplicate key error collection: shop.prices", output);
        Assert.Contains("E11000 duplicate key error collection: shop.prices", Tool("http", report, "s1/i1"));
    }

    [Fact]
    public void It_prints_its_arrows_through_a_pipe_whatever_the_console_code_page()
    {
        // A pipe on Windows gets the console's code page, which cannot encode → : `http` died with UnicodeEncodeError.
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteCappedReport();

        var (output, error, exit) = PythonProbe.RunFullAsPiped(ScriptPath, "http", report, "s1/i1");

        Assert.True(exit == 0, error);
        Assert.Contains("→ Pricing", output);
    }

    private string WriteReport(bool withNotATest = false, bool passing = false)
    {
        var features = Features();
        if (passing)
            features[0].Scenarios[1].Result = ExecutionResult.Passed;
        if (withNotATest)
            features = [.. features, new Feature
            {
                DisplayName = "Ingested",
                Scenarios = [new Scenario { Id = "session", DisplayName = "Traffic outside any test", Result = passing ? ExecutionResult.Passed : ExecutionResult.Failed, NotATest = true }]
            }];
        var written = ReportGenerator.GenerateTestRunReportData(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Fallback_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, Logs());

        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t0", DisplayName = "Browse the catalogue", Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1.2),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "browsing", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "t1", DisplayName = "Checkout fails on a wrong total", Result = ExecutionResult.Failed,
                    Duration = TimeSpan.FromSeconds(3.5), ErrorMessage = "Assert.Equal() Failure",
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Then", Text = "the total is right", Status = ExecutionResult.Failed,
                            FailureMessage = "Expected 4173 but found 3902",
                            SourceFile = "OverviewTests.cs", SourceLine = 142
                        }
                    ]
                }
            ]
        }
    ];

    /// <summary>One request and one response per service, paired by RequestResponseId, so `services` has
    /// something to count on both sides.</summary>
    private static RequestResponseLog[] Logs()
    {
        var start = new DateTimeOffset(2026, 1, 1, 10, 1, 0, TimeSpan.Zero);
        var logs = new List<RequestResponseLog>();

        foreach (var (service, uri, offset) in new[]
                 {
                     ("Pricing", "http://pricing/total", 0),
                     ("Inventory", "http://inventory/stock", 1)
                 })
        {
            var pairId = Guid.NewGuid();
            logs.Add(new RequestResponseLog("Orders", "t1", HttpMethod.Get, "{}", new Uri(uri), [],
                service, "Checkout fails on a wrong total", RequestResponseType.Request, Guid.NewGuid(), pairId, false)
            { Timestamp = start.AddSeconds(offset) });
            logs.Add(new RequestResponseLog("Orders", "t1", HttpMethod.Get, "{\"total\":3902}", new Uri(uri), [],
                service, "Checkout fails on a wrong total", RequestResponseType.Response, Guid.NewGuid(), pairId, false,
                HttpStatusCode.OK)
            { Timestamp = start.AddSeconds(offset).AddMilliseconds(20) });
        }

        return [.. logs];
    }
}
