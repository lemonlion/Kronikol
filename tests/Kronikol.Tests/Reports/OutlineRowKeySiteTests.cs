using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.ComponentDiagram;
using Kronikol.History;
using Kronikol.ReqNRoll;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;
using Reqnroll;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// #101, facts K1 and D1 to D3, one output at a time: a Reqnroll outline row, named as Gherkin names its pickle, shows
/// that name, and every output that writes its stable id writes the id its title gave it (5.4.2's), so a site left on
/// the display name fails here by name (plans/OUTLINE_ROW_NAMES_PLAN.md 4.2 and 4.3). The rows come through the Reqnroll
/// converter, as the lane builds them. The merge's identity is held by <c>OutlineRowMergeTests</c>.
/// </summary>
[Collection("DiagramsFetcher")]
public class OutlineRowKeySiteTests : IDisposable
{
    internal const string Title = "A basket is weighed on the UK scale (<case>)";
    internal const string Filled = "A basket is weighed on the UK scale (two apples)";
    private const string Suite = "Probe101.xUnit2";
    private static readonly DateTimeOffset At = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-outline-row-sites-" + Guid.NewGuid().ToString("N"));

    public OutlineRowKeySiteTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    internal static Dictionary<string, string> TwoApples => new() { ["case"] = "two apples", ["count"] = "2", ["grams"] = "301" };

    /// <summary>The id 5.4.2 wrote for the failing basket row: its title, as both the name and the outline, and its values.</summary>
    internal static string TitleId(string? suite, string feature = "Baskets") => ScenarioStableId.Compute(suite, feature, Title, Title, TwoApples);

    /// <summary>The id the row would take if its filled name were hashed: what no output may write.</summary>
    private static string FilledId(string? suite) => ScenarioStableId.Compute(suite, "Baskets", Filled, Title, TwoApples);

    private static ReqNRollScenarioInfo Row(string id, string? rowName, Dictionary<string, string> values, bool failed) => new()
    {
        ScenarioId = id,
        ScenarioTitle = Title,
        RowName = rowName,
        FeatureTitle = "Baskets",
        ScenarioTags = [],
        CombinedTags = [],
        ExecutionStatus = failed ? ScenarioExecutionStatus.TestError : ScenarioExecutionStatus.OK,
        TestError = failed ? new InvalidOperationException("Expected: 301\nActual:   300") : null,
        OutlineId = Title,
        ExampleValues = values,
        ExamplesBlockIndex = 0,
        Duration = TimeSpan.FromMilliseconds(16),
    };

    /// <summary>
    /// The issue's outline, its third row failing, as the Reqnroll converter builds it; with <paramref name="filled"/>
    /// false, as 5.4.2 built it, every row named by the title.
    /// </summary>
    internal static Feature[] Baskets(bool filled = true) => new[]
        {
            Row("s0", filled ? "A basket is weighed on the UK scale (empty basket)" : null, new() { ["case"] = "empty basket", ["count"] = "0", ["grams"] = "0" }, false),
            Row("s1", filled ? "A basket is weighed on the UK scale (one apple)" : null, new() { ["case"] = "one apple", ["count"] = "1", ["grams"] = "150" }, false),
            Row("s2", filled ? Filled : null, TwoApples, true),
        }
        .ToFeatures();

