using System.Security.Cryptography;
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Where each part of a scenario's search text starts (plans/SEARCH_PLAN.md 4.6.1, facts W1 and W2). A scenario's and an
/// outline group's <c>data-search</c> is its parts joined by spaces in a fixed order; <c>data-search-kinds</c> beside it
/// says where each kind of part starts in it, as the page reads the attribute (CRLF and a lone CR made LF by the HTML
/// parser), so the Search tab knows for every result which of its parts matched. <c>data-search</c> itself does not move.
/// </summary>
public class SearchKindsTests
{
    private static readonly DateTime Start = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

    private static IDocument Parse(string html) =>
        BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();

    // W1: every data-search and data-row-search of the rich fixture, in page order, against 5.9.0's bytes: the values were
    // the same, line for line, on c10ed743 and after B11 (cb614e39), whose linked calls the fixture does not draw, when the
    // pin was taken (plans/SEARCH_PLAN.harness/proofs/red-base.md)
    [Fact]
    public void data_search_is_byte_for_byte_unchanged()
    {
        var (diagrams, specFeatures, testRunFeatures, segments, cleanup) = ToggleDefaultsBaselineTests.BuildRichFixture();
        using (cleanup)
        {
            var run = Parse(ReportHtml.Read(ToggleDefaultsBaselineTests.GenerateTestRunShape(diagrams, testRunFeatures, segments, "SearchKinds_W1_Run.html")));
            var spec = Parse(ReportHtml.Read(ToggleDefaultsBaselineTests.GenerateSpecShape(diagrams, specFeatures, segments, "SearchKinds_W1_Spec.html")));
            var values = new[] { run, spec }
                .SelectMany(page => page.QuerySelectorAll("[data-search], [data-row-search]"))
                .Select(e => (e.GetAttribute("data-search") ?? "") + "|" + (e.GetAttribute("data-row-search") ?? ""))
                .ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values))));

            Assert.Equal("69078e15760d713b3903df350bac3231d557e877d17502564249543b47f3c673", hash);
        }
    }

    private static Scenario Run(string id, string name, ExecutionResult result = ExecutionResult.Passed, string? error = null, string[]? tags = null,
        string? description = null, string? rule = null, string[]? steps = null) => new()
    {
        Id = id, DisplayName = name, Result = result, ErrorMessage = error, Tags = tags, Description = description, Rule = rule,
        Steps = steps?.Select(s => new ScenarioStep { Keyword = "Given", Text = s, Status = ExecutionResult.Passed }).ToArray(),
    };

    // the runs of data-search-kinds: each kind's letter and the text from its start to the next run's
    private static List<(char Kind, string Text)> Runs(IElement e)
    {
        var text = e.GetAttribute("data-search")!;
        var marks = e.GetAttribute("data-search-kinds")!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(m => (Kind: m[0], At: int.Parse(m[1..], System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var runs = new List<(char, string)>();
        for (var i = 0; i < marks.Length; i++)
        {
            var end = i + 1 < marks.Length ? marks[i + 1].At : text.Length;
            Assert.True(marks[i].At >= 0 && marks[i].At <= end, $"run {i} of '{e.GetAttribute("data-search-kinds")}' is out of order");
            runs.Add((marks[i].Kind, text[marks[i].At..end].Trim()));
        }
        return runs;
    }

    // W2
    [Fact]
    public void data_search_kinds_says_where_each_part_starts()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Pancakes Creation", Description = "Making pancakes", Tags = ["kitchen"],
                Scenarios =
                [
                    Run("p1", "A batch is made", description: "Three people", rule: "Batches are whole", tags: ["smoke"], steps: ["an order for 3"]),
                    // a Windows error message: the page reads its CRLF as LF, and the offsets after it hold
                    Run("p2", "Flour is missing", ExecutionResult.Failed, error: "Expected 400\r\nbut was 500\rand more", steps: ["an order without flour"]),
                ]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([], features, Start, Start.AddSeconds(5), null, "SearchKinds_W2.html", "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var page = Parse(ReportHtml.Read(path));

        var batch = Runs(page.QuerySelectorAll("details.scenario[data-search]").Single(e => e.GetAttribute("data-search")!.Contains("a batch is made")));
        Assert.Equal(
            [('f', "pancakes creation"), ('n', "a batch is made"), ('d', "making pancakes three people"), ('r', "batches are whole"), ('t', "kitchen smoke"), ('s', "an order for 3")],
            batch);

        var flour = Runs(page.QuerySelectorAll("details.scenario[data-search]").Single(e => e.GetAttribute("data-search")!.Contains("flour is missing")));
        Assert.Equal('e', flour[^2].Kind);
        Assert.Equal("expected 400\nbut was 500\nand more", flour[^2].Text);
        Assert.Equal(('s', "an order without flour"), flour[^1]);
    }

    // W2, for an outline: the group's text is its own name, then its feature, then each row's parts
    [Fact]
    public void An_outline_group_s_kinds_say_where_each_part_starts()
    {
        static Scenario Row(string id, string @case) => new()
        {
            Id = id, DisplayName = $"A basket is weighed ({@case})", KeyedByOutline = true, OutlineId = "A basket is weighed (<case>)",
            ExampleValues = new() { ["case"] = @case }, ExamplesBlockIndex = 0, Result = ExecutionResult.Passed,
        };
        Feature[] features = [new Feature { DisplayName = "Baskets", Scenarios = [Row("b1", "empty"), Row("b2", "full")] }];
        var path = ReportGenerator.GenerateHtmlReport([], features, Start, Start.AddSeconds(5), null, "SearchKinds_W2_Outline.html", "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var page = Parse(ReportHtml.Read(path));

        var runs = Runs(page.QuerySelector("details.scenario-parameterized[data-search]")!);

        Assert.Equal('n', runs[0].Kind);
        Assert.Equal(('f', "baskets"), runs[1]);
        Assert.Contains(runs, r => r.Kind == 'x' && r.Text.Contains("empty", StringComparison.Ordinal));
        Assert.Contains(runs, r => r.Kind == 'n' && r.Text == "a basket is weighed (full)");
    }
}
