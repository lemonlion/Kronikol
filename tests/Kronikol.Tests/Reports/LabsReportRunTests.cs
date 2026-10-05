using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The labs page through a run (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md sections 3.3 and 3.7): written beside the
/// report by default whenever the run read history or recorded a diagnostic, named after the report, listed in
/// <c>Run.json</c> and by the pointer after the report, and never written with nothing to show. Each run names its
/// ledger, because the test process has <c>KRONIKOL_HISTORY=off</c>.
/// </summary>
[Collection("DiagramsFetcher")]
public class LabsReportRunTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-labs-run").FullName;

    public LabsReportRunTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Ledger => Path.Combine(_dir, "ledger", "history.jsonl");

    private string Reports(string run) => Path.Combine(_dir, run);

    private string Page(string run, string name = "TestRunReport.labs.html") => Path.Combine(Reports(run), name);

    /// <summary>Nothing about the page or the report's opt-ins is set: what a consumer gets by default.</summary>
    private ReportConfigurationOptions Options(string run) => new()
    {
        ReportsFolderPath = Reports(run),
        SuiteName = "LabsSuite",
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
        WriteRunSummaryToConsole = false,
        HistoryMinRuns = 2,
        HistoryBranch = ""
    };

    private static Feature[] Features(ExecutionResult pay) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "labs-" + Guid.NewGuid().ToString("N"), DisplayName = "Pay with an expired card", Result = pay,
                    ErrorMessage = pay == ExecutionResult.Failed ? "Expected 200 but got 500" : null,
                    Duration = TimeSpan.FromMilliseconds(120)
                }
            ]
        },
        new Feature
        {
            DisplayName = "Refunds",
            Scenarios = [new Scenario { Id = "labs-" + Guid.NewGuid().ToString("N"), DisplayName = "Refund a paid order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(40) }]
        }
    ];

    /// <summary>One run. <paramref name="ledger"/> names the ledger it reads and appends to; <paramref name="diagnostic"/>
    /// is recorded by the host before the run, as an ingest's malformed line is.</summary>
    private string Run(string run, string runId, ExecutionResult pay = ExecutionResult.Passed, bool ledger = true, string? diagnostic = null,
        Action<ReportConfigurationOptions>? configure = null)
    {
        var options = Options(run);
        options.HistoryRunId = runId;
        if (ledger)
            options.HistoryFilePath = Ledger;
        configure?.Invoke(options);

        var collector = new ReportDiagnosticsCollector();
        if (diagnostic is not null)
            collector.Add(DiagnosticKind.CaptureDegraded, diagnostic);

        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            using (ReportDiagnosticsScope.Begin(collector))
                ReportGenerator.CreateStandardReportsWithDiagrams(Features(pay), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
        }
        finally
        {
            Console.SetOut(original);
        }

        return captured.ToString();
    }

    private static string[] TopLevel(string directory) =>
        Directory.GetFiles(directory).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void With_no_option_set_a_run_that_read_history_and_recorded_a_diagnostic_writes_the_page_beside_its_report()
    {
        Run("r1", "labs:1:1");
        Run("r2", "labs:2:1", ExecutionResult.Failed, diagnostic: "a tap dropped 3 segments");

        var page = File.ReadAllText(Page("r2"));
        Assert.Contains("<details id=\"history-section\" class=\"history-section\" open>", page);
        Assert.Contains("<span class=\"history-sparkline\"", page);
        Assert.Contains("<span class=\"history-verdict history-verdict-broke\"", page);
        Assert.Contains("<details class=\"report-diagnostics\" open>", page);
        Assert.Contains("a tap dropped 3 segments", page);
        // Linked into the report beside it by the anchor the report resolves on load.
        Assert.Contains("<a class=\"history-link\" href=\"TestRunReport.html#sid-", page);
        Assert.Contains("TestRunReport.labs.html", RunManifest.TryRead(Path.Combine(Reports("r2"), RunManifest.FileName))!.Files);
    }

    [Fact]
    public void The_page_is_named_after_the_report_and_links_to_it_by_that_name()
    {
        Run("c1", "labs:1:1", configure: o => o.HtmlTestRunReportFileName = "Checkout");
        Run("c2", "labs:2:1", ExecutionResult.Failed, configure: o => o.HtmlTestRunReportFileName = "Checkout");

        Assert.False(File.Exists(Page("c2")), "the page took the default report's name");
        var page = File.ReadAllText(Page("c2", "Checkout.labs.html"));
        Assert.Contains("<a class=\"history-link\" href=\"Checkout.html#sid-", page);
        Assert.Contains("<a class=\"labs-back\" href=\"Checkout.html\">", page);
        Assert.DoesNotContain("TestRunReport.html", page);
    }

    [Fact]
    public void Without_a_report_the_page_is_still_written_and_names_its_scenarios_as_text()
    {
        Run("t1", "labs:1:1", configure: o => o.GenerateTestRunReport = false);
        Run("t2", "labs:2:1", ExecutionResult.Failed, configure: o => o.GenerateTestRunReport = false);

        Assert.False(File.Exists(Path.Combine(Reports("t2"), "TestRunReport.html")));
        var page = File.ReadAllText(Page("t2"));
        Assert.Contains("<span class=\"history-name\">Checkout &rsaquo; Pay with an expired card</span>", page);
        Assert.DoesNotContain("<a ", page);
    }

    [Fact]
    public void A_run_with_nothing_for_the_page_writes_none()
    {
        // No ledger read (history is off in this process unless a ledger is named) and no diagnostic.
        Run("n1", "labs:1:1", ledger: false);

        Assert.True(File.Exists(Path.Combine(Reports("n1"), "TestRunReport.html")));
        Assert.DoesNotContain(TopLevel(Reports("n1")), name => name.EndsWith(".labs.html", StringComparison.Ordinal));
        Assert.DoesNotContain(RunManifest.TryRead(Path.Combine(Reports("n1"), RunManifest.FileName))!.Files, f => f.EndsWith(".labs.html", StringComparison.Ordinal));
    }

    [Fact]
    public void Switched_off_there_is_no_page_whatever_the_run_has()
    {
        Run("o1", "labs:1:1", configure: o => o.GenerateLabsReport = false);
        Run("o2", "labs:2:1", ExecutionResult.Failed, diagnostic: "a tap dropped 3 segments", configure: o => o.GenerateLabsReport = false);

        Assert.False(File.Exists(Page("o2")));
    }

    [Fact]
    public void Diagnostics_alone_give_a_page_with_no_history_and_history_alone_one_with_no_diagnostics()
    {
        Run("d1", "labs:1:1", ledger: false, diagnostic: "a tap dropped 3 segments");
        var diagnosticsOnly = File.ReadAllText(Page("d1"));
        Assert.Contains("<details class=\"report-diagnostics\" open>", diagnosticsOnly);
        Assert.DoesNotContain("<details id=\"history-section\"", diagnosticsOnly);
        Assert.DoesNotContain("<span class=\"history-sparkline\"", diagnosticsOnly);

        Run("h1", "labs:2:1");
        Run("h2", "labs:3:1", ExecutionResult.Failed);
        var historyOnly = File.ReadAllText(Page("h2"));
        Assert.Contains("<details id=\"history-section\"", historyOnly);
        Assert.DoesNotContain("<details class=\"report-diagnostics\"", historyOnly);
    }

    [Fact]
    public void EmbedHistoryInReport_off_keeps_history_off_the_page_too()
    {
        Run("e1", "labs:1:1", configure: o => o.EmbedHistoryInReport = false);
        Run("e2", "labs:2:1", ExecutionResult.Failed, configure: o => o.EmbedHistoryInReport = false);
        Assert.False(File.Exists(Page("e2")), "a page was written for history the run was told not to embed");

        Run("e3", "labs:3:1", ExecutionResult.Failed, diagnostic: "a tap dropped 3 segments", configure: o => o.EmbedHistoryInReport = false);
        var page = File.ReadAllText(Page("e3"));
        Assert.DoesNotContain("<details id=\"history-section\"", page);
        Assert.DoesNotContain("<span class=\"history-sparkline\"", page);
        Assert.Contains("<details class=\"report-diagnostics\" open>", page);
        // The ledger still saw the runs: embedding is about the HTML, not about recording.
        Assert.Contains("labs:3:1", File.ReadAllText(Ledger));
    }

    [Fact]
    public void A_compare_branch_is_read_out_on_the_page()
    {
        Run("x1", "labs:1:1");
        Run("x2", "labs:2:1", configure: o => o.HistoryCompareBranch = "trunk");

        var page = File.ReadAllText(Page("x2"));
        Assert.Contains("class=\"history-compare\"", page);
        Assert.Contains("on <code>trunk</code>", page);
    }

    [Fact]
    public void The_pointer_lists_the_page_after_the_report_and_its_never_open_line_still_names_the_report()
    {
        Run("p1", "labs:1:1");
        var pointer = Run("p2", "labs:2:1", ExecutionResult.Failed, configure: o => o.WriteRunSummaryToConsole = true);

        var report = pointer.IndexOf("TestRunReport.html", StringComparison.Ordinal);
        var page = pointer.IndexOf("TestRunReport.labs.html", StringComparison.Ordinal);
        Assert.True(report >= 0, pointer);
        Assert.True(page > report, $"the page is not listed after the report:\n{pointer}");
        Assert.DoesNotContain("never open TestRunReport.labs.html", pointer);
    }

    [Fact]
    public void The_debug_section_never_tells_a_reader_not_to_open_the_page()
    {
        // With the report switched off the page is the only HTML the run wrote, and the "do not open" line took the
        // first .html it was handed: the small page an agent may read, named as the multi-megabyte report.
        var dir = Reports("ci");
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "TestRunReport.json", "Failures.md", "TestRunReport.labs.html" })
            File.WriteAllText(Path.Combine(dir, name), "x");
        var summary = RunSummaryConsoleWriter.Summarise(Features(ExecutionResult.Failed), dir, ["TestRunReport.json", "Failures.md", "TestRunReport.labs.html"], agentInstructionsWritten: false);

        var section = RunSummaryConsoleWriter.BuildCiSummarySection(summary);
        Assert.Contains("Do not open `TestRunReport.json` or `TestRunReport.html`", section);
    }
}
