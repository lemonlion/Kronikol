using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The guards of plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md section 7.3: with default options a report carries no
/// history and no report diagnostics at all (markup, stylesheet rules or verdict search code), so a run that read a
/// ledger and recorded diagnostics writes the same <c>TestRunReport.html</c> as one that did neither; and each opt-in
/// brings back its own pieces and no others. Each run names its ledger, because the test process has
/// <c>KRONIKOL_HISTORY=off</c>.
/// </summary>
[Collection("DiagramsFetcher")]
public class DefaultReportCleanTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-clean-report").FullName;

    public DefaultReportCleanTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Ledger => Path.Combine(_dir, "ledger", "history.jsonl");

    private string Reports(string run) => Path.Combine(_dir, run);

    private string Report(string run, string name = "TestRunReport.html") => File.ReadAllText(Path.Combine(Reports(run), name));

    // One set of features for every run, so two renders differ only by what the run read and recorded.
    private readonly Feature[] _features =
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario { Id = "clean-pay", DisplayName = "Pay with an expired card", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(120) },
                new Scenario { Id = "clean-refund", DisplayName = "Refund a paid order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(40) }
            ]
        }
    ];

    /// <summary>One run at a fixed time with a fixed id. <paramref name="ledger"/> names the ledger;
    /// <paramref name="diagnostics"/> are recorded by the host before the run.</summary>
    private void Run(string run, bool ledger, int diagnostics = 0, string runId = "clean:1:1", Action<ReportConfigurationOptions>? configure = null)
    {
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = Reports(run),
            SuiteName = "CleanSuite",
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = true,
            WriteRunSummaryToConsole = false,
            HistoryRunId = runId,
            HistoryMinRuns = 2,
            HistoryBranch = ""
        };
        if (ledger)
            options.HistoryFilePath = Ledger;
        configure?.Invoke(options);

        var collector = new ReportDiagnosticsCollector();
        for (var i = 0; i < diagnostics; i++)
            collector.Add(DiagnosticKind.CaptureDegraded, $"a tap dropped {i + 3} segments");
        DefaultDiagramsFetcher.Reset();
        // Its own log: other tests' calls in the process-wide one would read as this run's calls under an unknown id.
        using (RequestResponseLogger.IsolateForTests())
        using (ReportDiagnosticsScope.Begin(collector))
            ReportGenerator.CreateStandardReportsWithDiagrams(_features, Start, Start.AddMinutes(1), options);
    }

    /// <summary>Two earlier runs on the ledger, so the run under test reads real verdicts and a two-run sparkline.</summary>
    private void Seed()
    {
        Run("seed1", ledger: true, runId: "clean:0:1");
        Run("seed2", ledger: true, runId: "clean:0:2");
    }

    [Fact]
    public void By_default_a_run_that_read_history_and_recorded_diagnostics_writes_the_same_reports_as_one_that_did_neither()
    {
        Run("plain", ledger: false);
        Seed();
        Run("full", ledger: true, diagnostics: 2);

        // The run did read history and record its diagnostics: they are on the labs page beside the report.
        Assert.Contains("<details id=\"history-section\"", Report("full", "TestRunReport.labs.html"));
        Assert.Contains("a tap dropped 4 segments", Report("full", "TestRunReport.labs.html"));

        Assert.Equal(Report("plain"), Report("full"));
        Assert.Equal(Report("plain", "Specifications.html"), Report("full", "Specifications.html"));
    }

    [Fact]
    public void A_default_report_names_no_history_or_diagnostics_anywhere_in_the_file()
    {
        Seed();
        Run("full", ledger: true, diagnostics: 2);

        // The whole file, scripts and styles included. Not `history`: the scripts call history.replaceState.
        var html = Report("full");
        Assert.DoesNotContain("history-", html);
        Assert.DoesNotContain("data-history-verdicts", html);
        Assert.DoesNotContain("report-diagnostic", html);
        Assert.DoesNotContain("verdicts.has(", html);
    }

    [Fact]
    public void ShowScenarioHistory_brings_back_the_sparkline_pill_attribute_verdict_code_and_their_css_and_no_section()
    {
        Seed();
        Run("scenario", ledger: true, diagnostics: 2, configure: o => o.ShowScenarioHistory = true);

        var html = Report("scenario");
        Assert.Contains("<span class=\"history-sparkline\"", html);
        Assert.Contains("data-history-verdicts=\"", html);
        Assert.Contains("verdicts.has(ast.value)", html);
        Assert.Contains("hv ? new Set(hv.split(','))", html);
        Assert.Contains(".history-sparkline {", html);
        Assert.DoesNotContain("<details id=\"history-section\"", html);
        Assert.DoesNotContain("report-diagnostic", html);
    }

    [Fact]
    public void ShowHistorySection_brings_back_the_section_and_its_css_and_no_verdict_code()
    {
        Seed();
        Run("section", ledger: true, configure: o => o.ShowHistorySection = true);

        var html = Report("section");
        Assert.Contains("<details id=\"history-section\"", html);
        Assert.Contains(".history-section", html);
        Assert.DoesNotContain("data-history-verdicts", html);
        Assert.DoesNotContain("<span class=\"history-sparkline\"", html);
        Assert.DoesNotContain("verdicts.has(", html);
    }

    [Fact]
    public void ShowReportDiagnosticsSection_brings_back_the_section_and_its_css_and_no_history()
    {
        Seed();
        Run("diagnostics", ledger: true, diagnostics: 2, configure: o => o.ShowReportDiagnosticsSection = true);

        var html = Report("diagnostics");
        Assert.Contains("<details class=\"report-diagnostics\"", html);
        Assert.Contains(".report-diagnostics", html);
        Assert.DoesNotContain("history-", html);
        Assert.DoesNotContain("verdicts.has(", html);
    }

    [Fact]
    public void A_report_that_asks_for_the_diagnostics_section_with_nothing_to_list_carries_neither_section_nor_css()
    {
        Run("healthy", ledger: false, configure: o => o.ShowReportDiagnosticsSection = true);

        Assert.DoesNotContain("report-diagnostic", Report("healthy"));
    }

    [Fact]
    public void EmbedHistoryInReport_off_keeps_history_out_of_the_report_and_the_page_whatever_the_opt_ins_say()
    {
        Seed();
        Run("off", ledger: true, diagnostics: 1, configure: o =>
        {
            o.EmbedHistoryInReport = false;
            o.ShowScenarioHistory = true;
            o.ShowHistorySection = true;
        });

        Assert.DoesNotContain("history-", Report("off"));
        Assert.DoesNotContain("verdicts.has(", Report("off"));
        var page = Report("off", "TestRunReport.labs.html");
        Assert.DoesNotContain("<details id=\"history-section\"", page);
        Assert.DoesNotContain("<span class=\"history-sparkline\"", page);
    }

    [Fact]
    public void With_every_opt_in_on_the_report_carries_both_sections_and_the_scenario_history()
    {
        Seed();
        Run("all", ledger: true, diagnostics: 1, configure: o =>
        {
            o.ShowScenarioHistory = true;
            o.ShowHistorySection = true;
            o.ShowReportDiagnosticsSection = true;
        });

        var html = Report("all");
        Assert.Contains("<details id=\"history-section\"", html);
        Assert.Contains("<span class=\"history-sparkline\"", html);
        Assert.Contains("data-history-verdicts=\"", html);
        Assert.Contains("verdicts.has(ast.value)", html);
        Assert.Contains("<details class=\"report-diagnostics\"", html);
    }
}
