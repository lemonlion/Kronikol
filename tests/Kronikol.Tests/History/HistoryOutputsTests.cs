using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// History through the report generator (plans/CROSS_RUN_HISTORY_PLAN.md §6.6): a run reads the ledger,
/// writes its fragment, says its verdicts on every surface, and appends its line — and does none of it
/// when told not to. Each run names its ledger explicitly, because the test process has
/// <c>KRONIKOL_HISTORY=off</c> to keep the hundreds of other generated reports out of this repository's
/// ledger.
/// </summary>
[Collection("DiagramsFetcher")]
public class HistoryOutputsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-history-run").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Ledger => Path.Combine(_dir, "ledger", "history.jsonl");

    private string Reports(string run) => Path.Combine(_dir, run);

    private ReportConfigurationOptions Options(string run, string runId) => new()
    {
        ReportsFolderPath = Reports(run),
        SuiteName = "HistorySuite",
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
        GenerateTestRunReport = false,
        GenerateCtrfReport = true,
        // The pointer is process-wide console output; the tests that capture it share this collection.
        WriteRunSummaryToConsole = false,
        HistoryFilePath = Ledger,
        HistoryRunId = runId,
        HistoryMinRuns = 2
    };

    private static Feature[] Features(ExecutionResult pay, ExecutionResult refund = ExecutionResult.Passed) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "hist-" + Guid.NewGuid().ToString("N"), DisplayName = "Pay with an expired card", Result = pay,
                    ErrorMessage = pay == ExecutionResult.Failed ? "Expected 200 but got 500" : null,
                    Duration = TimeSpan.FromMilliseconds(120),
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "the charge is declined", Status = pay }]
                }
            ]
        },
        new Feature
        {
            DisplayName = "Refunds",
            Scenarios =
            [
                new Scenario { Id = "hist-" + Guid.NewGuid().ToString("N"), DisplayName = "Refund a paid order", Result = refund, Duration = TimeSpan.FromMilliseconds(40) }
            ]
        }
    ];

    private void Run(string run, string runId, Feature[] features, Action<ReportConfigurationOptions>? configure = null)
    {
        var options = Options(run, runId);
        configure?.Invoke(options);
        ReportGenerator.CreateStandardReportsWithDiagrams(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
    }

    [Fact]
    public void A_run_writes_its_fragment_and_appends_its_line_to_the_named_ledger()
    {
        Run("r1", "test:1:1", Features(ExecutionResult.Passed));

        var fragmentPath = Path.Combine(Reports("r1"), HistoryFormat.FragmentFileName);
        Assert.True(File.Exists(fragmentPath), "History.run.json was not written");
        var fragment = HistoryFragment.Parse(File.ReadAllText(fragmentPath));
        Assert.Equal("test:1:1", fragment.Run.Id);
        Assert.Equal("HistorySuite", fragment.Run.Suite);
        Assert.Equal("PP", fragment.Run.Results);
        Assert.Equal(2, fragment.Roster.Count);
        Assert.Equal(["Checkout", "Refunds"], fragment.Roster.Features);

        var ledger = HistoryLedgerReader.Read(Ledger, 50).Ledger!;
        var recorded = Assert.Single(ledger.Runs("HistorySuite"));
        Assert.Equal("test:1:1", recorded.Id);
        Assert.False(recorded.Partial);
    }

    [Fact]
    public void The_second_run_reads_the_first_and_every_surface_says_what_broke()
    {
        Run("r1", "test:1:1", Features(ExecutionResult.Passed));
        Run("r2", "test:2:1", Features(ExecutionResult.Failed));

        var digest = File.ReadAllText(Path.Combine(Reports("r2"), "Failures.md"));
        Assert.Contains("**History:** 1 broke", digest);
        Assert.Contains("History: **broke**", digest);
        Assert.Contains("passed in test:1:1", digest);
        Assert.Contains("last runs `PF`", digest);

        var jsonl = File.ReadAllLines(Path.Combine(Reports("r2"), "Failures.jsonl"));
        var failure = jsonl.Select(l => JsonDocument.Parse(l).RootElement).Single(e => e.GetProperty("kind").GetString() == "failure");
        var history = failure.GetProperty("history");
        Assert.Equal("broke", history.GetProperty("primary").GetString());
        Assert.Equal("PF", history.GetProperty("series").GetString());
        Assert.Contains("broke", history.GetProperty("verdicts").EnumerateArray().Select(v => v.GetString()));

        var ctrf = JsonDocument.Parse(File.ReadAllText(Path.Combine(Reports("r2"), CtrfReportGenerator.FileName))).RootElement;
        var test = ctrf.GetProperty("results").GetProperty("tests").EnumerateArray().Single(t => t.GetProperty("status").GetString() == "failed");
        Assert.Equal("broke", test.GetProperty("extra").GetProperty("kronikolHistory").GetProperty("primary").GetString());

        Assert.Equal(2, HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("HistorySuite").Count);
    }

    [Fact]
    public void A_flaky_scenario_is_flaky_on_the_ctrf_document_even_when_it_passed_first_time()
    {
        var results = new[] { ExecutionResult.Passed, ExecutionResult.Failed, ExecutionResult.Passed, ExecutionResult.Failed, ExecutionResult.Passed, ExecutionResult.Failed };
        for (var i = 0; i < results.Length; i++)
            Run("r" + i, $"test:{i}:1", Features(results[i]));
        Run("last", "test:99:1", Features(ExecutionResult.Passed));

        var ctrf = JsonDocument.Parse(File.ReadAllText(Path.Combine(Reports("last"), CtrfReportGenerator.FileName))).RootElement;
        var pay = ctrf.GetProperty("results").GetProperty("tests").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Pay with an expired card");

        Assert.True(pay.GetProperty("flaky").GetBoolean());
        Assert.Equal("flaky", pay.GetProperty("extra").GetProperty("kronikolHistory").GetProperty("primary").GetString());
    }

    [Fact]
    public void Failures_are_worked_through_regressions_first()
    {
        // Refund has been failing for two runs; Pay just broke. Pay is s0 by address but the reader
        // wants the regression first, and the digest says which is which.
        Run("r1", "test:1:1", Features(ExecutionResult.Passed, refund: ExecutionResult.Failed));
        Run("r2", "test:2:1", Features(ExecutionResult.Passed, refund: ExecutionResult.Failed));
        Run("r3", "test:3:1", Features(ExecutionResult.Failed, refund: ExecutionResult.Failed));

        var digest = File.ReadAllText(Path.Combine(Reports("r3"), "Failures.md"));
        var broke = digest.IndexOf("History: **broke**", StringComparison.Ordinal);
        var failing = digest.IndexOf("History: **always-failing**", StringComparison.Ordinal);
        Assert.True(broke >= 0 && failing >= 0, "both verdicts should be in the digest");
        Assert.True(broke < failing, "the regression should be worked through before the long-standing failure");
        Assert.Contains("1 broke, 1 always failing", digest);
    }

    [Fact]
    public void The_fragment_and_the_append_can_each_be_switched_off()
    {
        Run("r1", "test:1:1", Features(ExecutionResult.Passed), o => { o.GenerateHistoryFragment = false; o.WriteHistoryLedger = false; });

        Assert.False(File.Exists(Path.Combine(Reports("r1"), HistoryFormat.FragmentFileName)));
        Assert.False(File.Exists(Ledger));
        Assert.True(File.Exists(Path.Combine(Reports("r1"), "Failures.md")), "the rest of the run is unaffected");
    }

    [Fact]
    public void The_same_run_identity_is_not_appended_twice()
    {
        Run("r1", "test:1:1", Features(ExecutionResult.Passed));
        Run("r2", "test:1:1", Features(ExecutionResult.Failed));

        Assert.Single(HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("HistorySuite"));
    }

    [Fact]
    public void A_ledger_that_cannot_be_read_is_a_diagnostic_not_a_failed_run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ledger)!);
        File.WriteAllText(Ledger, "{\"t\":\"header\",\"historyFormatVersion\":99,\"generator\":\"9.0.0\"}\n");
        var collector = new ReportDiagnosticsCollector();

        using (ReportDiagnosticsScope.Begin(collector))
            Run("r1", "test:1:1", Features(ExecutionResult.Failed));

        var entry = Assert.Single(collector.Entries, d => d.Kind == DiagnosticKind.HistoryUnavailable);
        Assert.Contains("99", entry.Message);
        Assert.True(File.Exists(Path.Combine(Reports("r1"), "Failures.md")));
        Assert.True(File.Exists(Path.Combine(Reports("r1"), HistoryFormat.FragmentFileName)), "the fragment is still written so nothing is lost");
        Assert.Equal("{\"t\":\"header\",\"historyFormatVersion\":99,\"generator\":\"9.0.0\"}\n", File.ReadAllText(Ledger));
    }

    [Fact]
    public void A_filtered_run_is_recorded_as_partial_with_a_diagnostic()
    {
        Run("r1", "test:1:1", Features(ExecutionResult.Passed));
        var collector = new ReportDiagnosticsCollector();
        var one = Features(ExecutionResult.Passed).Take(1).ToArray();

        using (ReportDiagnosticsScope.Begin(collector))
            Run("r2", "test:2:1", one);

        Assert.Contains(collector.Entries, d => d.Kind == DiagnosticKind.HistoryPartialRun);
        var fragment = HistoryFragment.Parse(File.ReadAllText(Path.Combine(Reports("r2"), HistoryFormat.FragmentFileName)));
        Assert.True(fragment.Run.Partial);
        Assert.True(HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("HistorySuite")[^1].Partial);
    }

    [Fact]
    public void The_pointer_carries_the_history_line_on_both_channels()
    {
        var summary = new RunSummary(_dir, [], 3, [new RunSummaryFailure("abc", "Checkout", "Pay")], false, History: "history: 1 broke (against 4 earlier runs on main)");

        Assert.Contains("\n  history: 1 broke (against 4 earlier runs on main)\n", RunSummaryConsoleWriter.Build(summary));
        Assert.Contains("history: 1 broke (against 4 earlier runs on main)\n\n", RunSummaryConsoleWriter.BuildCiSummarySection(summary));
        Assert.DoesNotContain("history:", RunSummaryConsoleWriter.Build(summary with { History = null }));
    }

    [Fact]
    public void Nothing_about_history_reaches_the_outputs_when_it_is_off()
    {
        // KRONIKOL_HISTORY=off, which this test process has: no ledger named, so no fragment, no
        // diagnostic, no verdict line — the outputs are what they were before history existed.
        var collector = new ReportDiagnosticsCollector();
        var options = Options("r1", "test:1:1");
        options.HistoryFilePath = null;

        using (ReportDiagnosticsScope.Begin(collector))
            ReportGenerator.CreateStandardReportsWithDiagrams(Features(ExecutionResult.Failed), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.False(File.Exists(Path.Combine(Reports("r1"), HistoryFormat.FragmentFileName)));
        Assert.DoesNotContain(collector.Entries, d => d.Kind is DiagnosticKind.HistoryUnavailable or DiagnosticKind.HistoryPartialRun or DiagnosticKind.HistoryLedgerDamaged);
        Assert.DoesNotContain("History", File.ReadAllText(Path.Combine(Reports("r1"), "Failures.md")));
        Assert.DoesNotContain("\"history\"", File.ReadAllText(Path.Combine(Reports("r1"), "Failures.jsonl")));
        Assert.DoesNotContain("kronikolHistory", File.ReadAllText(Path.Combine(Reports("r1"), CtrfReportGenerator.FileName)));
    }

    [Fact]
    public void The_html_report_embeds_history_unless_told_not_to()
    {
        Run("h1", "test:1:1", Features(ExecutionResult.Passed), o => o.GenerateTestRunReport = true);
        Run("h2", "test:2:1", Features(ExecutionResult.Failed), o => o.GenerateTestRunReport = true);
        Run("h3", "test:3:1", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.EmbedHistoryInReport = false; });

        var second = File.ReadAllText(Path.Combine(Reports("h2"), "TestRunReport.html"));
        Assert.Contains("<details id=\"history-section\"", second);
        Assert.Contains("data-history-verdicts=\"broke\"", second);
        Assert.Contains("history-sparkline", second);

        var third = File.ReadAllText(Path.Combine(Reports("h3"), "TestRunReport.html"));
        Assert.DoesNotContain("<details id=\"history-section\"", third);
        Assert.DoesNotContain("data-history-verdicts=\"", third);
        // The ledger still saw the run: embedding is about the file, not about recording.
        Assert.Contains("test:3:1", File.ReadAllText(Ledger));
    }

    /// <summary>Three earlier runs of the same roster recorded on the <c>main</c> stream, so a local run has another stream to read against.</summary>
    private void SeedMain(params string[] results)
    {
        var (roster, run) = HistoryRunBuilder.Build(Features(ExecutionResult.Passed), [], "HistorySuite", null,
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new HistoryBuildOptions(), "gh:0:1");
        for (var i = 0; i < results.Length; i++)
        {
            var line = run with
            {
                Id = $"gh:{i + 1}:1", Branch = "main", Commit = $"c{i + 1:D6}", At = run.At.AddHours(i),
                Results = results[i], Attempts = new string('-', results[i].Length),
                Durations = Enumerable.Repeat<int?>(100, results[i].Length).ToArray(),
                Errors = results[i].Select(r => r == 'F' ? "e1" : null).ToArray(),
                ErrorText = results[i].Contains('F') ? new Dictionary<string, string> { ["e1"] = "boom" } : new Dictionary<string, string>()
            };
            Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(Ledger, roster, line, "3.12.0").Outcome);
        }
    }

    [Fact]
    public void A_run_reads_against_the_stream_it_is_told_to()
    {
        // A pull request's run forms its own stream and would read as a cold start; told to read against
        // main, a failure that passed on main is the regression it is.
        SeedMain("PP", "PP", "PP");

        Run("own", "test:9:1", Features(ExecutionResult.Failed), o => o.GenerateTestRunReport = true);
        Run("main", "test:9:2", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.HistoryBranch = "main"; });

        var own = File.ReadAllText(Path.Combine(Reports("own"), "Failures.md"));
        Assert.DoesNotContain("**broke**", own);

        var against = File.ReadAllText(Path.Combine(Reports("main"), "Failures.md"));
        Assert.Contains("**broke**", against);
        Assert.Contains("against 3 earlier runs on main", against);
        Assert.Contains(@"data-history-verdicts=""broke""", File.ReadAllText(Path.Combine(Reports("main"), "TestRunReport.html")));
        // The run's own line still records under its own stream, so main's history stays main's.
        var ownLine = File.ReadAllLines(Ledger).Single(l => l.Contains(@"""id"":""test:9:2""", StringComparison.Ordinal));
        Assert.DoesNotContain(@"""branch"":""main""", ownLine);
    }

    [Fact]
    public void A_compare_branch_is_read_out_beside_the_run_s_own_stream()
    {
        SeedMain("PP", "PP", "PP");

        Run("cmp", "test:9:3", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.HistoryCompareBranch = "main"; });

        var digest = File.ReadAllText(Path.Combine(Reports("cmp"), "Failures.md"));
        Assert.Contains("on main: 1 broke (against 3 earlier runs on main)", digest);
        var html = File.ReadAllText(Path.Combine(Reports("cmp"), "TestRunReport.html"));
        Assert.Contains(@"class=""history-compare""", html);
        Assert.Contains("on <code>main</code>: 1 broke", html);
    }
}
