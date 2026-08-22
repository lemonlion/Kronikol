using System.Text.RegularExpressions;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Part E, end to end: after a report is generated, every step line and every assertion note in it reads
/// as a sentence — and the diagram and the step list agree, because both go through the same rule.
/// </summary>
[Collection("DiagramsFetcher")]
public class StepCapitalisationReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-caps-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
    private const string TestId = "55555555555555555555555555555555";

    public StepCapitalisationReportTests()
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

    /// <summary>A run with the label shapes the Playwright reporter actually produces.</summary>
    private IngestResult Run(string subdirectory, bool capitalise = true)
    {
        var (request, response) = InteractionRecord.Pair(TestId, "overview renders", "GET", "http://localhost/overview",
            "api", "web", responseContent: "{}", statusCode: "200",
            requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(2));

        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, subdirectory);
        options.GenerateComponentDiagram = false;
        options.CapitaliseStepText = capitalise;
        options.CapitaliseTitles = capitalise;

        return IngestPipeline.Run(new IngestRequest
        {
            Interactions = [request, response],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = TestId, TestName = "overview renders", Timestamp = T0 },
                new TestRunRecord { Event = "step", TestId = TestId, Keyword = "Given", Text = "the mock is armed", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "step", TestId = TestId, Text = "the user accepts the trial", Timestamp = T0.AddSeconds(2) },
                new TestRunRecord { Event = "assertion", TestId = TestId, Text = "the trial banner is visible", Status = "passed", Timestamp = T0.AddSeconds(3) },
                new TestRunRecord { Event = "assertion", TestId = TestId, Text = "\"Deep dive\" is not listed", Status = "failed", Error = "it was listed", Timestamp = T0.AddSeconds(4) },
                new TestRunRecord { Event = "end", TestId = TestId, Status = "failed", Error = "boom", Timestamp = T0.AddSeconds(5) },
            ],
            Options = options,
        });
    }

    [Fact]
    public void Every_rendered_step_line_starts_with_a_capital_or_a_quoted_literal()
    {
        var html = File.ReadAllText(Run("on").TestRunReportHtml);

        // A Gherkin step renders its keyword in its own span before the text, and that keyword is what
        // the reader sees first — so the line is judged on whichever comes first.
        var stepLines = Regex.Matches(html,
                "(?:<span class=\"step-keyword\">(?<keyword>[^<]*)</span> )?<span class=\"step-text\">(?<text>[^<]*)</span>")
            .Select(m => (
                Keyword: System.Net.WebUtility.HtmlDecode(m.Groups["keyword"].Value),
                Text: System.Net.WebUtility.HtmlDecode(m.Groups["text"].Value)))
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .ToArray();

        Assert.NotEmpty(stepLines);
        Assert.All(stepLines, line => Assert.True(
            StepText.StartsWithCapitalOrQuote(line.Keyword, line.Text),
            $"step line does not read as a sentence: {line.Keyword} {line.Text}"));
    }

    [Fact]
    public void The_keyword_is_what_the_reader_sees_first_so_its_text_keeps_the_author_s_casing()
    {
        var result = Run("keyword");
        var steps = result.Features[0].Scenarios[0].Steps!;

        Assert.Equal("Given", steps[0].Keyword);
        Assert.Equal("the mock is armed", steps[0].Text);
        Assert.Equal("The user accepts the trial", steps[1].Text);
    }

    [Fact]
    public void Assertion_notes_in_the_diagram_agree_with_the_step_list()
    {
        var result = Run("diagram");

        var scenario = result.Features[0].Scenarios[0];
        var assertions = scenario.Steps!.SelectMany(s => s.SubSteps ?? []).Select(s => s.Text).ToArray();
        Assert.Contains("✓ The trial banner is visible", assertions);
        // A quoted literal is the producer's content and stays exactly as written.
        Assert.Contains("✗ \"Deep dive\" is not listed", assertions);

        var diagram = string.Join("\n", DiagramsFor(result.ReportsDirectory, scenario.Id));
        foreach (var line in diagram.Split('\n').Where(l => l.StartsWith("✓ ") || l.StartsWith("✗ ")))
        {
            Assert.True(StepText.StartsWithCapitalOrQuote(null, line),
                $"assertion note does not read as a sentence: {line}");
        }

        Assert.Contains("✓ The trial banner is visible", diagram);
    }

    [Fact]
    public void A_clean_run_reports_no_lowercase_steps_at_all()
    {
        // What the live bar asserts: with the rule on, nothing is left for a reader to trip over — the
        // quoted literal the rule refuses to touch reads as a sentence too, so it is not a violation.
        Assert.DoesNotContain(Run("clean").Diagnostics, d => d.Kind == DiagnosticKind.StepsNotStartingWithCapital);
    }

    [Fact]
    public void With_the_rule_off_the_diagnostic_names_the_labels_that_do_not_read_as_sentences()
    {
        var result = Run("diagnostics", capitalise: false);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Kind == DiagnosticKind.StepsNotStartingWithCapital);
        Assert.Contains("2 step text(s) do not start with a capital letter", diagnostic.Message);
        Assert.Contains("the user accepts the trial", diagnostic.Message);
        Assert.Contains("✓ the trial banner is visible", diagnostic.Message);
        // The Gherkin step is judged on its keyword, and the quoted literal is the producer's content.
        Assert.DoesNotContain("the mock is armed", diagnostic.Message);
        Assert.DoesNotContain("Deep dive", diagnostic.Message);
    }

    [Fact]
    public void Turning_the_rule_off_leaves_every_label_exactly_as_the_producer_wrote_it()
    {
        var result = Run("off", capitalise: false);

        var scenario = result.Features[0].Scenarios[0];
        Assert.Equal("the user accepts the trial", scenario.Steps![1].Text);
        Assert.Contains("✓ the trial banner is visible", DiagramsFor(result.ReportsDirectory, scenario.Id));
    }

    [Fact]
    public void The_html_json_xml_and_yaml_views_of_a_step_all_agree()
    {
        var json = Run("agree-json");
        var scenario = json.Features[0].Scenarios[0];
        Assert.Contains("The user accepts the trial", File.ReadAllText(json.TestRunReportHtml));
        Assert.Contains("\"text\": \"The user accepts the trial\"",
            File.ReadAllText(Path.Combine(json.ReportsDirectory, "TestRunReport.json")));

        // The model is capitalised once, before any format is written, so the other two cannot disagree.
        Assert.Equal("The user accepts the trial", scenario.Steps![1].Text);
    }

    private static string DiagramsFor(string reportsDirectory, string scenarioId)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(reportsDirectory, "TestRunReport.json")));

        return string.Join("\n", document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Where(s => s.GetProperty("id").GetString() == scenarioId)
            .SelectMany(s => s.GetProperty("diagrams").EnumerateArray())
            .Select(d => d.GetString()));
    }
    [Fact]
    public void Scenario_titles_read_as_sentences_in_every_view_and_the_rule_can_be_turned_off()
    {
        // The producer named the test "overview renders" (a Gherkin "Scenario: overview renders"):
        // the report's headings capitalise it, once, before any format is written.
        var on = Run("titles-on");
        var scenario = on.Features[0].Scenarios[0];
        Assert.Equal("Overview renders", scenario.DisplayName);
        Assert.Contains("Overview renders", File.ReadAllText(on.TestRunReportHtml));
        Assert.Contains("\"name\": \"Overview renders\"",
            File.ReadAllText(Path.Combine(on.ReportsDirectory, "TestRunReport.json")));
        Assert.DoesNotContain(on.Diagnostics, d => d.Kind == DiagnosticKind.TitlesNotStartingWithCapital);

        var off = Run("titles-off", capitalise: false);
        Assert.Equal("overview renders", off.Features[0].Scenarios[0].DisplayName);
        var diagnostic = Assert.Single(off.Diagnostics, d => d.Kind == DiagnosticKind.TitlesNotStartingWithCapital);
        Assert.Contains("1 feature/rule/scenario title(s) do not start with a capital letter", diagnostic.Message);
        Assert.Contains("overview renders", diagnostic.Message);
    }
}