    /// <summary>Verdicts read from a ledger 5.4.2 wrote: its roster holds the ids the rows' titles gave them.</summary>
    private HistoryVerdicts TitleVerdicts()
    {
        var ledgerPath = Path.Combine(_dir, Guid.NewGuid().ToString("N"), "history.jsonl");
        var (roster, run) = HistoryRunBuilder.Build(Baskets(filled: false), [], Suite, null, At, new HistoryBuildOptions(), "test:99:1");
        for (var i = 0; i < 3; i++)
        {
            var prior = run with
            {
                Id = $"test:{i + 1}:1", At = At.AddHours(i - 3), Commit = $"c{i + 1:D6}", Results = "PPP", Attempts = "---",
                Durations = [100, 100, 100], Errors = [null, null, null], ErrorText = new Dictionary<string, string>()
            };
            Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(ledgerPath, roster, prior, "5.4.2").Outcome);
        }
        return HistoryAnalyzer.Analyse(HistoryLedgerReader.Read(ledgerPath, 50).Ledger!, roster, run, new HistoryAnalysisOptions { MinRuns = 2 });
    }

    private string Html(Feature[] features, HistoryVerdicts? history = null)
    {
        var diagrams = features.SelectMany(f => f.Scenarios).Select(s => new DiagramAsCode(s.Id, "", "@startuml\nA->B\n@enduml")).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(diagrams, features, At.UtcDateTime.AddMinutes(-1), At.UtcDateTime, null,
            Path.Combine(_dir, $"Baskets_{Guid.NewGuid():N}.html"), "Baskets", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            suite: Suite, history: history, showHistorySection: history is not null);
        return File.ReadAllText(path);
    }

    private static void HasTheTitleIdOnly(string text, string? suite)
    {
        Assert.Contains(TitleId(suite), text);
        Assert.DoesNotContain(FilledId(suite), text);
    }

    // ─── the history roster (HistoryRunBuilder) ───

    [Fact]
    public void The_history_roster_keys_the_row_on_the_id_its_title_gave_it()
    {
        var (roster, _) = HistoryRunBuilder.Build(Baskets(), [], Suite, null, At, new HistoryBuildOptions());

        Assert.Contains(TitleId(Suite), roster.Ids);
        Assert.DoesNotContain(FilledId(Suite), roster.Ids);
        Assert.Equal(HistoryRunBuilder.Build(Baskets(filled: false), [], Suite, null, At, new HistoryBuildOptions()).Roster.Ids, roster.Ids);
    }

    // ─── CTRF (CtrfReportGenerator) ───

    [Fact]
    public void The_CTRF_report_names_the_row_and_writes_the_id_its_title_gave_it()
    {
        using var ctrf = JsonDocument.Parse(CtrfReportGenerator.Generate(Baskets(), At.UtcDateTime, At.UtcDateTime, null, "5.4.2", Suite));

        var test = ctrf.RootElement.GetProperty("results").GetProperty("tests").EnumerateArray().Single(t => t.GetProperty("status").GetString() == "failed");
        Assert.Equal(Filled, test.GetProperty("name").GetString());
        Assert.Equal(TitleId(Suite), test.GetProperty("extra").GetProperty("stableId").GetString());
    }

    // ─── Failures.md and Failures.jsonl (FailuresDigestGenerator) ───

    [Fact]
    public void Failures_md_heads_the_row_with_its_name_and_keeps_its_example_row_and_its_id()
    {
        var digest = FailuresDigestGenerator.Generate(Baskets(), null, "TestRunReport", "5.4.2", suite: Suite);

        Assert.Contains($"### 1. Baskets › {Filled}\n", digest.Markdown);
        Assert.DoesNotContain("(<case>)", digest.Markdown);
        Assert.Contains("Example row: case=two apples, count=2, grams=301", digest.Markdown);
        Assert.Contains($"stableId `{TitleId(Suite)}`", digest.Markdown);
        Assert.Contains($"TestRunReport.html#sid-{TitleId(Suite)}", digest.Markdown);
    }

    [Fact]
    public void Failures_jsonl_names_the_row_and_writes_the_id_its_title_gave_it()
    {
        var digest = FailuresDigestGenerator.Generate(Baskets(), null, "TestRunReport", "5.4.2", suite: Suite);

        var failure = FailuresJsonl.Failures(digest.Jsonl).Single();
        Assert.Equal(Filled, failure.GetProperty("scenario").GetString());
        Assert.Equal(TitleId(Suite), failure.GetProperty("stableId").GetString());
    }

    // ─── the labs page (LabsReportGenerator) ───

    [Fact]
    public void The_labs_page_links_the_row_by_the_id_its_title_gave_it()
    {
        var page = LabsReportGenerator.Build(new LabsPage("Baskets", Baskets(), Suite, TitleVerdicts(), [], "TestRunReport.html", RunId: "test:99:1", EndedAt: At));

        var row = Regex.Matches(page, "<tr class=\"labs-row\">.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).Single(r => r.Contains("two apples"));
        Assert.Contains($"TestRunReport.html#sid-{TitleId(Suite)}", row);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(Filled), row);
    }

    // ─── the console summary (RunSummaryConsoleWriter) ───

    [Fact]
    public void The_console_summary_names_the_row_and_writes_the_id_its_title_gave_it()
    {
        var summary = RunSummaryConsoleWriter.Summarise(Baskets(), _dir, [], agentInstructionsWritten: true, suite: Suite);

        Assert.Equal(TitleId(Suite), summary.Failures.Single().StableId);
        var text = RunSummaryConsoleWriter.Build(summary);
        Assert.Contains($"{TitleId(Suite)}  Baskets › {Filled}", text);
    }

    // ─── the CI summary (CiSummaryGenerator) ───

    [Fact]
    public void The_CI_summary_names_the_row()
    {
        var markdown = CiSummaryGenerator.GenerateMarkdown(Baskets(), [], [], At.UtcDateTime.AddMinutes(-1), At.UtcDateTime);

        Assert.Contains(Filled, markdown);
        Assert.DoesNotContain("(<case>)", markdown);
    }

    // ─── the page (ReportGenerator) ───

    [Fact]
    public void The_timeline_names_the_row_and_joins_it_by_the_id_its_title_gave_it()
    {
        var html = Html(Baskets());

        var timelineRow = Regex.Match(html, $"<div class=\"timeline-row[^\"]*\" data-sid=\"{TitleId(Suite)}\"[^>]*>.*?</div>", RegexOptions.Singleline);
        Assert.True(timelineRow.Success, "no timeline row joined by the title's id");
        Assert.Contains($"<div class=\"timeline-label\" title=\"{Filled}\">{Filled}</div>", html);
        Assert.DoesNotContain($"data-sid=\"{FilledId(Suite)}\"", html);
    }

    [Fact]
    public void The_outline_s_row_carries_the_id_its_title_gave_it()
    {
        var html = Html(Baskets());

        Assert.Matches(new Regex($"<tr [^>]*data-stable-id=\"{TitleId(Suite)}\" id=\""), html);
        Assert.DoesNotContain(FilledId(Suite), html);
    }

    [Fact]
    public void The_outline_s_flat_row_carries_the_id_its_title_gave_it()
    {
        var features = Baskets();
        foreach (var scenario in features[0].Scenarios)
            scenario.ExampleFlatValues = new Dictionary<string, string>(scenario.ExampleValues!);

        var html = Html(features);

        // the grouped table's row and the flat table's
        Assert.Equal(2, Regex.Matches(html, $"<tr [^>]*data-stable-id=\"{TitleId(Suite)}\"").Count);
        Assert.DoesNotContain(FilledId(Suite), html);
    }

    [Fact]
    public void The_outline_s_row_reads_its_history_by_the_id_its_title_gave_it()
    {
        var html = Html(Baskets(), TitleVerdicts());

        var row = Regex.Match(html, $"<tr [^>]*data-stable-id=\"{TitleId(Suite)}\"[^>]*>");
        Assert.True(row.Success, "no row of the title's id");
        Assert.Contains("data-history-verdicts=\"broke\"", row.Value);
    }

    [Fact]
    public void A_marked_scenario_shown_on_its_own_carries_the_id_its_title_gave_it()
    {
        // A marked row is always in its outline's group, but the scenario element writes an id too: a single marked
        // scenario with no values is shown on its own.
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Baskets",
                Scenarios = [new Scenario { Id = "s9", DisplayName = "Weigh a basket for breakfast", OutlineId = "Weigh a basket for <occasion>", KeyedByOutline = true, Result = ExecutionResult.Passed }]
            }
        ];

        var html = Html(features);

        var expected = ScenarioStableId.Compute(Suite, "Baskets", "Weigh a basket for <occasion>", "Weigh a basket for <occasion>");
        Assert.Matches(new Regex($"<details class=\"scenario[^\"]*\"[^>]* data-stable-id=\"{expected}\""), html);
    }

    [Fact]
    public void The_component_diagram_s_model_lists_the_row_by_the_id_its_title_gave_it()
    {
        RequestResponseLog Call(string testId, RequestResponseType type) =>
            new(testId, testId, HttpMethod.Post, null, new Uri("http://scale/"), [], "Scale", "Shop", type, Guid.NewGuid(), Guid.NewGuid(), false);
        var calls = new[] { Call("s2", RequestResponseType.Request) };
        var written = MergeableReportRenderer.Render(new MergeableReport
        {
            Suite = Suite,
            Features = Baskets(),
            ComponentRelationships = ComponentDiagramGenerator.ExtractRelationships(calls),
            Interactions = calls
        }, Path.Combine(_dir, $"Merged_{Guid.NewGuid():N}.html"));

        var model = Regex.Match(File.ReadAllText(written), "<script id=\"component-diagram-model\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(model.Success, "no component diagram model");
        using var json = JsonDocument.Parse(model.Groups[1].Value);
        Assert.Contains(TitleId(Suite), json.RootElement.GetProperty("scenarios").EnumerateArray().Select(e => e.GetString()));
    }

    // ─── the data files (ReportGenerator.GenerateTestRunReportData) ───

    private string DataFile(DataFormat format, string extension)
    {
        using var _ = ReportGenerator.ScopeReportsDirectory(_dir);
        return File.ReadAllText(ReportGenerator.GenerateTestRunReportData(Baskets(), At.UtcDateTime, At.UtcDateTime,
            $"TestRunData_{Guid.NewGuid():N}.{extension}", format, suite: Suite));
    }

    [Fact]
    public void The_JSON_data_file_names_the_row_and_writes_the_id_its_title_gave_it()
    {
        using var json = JsonDocument.Parse(DataFile(DataFormat.Json, "json"));

        var row = json.RootElement.GetProperty("features")[0].GetProperty("scenarios").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "s2");
        Assert.Equal(Filled, row.GetProperty("name").GetString());
        Assert.Equal(Title, row.GetProperty("outlineId").GetString());
        Assert.True(row.GetProperty("keyedByOutline").GetBoolean());
        Assert.Equal(TitleId(Suite), row.GetProperty("stableId").GetString());
    }

    [Fact]
    public void The_XML_data_file_names_the_row_and_writes_the_id_its_title_gave_it()
    {
        var scenario = XDocument.Parse(DataFile(DataFormat.Xml, "xml")).Descendants("Scenario").Single(s => s.Element("Name")?.Value == Filled);

        Assert.Equal(TitleId(Suite), scenario.Element("StableId")?.Value);
    }

    [Fact]
    public void The_YAML_data_file_names_the_row_and_writes_the_id_its_title_gave_it()
    {
        var yaml = DataFile(DataFormat.Yaml, "yml");

        Assert.Contains(Filled, yaml);
        HasTheTitleIdOnly(yaml, Suite);
    }
}
