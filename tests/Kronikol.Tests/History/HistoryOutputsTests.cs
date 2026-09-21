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
        HistoryMinRuns = 2,
        // The run's own stream in every environment: on a pull request build the default reads against the
        // branch it targets, which is what one test here is about and the rest are not.
        HistoryBranch = ""
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
    public void A_consumers_templating_rules_reach_the_run_and_a_bad_one_is_a_diagnostic_not_a_failed_run()
    {
        var collector = new ReportDiagnosticsCollector();

        using (ReportDiagnosticsScope.Begin(collector))
            Run("r1", "test:1:1", Features(ExecutionResult.Passed), options =>
            {
                options.HistoryShapeTemplates.Add(new HistoryShapeTemplate("(unclosed", "{x}"));
                options.HistoryShapeTemplates.Add(new HistoryShapeTemplate(@"rep_[a-z]+", "rep_{key}"));
            });

        var entry = Assert.Single(collector.Entries, d => d.Kind == DiagnosticKind.HistoryShapeTemplate);
        Assert.Contains("(unclosed", entry.Message);
        var run = HistoryLedgerReader.Read(Ledger, 50).Ledger!.LatestRun("HistorySuite")!;
        Assert.Matches("^[0-9a-f]{8}$", run.ShapeRules);

        // And a run configured with none records none.
        Run("r2", "test:2:1", Features(ExecutionResult.Passed));
        Assert.Null(HistoryLedgerReader.Read(Ledger, 50).Ledger!.LatestRun("HistorySuite")!.ShapeRules);
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
        void Section(ReportConfigurationOptions o) { o.GenerateTestRunReport = true; o.ShowHistorySection = true; }
        Run("h1", "test:1:1", Features(ExecutionResult.Passed), Section);
        Run("h2", "test:2:1", Features(ExecutionResult.Failed), Section);
        Run("h3", "test:3:1", Features(ExecutionResult.Failed), o => { Section(o); o.EmbedHistoryInReport = false; });

        var second = File.ReadAllText(Path.Combine(Reports("h2"), "TestRunReport.html"));
        Assert.Contains("<details id=\"history-section\"", second);
        Assert.Contains("data-history-verdicts=\"broke\"", second);
        Assert.Contains("<span class=\"history-sparkline\"", second);

        // EmbedHistoryInReport is the master switch: nothing about history reaches the file, section asked for or not.
        // Every assertion here names emitted markup: the stylesheet carries the class names either way.
        var third = File.ReadAllText(Path.Combine(Reports("h3"), "TestRunReport.html"));
        Assert.DoesNotContain("<details id=\"history-section\"", third);
        Assert.DoesNotContain("data-history-verdicts=\"", third);
        Assert.DoesNotContain("<span class=\"history-sparkline\"", third);
        // The ledger still saw the run: embedding is about the file, not about recording.
        Assert.Contains("test:3:1", File.ReadAllText(Ledger));
    }

    [Fact]
    public void The_history_section_is_left_out_of_the_report_unless_it_is_asked_for()
    {
        Run("h1", "test:1:1", Features(ExecutionResult.Passed), o => o.GenerateTestRunReport = true);
        Run("h2", "test:2:1", Features(ExecutionResult.Failed), o => o.GenerateTestRunReport = true);
        Run("h3", "test:3:1", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.ShowHistorySection = true; });

        // A run that broke a scenario has as much to say as history ever has, and the section is still not there.
        // The assertions name emitted markup, not class names: the stylesheet carries those either way.
        var byDefault = File.ReadAllText(Path.Combine(Reports("h2"), "TestRunReport.html"));
        Assert.DoesNotContain("<details id=\"history-section\"", byDefault);
        Assert.DoesNotContain("<summary class=\"h2\">History ", byDefault);
        // Only the section goes. What the run read is still beside the scenario it is about.
        Assert.Contains("data-history-verdicts=\"broke\"", byDefault);
        Assert.Contains("<span class=\"history-sparkline\"", byDefault);

        var asked = File.ReadAllText(Path.Combine(Reports("h3"), "TestRunReport.html"));
        Assert.Contains("<details id=\"history-section\"", asked);
        Assert.Contains("<summary class=\"h2\">History ", asked);
    }

    /// <summary>
    /// Three earlier runs of the same roster recorded on a <c>trunk</c> stream, so the run under test has another
    /// stream to read against. Not <c>main</c>: on CI the process is on GITHUB_REF_NAME, and a push to main would put
    /// the run under test on the very stream it is meant to be reading across to.
    /// </summary>
    private void SeedTrunk(params string[] results) => Seed("trunk", 1, results);

    private void Seed(string branch, int firstId, params string[] results)
    {
        var (roster, run) = HistoryRunBuilder.Build(Features(ExecutionResult.Passed), [], "HistorySuite", null,
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new HistoryBuildOptions(), "gh:0:1");
        for (var i = 0; i < results.Length; i++)
        {
            var line = run with
            {
                Id = $"gh:{firstId + i}:1", Branch = branch, Commit = $"c{firstId + i:D6}", At = run.At.AddHours(firstId + i),
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
        // trunk, a failure that passed on trunk is the regression it is.
        SeedTrunk("PP", "PP", "PP");

        Run("own", "test:9:1", Features(ExecutionResult.Failed), o => o.GenerateTestRunReport = true);
        Run("against", "test:9:2", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.HistoryBranch = "trunk"; });

        var own = File.ReadAllText(Path.Combine(Reports("own"), "Failures.md"));
        Assert.DoesNotContain("**broke**", own);

        var against = File.ReadAllText(Path.Combine(Reports("against"), "Failures.md"));
        Assert.Contains("**broke**", against);
        Assert.Contains("against 3 earlier runs on trunk", against);
        Assert.Contains(@"data-history-verdicts=""broke""", File.ReadAllText(Path.Combine(Reports("against"), "TestRunReport.html")));
        // The run's own line still records under its own stream, so main's history stays main's.
        var ownLine = File.ReadAllLines(Ledger).Single(l => l.Contains(@"""id"":""test:9:2""", StringComparison.Ordinal));
        Assert.DoesNotContain(@"""branch"":""trunk""", ownLine);
    }

    [Fact]
    public void A_compare_branch_is_read_out_beside_the_run_s_own_stream()
    {
        SeedTrunk("PP", "PP", "PP");

        // The compare reading is a line of the History section, so this run asks for the section.
        Run("cmp", "test:9:3", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.ShowHistorySection = true; o.HistoryCompareBranch = "trunk"; });

        var digest = File.ReadAllText(Path.Combine(Reports("cmp"), "Failures.md"));
        Assert.Contains("on trunk: 1 broke (against 3 earlier runs on trunk)", digest);
        var html = File.ReadAllText(Path.Combine(Reports("cmp"), "TestRunReport.html"));
        Assert.Contains(@"class=""history-compare""", html);
        Assert.Contains("on <code>trunk</code>: 1 broke", html);
    }

    [Fact]
    public void A_pull_request_reads_its_target_however_many_pull_request_runs_came_since()
    {
        // #95: the window was the suite's last lines whatever their branch. Three runs of trunk and then
        // three of a pull request, read with a window of two: trunk had no run left in it, and a failure
        // that passed on trunk read as new instead of as the regression it is.
        SeedTrunk("PP", "PP", "PP");
        Seed("42/merge", 4, "PP", "PP", "PP");
        var ci = new CiMetadata(CiEnvironment.GitHubActions, "7", "42/merge", "abc1234", null, "o/r", "77", "1");
        var options = Options("pr-window", "gh:77:1");
        options.HistoryBranch = "trunk";
        options.HistoryWindow = 2;

        var context = HistoryRunContext.Create(Features(ExecutionResult.Failed), [], "HistorySuite", ci, DateTimeOffset.UtcNow, options, Reports("pr-window"), "3.25.1", _ => null)!;

        Assert.Equal("trunk", context.Verdicts!.Stream);
        Assert.Equal(2, context.Verdicts.RunsRecorded);
        Assert.Equal(1, context.Verdicts.Count(HistoryVerdictKind.Broke));
    }

    [Fact]
    public void A_window_of_zero_is_not_an_invitation_to_read_the_whole_ledger_back()
    {
        // The read at the end of a run has always been given at least a window of one; the analysis is held
        // to the same, or "0" would now mean every run the stream ever had, parsed at the end of every run.
        SeedTrunk("PP", "PP", "PP");
        var options = Options("zero-window", "gh:78:1");
        options.HistoryBranch = "trunk";
        options.HistoryWindow = 0;

        var context = HistoryRunContext.Create(Features(ExecutionResult.Failed), [], "HistorySuite", null, DateTimeOffset.UtcNow, options, Reports("zero-window"), "3.25.1", _ => null)!;

        Assert.Equal(1, context.Verdicts!.RunsRecorded);
    }

    [Fact]
    public void A_pull_request_reads_against_the_branch_it_targets_without_being_told()
    {
        // On a pull request build the environment names the branch the pull request targets, and that is
        // the stream the run reads against; a push reads its own, and HistoryBranch set still wins. The
        // context is driven directly because the environment is the process's.
        SeedTrunk("PP", "PP", "PP");
        var ci = new CiMetadata(CiEnvironment.GitHubActions, "7", "42/merge", "abc1234", null, "o/r", "77", "1");
        string? PullRequest(string key) => key switch { "GITHUB_ACTIONS" => "true", "GITHUB_BASE_REF" => "trunk", _ => null };
        string? Push(string key) => key == "GITHUB_ACTIONS" ? "true" : null;
        HistoryRunContext Create(Func<string, string?> env, Action<ReportConfigurationOptions>? configure = null)
        {
            var options = Options("pr", "gh:77:1");
            options.HistoryBranch = null;
            configure?.Invoke(options);
            return HistoryRunContext.Create(Features(ExecutionResult.Failed), [], "HistorySuite", ci, DateTimeOffset.UtcNow, options, Reports("pr"), "3.13.0", env)!;
        }

        var pullRequest = Create(PullRequest);
        Assert.Equal("trunk", pullRequest.Verdicts!.Stream);
        Assert.Equal(1, pullRequest.Verdicts.Count(HistoryVerdictKind.Broke));
        Assert.Equal("42/merge", pullRequest.Run.Branch);

        Assert.Equal("42/merge", Create(Push).Verdicts!.Stream);
        Assert.Equal("release", Create(PullRequest, o => o.HistoryBranch = "release").Verdicts!.Stream);
        Assert.Equal("42/merge", Create(PullRequest, o => o.HistoryBranch = "").Verdicts!.Stream);
    }
}
