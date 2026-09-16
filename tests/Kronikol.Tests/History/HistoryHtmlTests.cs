using System.Text.RegularExpressions;
using Kronikol.History;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.History;

/// <summary>
/// History in the HTML report (plans/CROSS_RUN_HISTORY_PLAN.md §8.1): each scenario carries its verdicts
/// as an attribute the search box reads, a sparkline of its last runs and a verdict pill; a History
/// section beside the timeline lists what changed and links each scenario by its stable id; and a
/// report rendered with no history is byte for byte what it was before.
/// </summary>
public class HistoryHtmlTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-history-html").FullName;
    private const string Suite = "HtmlSuite";
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private static string PayId => ScenarioStableId.Compute(Suite, "Checkout", "Pay by card");
    private static string RefundId => ScenarioStableId.Compute(Suite, "Checkout", "Refund an order");

    private static Feature[] Features(ExecutionResult pay = ExecutionResult.Failed, ExecutionResult refund = ExecutionResult.Passed) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario { Id = "t-pay", DisplayName = "Pay by card", Result = pay, Duration = TimeSpan.FromMilliseconds(120), ErrorMessage = pay == ExecutionResult.Failed ? "Expected 200 but got 500" : null },
                new Scenario { Id = "t-refund", DisplayName = "Refund an order", Result = refund, Duration = TimeSpan.FromMilliseconds(40) }
            ]
        },
        new Feature
        {
            DisplayName = "Search",
            Scenarios = [new Scenario { Id = "t-find", DisplayName = "Find a product", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(70) }]
        }
    ];

    /// <summary>Seeds the ledger with prior runs over the same roster and analyses the current run against them.</summary>
    private HistoryVerdicts Verdicts(Feature[] features, params string[] priorResults) => VerdictsInto(Ledger, features, priorResults);

    // The ledger path comes FIRST: with it second, `Verdicts(features, "PPP", "PPP")` resolved to this
    // overload with "PPP" as the path, and every test appended to one file in the working directory.
    private static HistoryVerdicts VerdictsInto(string ledgerPath, Feature[] features, params string[] priorResults)
    {
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
            var appended = HistoryLedgerWriter.Append(ledgerPath, roster, prior, "3.11.0");
            Assert.Equal(HistoryAppendOutcome.Appended, appended.Outcome);
        }
        var ledger = HistoryLedgerReader.Read(ledgerPath, 50).Ledger!;
        return HistoryAnalyzer.Analyse(ledger, roster, run, new HistoryAnalysisOptions { MinRuns = 2 });
    }

    private string Html(Feature[] features, HistoryVerdicts? history, string name = "TestRunReport.html")
    {
        var diagrams = features.SelectMany(f => f.Scenarios).Select(s => new DiagramAsCode(s.Id, "", "@startuml\nA->B\n@enduml")).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(diagrams, features, At.UtcDateTime.AddMinutes(-1), At.UtcDateTime, null,
            // The tests here are about what history renders, so they ask for the section a plain report leaves out.
            Path.Combine(_dir, name), "History HTML", true, suite: Suite, history: history, showHistorySection: true);
        return File.ReadAllText(path);
    }

    /// <summary>The scenario element's opening tag and summary, for assertions about one scenario rather than the whole file.</summary>
    private static string ScenarioHead(string html, string stableId)
    {
        var match = Regex.Match(html, "<details class=\"scenario[^\"]*\"[^>]*data-stable-id=\"" + stableId + "\"[^>]*>.*?</summary>", RegexOptions.Singleline);
        Assert.True(match.Success, "no scenario element for " + stableId);
        return match.Value;
    }

    [Fact]
    public void A_regression_carries_its_verdict_a_sparkline_and_a_pill_beside_its_duration()
    {
        var features = Features(pay: ExecutionResult.Failed);
        var html = Html(features, Verdicts(features, "PPP", "PPP", "PPP"));

        var pay = ScenarioHead(html, PayId);
        Assert.Contains("data-history-verdicts=\"broke\"", pay);
        Assert.Contains("class=\"history-sparkline\"", pay);
        Assert.Contains("linear-gradient(90deg,", pay);
        Assert.Contains("#bf0000 75%,#bf0000 100%", pay); // the current failure is the rightmost stop
        Assert.Contains("PPPF", pay); // the tooltip carries the series
        Assert.Contains("broke: passed in test:3:1", pay);
        Assert.Contains("history-verdict-broke\"", pay);
        Assert.Contains(">broke</span>", pay);
        // The sparkline sits beside the duration badge, inside the summary, so a filtered export carries it.
        Assert.True(pay.IndexOf("duration-badge", StringComparison.Ordinal) < pay.IndexOf("history-sparkline", StringComparison.Ordinal));

        var refund = ScenarioHead(html, RefundId);
        Assert.Contains("data-history-verdicts=\"stable\"", refund);
        Assert.Contains("history-sparkline", refund);
        Assert.DoesNotContain("history-verdict-", refund);
    }

    [Fact]
    public void The_history_section_lists_the_regression_and_links_it_by_stable_id()
    {
        var features = Features(pay: ExecutionResult.Failed);
        var html = Html(features, Verdicts(features, "PPP", "PPP", "PPP"));

        var section = Regex.Match(html, "<details id=\"history-section\"[^>]*>.*?</details>", RegexOptions.Singleline);
        Assert.True(section.Success, "no history section");
        Assert.Contains(" open>", section.Value[..80]);
        Assert.Contains("1 broke (against 3 earlier runs on local)", section.Value);
        Assert.Contains("New failures", section.Value);
        Assert.Contains($"href=\"#sid-{PayId}\"", section.Value);
        Assert.Contains("Checkout &rsaquo; Pay by card", section.Value);
        Assert.Contains("class=\"history-chart\"", section.Value);
        Assert.Contains("Pass rate", section.Value);
        Assert.Equal(4, Regex.Matches(section.Value, "<rect class=\"history-bar history-bar-(pass|fail)").Count); // three prior runs and this one
        // The section sits at the top level, beside the timeline, not inside a feature: the export copies
        // features only and leaves the aggregate behind on purpose.
        Assert.True(html.IndexOf("<details id=\"history-section\"", StringComparison.Ordinal) < html.IndexOf("<div id=\"report-content\">", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fixed_scenario_is_listed_as_newly_fixed_and_a_stable_run_leaves_the_section_closed()
    {
        var fixedRun = Features(pay: ExecutionResult.Passed);
        var fixedHtml = Html(fixedRun, Verdicts(fixedRun, "FPP", "FPP", "FPP"));
        Assert.Contains("data-history-verdicts=\"fixed\"", ScenarioHead(fixedHtml, PayId));
        Assert.Contains("Newly fixed", fixedHtml);

        var quiet = Features(pay: ExecutionResult.Passed);
        var quietHtml = Html(quiet, VerdictsInto(Path.Combine(_dir, "quiet", "history.jsonl"), quiet, "PPP", "PPP", "PPP"), "Quiet.html");
        var section = Regex.Match(quietHtml, "<details id=\"history-section\"[^>]*>", RegexOptions.Singleline);
        Assert.True(section.Success);
        Assert.DoesNotContain(" open", section.Value);
        Assert.Contains("nothing changed (against 3 earlier runs on local)", quietHtml);
    }

    [Fact]
    public void Without_history_nothing_about_history_reaches_the_html()
    {
        var html = Html(Features(), null);

        // The stylesheet and the search script name these classes and attributes whether or not the run
        // had history; what must be absent is the markup.
        Assert.DoesNotContain("class=\"history-sparkline\"", html);
        Assert.DoesNotContain("<details id=\"history-section\"", html);
        Assert.DoesNotContain("data-history-verdicts=\"", html);
        Assert.DoesNotContain("class=\"history-verdict ", html);
    }

    [Fact]
    public void A_parameterised_group_carries_the_union_of_its_rows_verdicts_and_each_row_its_own()
    {
        Feature[] Outline(ExecutionResult visa) =>
        [
            new Feature
            {
                DisplayName = "Cards",
                Scenarios =
                [
                    new Scenario { Id = "row-visa", DisplayName = "Pay with card", OutlineId = "pay-with-card", ExampleValues = new Dictionary<string, string> { ["card"] = "visa" }, Result = visa, ErrorMessage = visa == ExecutionResult.Failed ? "declined" : null, Duration = TimeSpan.FromMilliseconds(10) },
                    new Scenario { Id = "row-amex", DisplayName = "Pay with card", OutlineId = "pay-with-card", ExampleValues = new Dictionary<string, string> { ["card"] = "amex" }, Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }
                ]
            }
        ];

        var features = Outline(ExecutionResult.Failed);
        var html = Html(features, Verdicts(features, "PP", "PP", "PP"));

        var group = Regex.Match(html, "<details class=\"scenario scenario-parameterized[^\"]*\"[^>]*>.*?</summary>", RegexOptions.Singleline);
        Assert.True(group.Success, "no parameterised group");
        Assert.Contains("data-history-verdicts=\"broke,stable\"", group.Value);
        Assert.Contains("history-verdict-broke\"", group.Value);

        var visaId = ScenarioStableId.Compute(Suite, "Cards", "Pay with card", "pay-with-card", new Dictionary<string, string> { ["card"] = "visa" });
        var rows = Regex.Matches(html, "<tr [^>]*data-stable-id=\"" + visaId + "\"[^>]*>");
        Assert.True(rows.Count >= 1, "no row for the visa example");
        Assert.All(rows, r => Assert.Contains("data-history-verdicts=\"broke\"", r.Value));
    }
}
