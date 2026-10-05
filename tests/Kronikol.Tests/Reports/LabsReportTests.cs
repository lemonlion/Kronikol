using System.Text.RegularExpressions;
using Kronikol.History;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The labs page, <c>{report}.labs.html</c> beside the report (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md
/// section 3.4): the History section, every scenario's sparkline and verdict, and the run's report diagnostics, drawn
/// by the renderers a report's opt-ins use. It has no script; its scenario names link into the report by the
/// <c>#sid-</c> anchor the report resolves on load.
/// </summary>
public class LabsReportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-labs").FullName;
    private const string Suite = "LabsSuite";
    private static readonly DateTimeOffset At = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static string PayId => ScenarioStableId.Compute(Suite, "Checkout", "Pay by card");
    private static string RefundId => ScenarioStableId.Compute(Suite, "Checkout", "Refund an order");
    private static string FindId => ScenarioStableId.Compute(Suite, "Search", "Find a product");

    private static readonly DiagnosticEntry RenderFailure = new(DiagnosticKind.RenderFailure, "the render process did not answer", "t-pay");
    private static readonly DiagnosticEntry DeadTap = new(DiagnosticKind.CaptureDegraded, "tap-di-redis: decoding disabled on 1 connection(s)");

    private static Feature[] Features(ExecutionResult pay = ExecutionResult.Failed) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario { Id = "t-pay", DisplayName = "Pay by card", Result = pay, Duration = TimeSpan.FromMilliseconds(120), ErrorMessage = pay == ExecutionResult.Failed ? "Expected 200 but got 500" : null },
                new Scenario { Id = "t-refund", DisplayName = "Refund an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(40) }
            ]
        },
        new Feature
        {
            DisplayName = "Search",
            Scenarios = [new Scenario { Id = "t-find", DisplayName = "Find a product", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(70) }]
        }
    ];

    // The ledger path comes first (see HistoryHtmlTests.VerdictsInto for why).
    private HistoryVerdicts Verdicts(Feature[] features, params string[] priorResults)
    {
        var ledgerPath = Path.Combine(_dir, Guid.NewGuid().ToString("N"), "history.jsonl");
        var (roster, run) = HistoryRunBuilder.Build(features, [], Suite, null, At, new HistoryBuildOptions(), "test:99:1");
        for (var i = 0; i < priorResults.Length; i++)
        {
            var results = priorResults[i];
            var prior = run with
            {
                Id = $"test:{i + 1}:1", At = At.AddHours(i - priorResults.Length), Commit = $"c{i + 1:D6}",
                Results = results, Attempts = new string('-', results.Length),
                Durations = Enumerable.Repeat<int?>(100, results.Length).ToArray(),
                Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
                ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "boom" } : new Dictionary<string, string>()
            };
            Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(ledgerPath, roster, prior, "4.5.1").Outcome);
        }
        var ledger = HistoryLedgerReader.Read(ledgerPath, 50).Ledger!;
        return HistoryAnalyzer.Analyse(ledger, roster, run, new HistoryAnalysisOptions { MinRuns = 2 });
    }

    private HistoryVerdicts Broke() => Verdicts(Features(), "PPP", "PPP", "PPP");

    private static string Page(HistoryVerdicts? history, IReadOnlyList<DiagnosticEntry> diagnostics, Feature[]? features = null,
        string? reportHref = "TestRunReport.html", string? customCss = null) =>
        LabsReportGenerator.Build(new LabsPage("Checkout - Test Run Report", features ?? Features(), Suite, history, diagnostics,
            reportHref, RunId: "test:99:1", EndedAt: At, CustomCss: customCss));

    private static string Table(string page)
    {
        var match = Regex.Match(page, "<details class=\"labs-scenarios\">.*?</details>", RegexOptions.Singleline);
        Assert.True(match.Success, "no table of every scenario");
        return match.Value;
    }

    private static string[] Rows(string table) =>
        Regex.Matches(table, "<tr class=\"labs-row\">.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).ToArray();

    [Fact]
    public void A_page_holds_the_history_section_open_a_sparkline_per_scenario_and_the_diagnostics_open()
    {
        var page = Page(Broke(), [RenderFailure, DeadTap]);

        Assert.Contains("<details id=\"history-section\" class=\"history-section\" open>", page);
        Assert.Equal(3, Regex.Matches(page, "<span class=\"history-sparkline\"").Count);
        Assert.Contains("<span class=\"history-verdict history-verdict-broke\"", page);
        Assert.Contains("<details class=\"report-diagnostics\" open>", page);
        Assert.Contains("<span class=\"report-diagnostic-kind report-diagnostic-kind-renderfailure\">RenderFailure</span>", page);
    }

    [Fact]
    public void The_section_is_open_even_when_nothing_changed()
    {
        // The report's section opens only when there is something to say; the page is where history is read, so
        // its section always is.
        var stable = Verdicts(Features(pay: ExecutionResult.Passed), "PPP", "PPP", "PPP");
        Assert.False(stable.HasAnything);

        Assert.Contains("<details id=\"history-section\" class=\"history-section\" open>", Page(stable, []));
    }

    [Fact]
    public void The_names_link_into_the_report_by_stable_id_and_the_page_has_no_script()
    {
        var page = Page(Broke(), []);

        Assert.Contains($"<a class=\"history-link\" href=\"TestRunReport.html#sid-{PayId}\">Checkout &rsaquo; Pay by card</a>", page);
        Assert.Contains($"href=\"TestRunReport.html#sid-{RefundId}\"", Table(page));
        Assert.DoesNotContain("onclick", page);
        Assert.DoesNotContain("<script", page);
    }

    [Fact]
    public void A_report_named_otherwise_is_linked_by_its_own_name()
    {
        var page = Page(Broke(), [], reportHref: "Checkout.html");

        Assert.Contains($"href=\"Checkout.html#sid-{PayId}\"", page);
        Assert.Contains("<a class=\"labs-back\" href=\"Checkout.html\">", page);
        Assert.DoesNotContain("TestRunReport.html", page);
    }

    [Fact]
    public void With_no_report_beside_it_the_names_are_text()
    {
        var page = Page(Broke(), [], reportHref: null);

        Assert.DoesNotContain("<a ", page);
        Assert.Contains("Checkout &rsaquo; Pay by card", page);
        Assert.Contains("Refund an order", Table(page));
    }

    [Fact]
    public void History_alone_draws_no_diagnostics_and_diagnostics_alone_no_history()
    {
        var historyOnly = Page(Broke(), []);
        Assert.Contains("<details id=\"history-section\"", historyOnly);
        Assert.DoesNotContain("<details class=\"report-diagnostics\"", historyOnly);

        var diagnosticsOnly = Page(null, [DeadTap]);
        Assert.DoesNotContain("<details id=\"history-section\"", diagnosticsOnly);
        Assert.DoesNotContain("<details class=\"labs-scenarios\"", diagnosticsOnly);
        Assert.DoesNotContain("<span class=\"history-sparkline\"", diagnosticsOnly);
        Assert.Contains("<details class=\"report-diagnostics\" open>", diagnosticsOnly);
    }

    [Fact]
    public void A_page_has_something_to_show_only_with_history_or_a_diagnostic()
    {
        Assert.False(LabsReportGenerator.HasContent(null, []));
        Assert.True(LabsReportGenerator.HasContent(Broke(), []));
        Assert.True(LabsReportGenerator.HasContent(null, [DeadTap]));
    }

    [Fact]
    public void The_table_is_collapsed_and_lists_every_scenario_in_report_order_under_its_feature()
    {
        var table = Table(Page(Broke(), []));

        Assert.StartsWith("<details class=\"labs-scenarios\"><summary", table);
        var order = new[] { ">Checkout<", $"#sid-{PayId}", $"#sid-{RefundId}", ">Search<", $"#sid-{FindId}" }
            .Select(token => table.IndexOf(token, StringComparison.Ordinal)).ToArray();
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.Order().ToArray(), order);
        Assert.Equal(3, Rows(table).Length);

        var pay = Rows(table)[0];
        Assert.Contains("<span class=\"history-sparkline\"", pay);
        Assert.Contains("<span class=\"history-verdict history-verdict-broke\"", pay);
        Assert.Contains("<span class=\"labs-stable\">stable</span>", Rows(table)[1]);
    }

    [Fact]
    public void Each_outline_row_is_a_row_of_its_own_with_its_own_stable_id()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Inventory",
                Scenarios =
                [
                    new Scenario { Id = "o1", DisplayName = "Adjust stock(by: 1)", OutlineId = "Adjust stock", ExampleValues = new Dictionary<string, string> { ["by"] = "1" }, Result = ExecutionResult.Failed, ErrorMessage = "no" },
                    new Scenario { Id = "o2", DisplayName = "Adjust stock(by: 2)", OutlineId = "Adjust stock", ExampleValues = new Dictionary<string, string> { ["by"] = "2" }, Result = ExecutionResult.Passed },
                ]
            }
        ];
        var page = Page(Verdicts(features, "PP", "PP", "PP"), [], features);
        var rows = Rows(Table(page));

        Assert.Equal(2, rows.Length);
        Assert.Contains("#sid-" + ScenarioStableId.Compute(Suite, "Inventory", "Adjust stock(by: 1)", "Adjust stock", new Dictionary<string, string> { ["by"] = "1" }), rows[0]);
        Assert.Contains("#sid-" + ScenarioStableId.Compute(Suite, "Inventory", "Adjust stock(by: 2)", "Adjust stock", new Dictionary<string, string> { ["by"] = "2" }), rows[1]);
        Assert.Contains("history-verdict-broke", rows[0]);
        Assert.DoesNotContain("history-verdict-broke", rows[1]);
    }

    [Fact]
    public void Two_scenarios_sharing_a_stable_id_each_read_their_own_entry()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario { Id = "d1", DisplayName = "Pay twice", Result = ExecutionResult.Passed },
                    new Scenario { Id = "d2", DisplayName = "Pay twice", Result = ExecutionResult.Failed, ErrorMessage = "no" },
                ]
            }
        ];
        var rows = Rows(Table(Page(Verdicts(features, "PP", "PP", "PP"), [], features)));

        Assert.Equal(2, rows.Length);
        Assert.DoesNotContain("history-verdict-broke", rows[0]);
        Assert.Contains("history-verdict-broke", rows[1]);
    }

    [Fact]
    public void Names_evidence_messages_and_ids_are_encoded()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders <v2> & \"co\"",
                Scenarios = [new Scenario { Id = "e1", DisplayName = "Pay <now>", Result = ExecutionResult.Failed, ErrorMessage = "no" }]
            }
        ];
        var page = Page(Verdicts(features, "P", "P", "P"), [new DiagnosticEntry(DiagnosticKind.Other, "a <b> & c", "id<1>")], features);

        Assert.DoesNotContain("<v2>", page);
        Assert.DoesNotContain("<now>", page);
        Assert.DoesNotContain("<b>", page);
        Assert.DoesNotContain("id<1>", page);
        Assert.Contains("Orders &lt;v2&gt; &amp; &quot;co&quot;", page);
        Assert.Contains("Pay &lt;now&gt;", page);
        Assert.Contains("a &lt;b&gt; &amp; c", page);
        Assert.Contains("[id&lt;1&gt;]", page);
    }

    [Fact]
    public void The_page_carries_the_shared_sheet_both_feature_sheets_its_own_and_the_custom_css()
    {
        var page = Page(Broke(), [DeadTap], customCss: ".kron-custom-marker { color: red; }");

        Assert.Contains(Stylesheets.HtmlReportStyleSheet, page);
        Assert.Contains(Stylesheets.HistoryStyleSheet, page);
        Assert.Contains(Stylesheets.ReportDiagnosticsStyleSheet, page);
        Assert.Contains(Stylesheets.LabsStyleSheet, page);
        Assert.Contains("<style>.kron-custom-marker { color: red; }</style>", page);
        Assert.StartsWith("<!DOCTYPE html>", page);
    }

    [Fact]
    public void The_header_names_the_report_the_run_and_the_way_back()
    {
        var page = Page(Broke(), []);

        Assert.Contains("<title>Labs · Checkout - Test Run Report</title>", page);
        Assert.Contains("<h1>Checkout - Test Run Report <span class=\"labs-badge\">Labs</span></h1>", page);
        Assert.Contains("Labs: views still being designed. Their layout may change in any release.", page);
        Assert.Contains("suite <code>LabsSuite</code>", page);
        Assert.Contains("run <code>test:99:1</code>", page);
        Assert.Contains("ended 2026-10-05 12:00 UTC", page);
        Assert.Contains("<a class=\"labs-back\" href=\"TestRunReport.html\">", page);
    }

    [Fact]
    public void The_page_is_named_after_its_report()
    {
        Assert.Equal("TestRunReport.labs.html", LabsReportGenerator.FileName("TestRunReport"));
        Assert.Equal("Checkout.labs.html", LabsReportGenerator.FileName("Checkout"));
    }
}
