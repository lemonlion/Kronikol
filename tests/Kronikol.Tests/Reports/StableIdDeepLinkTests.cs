using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The address an agent is handed has to be an address a human can open. <c>kronikol query</c> and
/// <c>Failures.md</c> both speak <c>stableId</c>, but until now the HTML had no element carrying one —
/// the only anchor was <c>#scenario-&lt;slug&gt;</c>, a slug of the display name, which changes the
/// moment anyone renames a test and collides whenever two features name a scenario the same thing.
/// These facts pin the other half of that contract: every scenario element carries its stable id, and
/// the ids in the markup are the ids in the data file — a link built from one resolves in the other.
/// </summary>
public class StableIdDeepLinkTests
{
    private const string DiagramSource = """
        @startuml
        actor "Caller" as caller
        participant "Svc" as svc
        caller -> svc : GET /health
        svc --> caller : 200 OK
        @enduml
        """;

    private static Feature[] TwoFeaturesSharingAScenarioName() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "a1", DisplayName = "Pay", Result = ExecutionResult.Failed,
                    ErrorMessage = "card declined",
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "it charges", Status = ExecutionResult.Failed }]
                }
            ]
        },
        new Feature
        {
            DisplayName = "Refunds",
            Scenarios =
            [
                // Same display name, different feature: one slug, two scenarios. The slug anchor cannot
                // tell them apart; the stable id can, because the feature name is in the hash.
                new Scenario
                {
                    Id = "b1", DisplayName = "Pay", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "it refunds", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static Feature[] AnOutlineWithTwoRows() =>
    [
        new Feature
        {
            DisplayName = "Pricing",
            Scenarios =
            [
                new Scenario
                {
                    Id = "p1", DisplayName = "Discount applies", Result = ExecutionResult.Passed,
                    OutlineId = "discount", ExampleValues = new Dictionary<string, string> { ["tier"] = "gold" },
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "10% off", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "p2", DisplayName = "Discount applies", Result = ExecutionResult.Passed,
                    OutlineId = "discount", ExampleValues = new Dictionary<string, string> { ["tier"] = "silver" },
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "5% off", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static Feature[] AnOutlineWithFlatValues()
    {
        var features = AnOutlineWithTwoRows();
        features[0].Scenarios[0].ExampleFlatValues = new Dictionary<string, string> { ["tier"] = "gold" };
        features[0].Scenarios[1].ExampleFlatValues = new Dictionary<string, string> { ["tier"] = "silver" };
        return features;
    }

    private static string GenerateHtml(Feature[] features, bool testRunReport = true)
    {
        var diagrams = features.SelectMany(f => f.Scenarios)
            .Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", DiagramSource)).ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features, DateTime.UtcNow, DateTime.UtcNow,
            null, $"StableIdLink_{Guid.NewGuid():N}.html", "Test", testRunReport,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    private static string[] StableIdsIn(string html) =>
        Regex.Matches(html, "data-stable-id=\"([0-9a-f]{16})\"").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void Every_scenario_element_carries_its_stable_id()
    {
        var html = GenerateHtml(TwoFeaturesSharingAScenarioName());

        // Anchored on the emitted <details> tag, not on a bare substring: a report that merely names
        // the attribute in a script selector would pass a naive Contains and fail every real link.
        foreach (var (feature, scenario) in new[] { ("Checkout", "Pay"), ("Refunds", "Pay") })
        {
            var expected = ScenarioStableId.Compute(RunSuite.Current, feature, scenario);
            Assert.Matches(new Regex($"<details class=\"scenario\"[^>]*data-stable-id=\"{expected}\""), html);
        }

        // Two scenarios sharing a display name share one slug and differ by stable id — which is the
        // whole reason the second anchor exists.
        Assert.Equal(2, StableIdsIn(html).Distinct().Count());
    }

    [Fact]
    public void Every_example_row_carries_its_own_stable_id()
    {
        var html = GenerateHtml(AnOutlineWithTwoRows());

        foreach (var tier in new[] { "gold", "silver" })
        {
            var expected = ScenarioStableId.Compute(RunSuite.Current, "Pricing", "Discount applies", "discount",
                new Dictionary<string, string> { ["tier"] = tier });
            Assert.Matches(new Regex($"<tr class=\"[^\"]*\"[^>]*data-stable-id=\"{expected}\""), html);
        }

        Assert.Equal(2, StableIdsIn(html).Distinct().Count());
    }

    [Fact]
    public void An_outline_with_a_flat_view_carries_the_id_on_both_copies_of_each_row()
    {
        // An outline whose members expose flattened values renders its rows twice — the flat table,
        // displayed by default, and the grouped one behind it. Only the grouped copy carries an `id`
        // (Flat_table_rows_have_no_id_attribute), so without the stable id on the flat copy a deep
        // link resolves into a table nobody is looking at.
        var html = GenerateHtml(AnOutlineWithFlatValues());

        var silver = ScenarioStableId.Compute(RunSuite.Current, "Pricing", "Discount applies", "discount",
            new Dictionary<string, string> { ["tier"] = "silver" });
        Assert.Equal(2, Regex.Matches(html, $"data-stable-id=\"{silver}\"").Count);

        // The literal also appears in the flatten-params script, so anchor on the emitted <table>.
        var flatStart = html.IndexOf("<table class=\"param-test-table param-table-flat\"", StringComparison.Ordinal);
        var flatTable = html[flatStart..(html.IndexOf("</table>", flatStart, StringComparison.Ordinal) + 8)];
        Assert.Contains($"data-stable-id=\"{silver}\"", flatTable);
        Assert.DoesNotContain("id=\"scenario-", flatTable);
    }

    [Fact]
    public void The_ids_in_the_markup_are_the_ids_in_the_data_file()
    {
        // The contract that makes the link work at all: `kronikol query` reads the JSON, prints a
        // stableId, and the browser has to find that exact string in the HTML.
        var features = TwoFeaturesSharingAScenarioName().Concat(AnOutlineWithTwoRows()).ToArray();
        var html = GenerateHtml(features);

        var written = ReportGenerator.GenerateTestRunReportData(
            features, DateTime.UtcNow, DateTime.UtcNow,
            $"StableIdLink_{Guid.NewGuid():N}.json", DataFormat.Json);
        using var document = JsonDocument.Parse(File.ReadAllText(written));
        var fromData = document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Select(s => s.GetProperty("stableId").GetString()!)
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();

        Assert.Equal(fromData, StableIdsIn(html).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_specifications_report_carries_them_too()
    {
        // Same renderer, same model — the spec document gets deep links for free rather than by a branch.
        var html = GenerateHtml(TwoFeaturesSharingAScenarioName(), testRunReport: false);

        Assert.Contains(ScenarioStableId.Compute(RunSuite.Current, "Checkout", "Pay"), StableIdsIn(html));
    }

    [Fact]
    public void The_report_script_knows_how_to_resolve_a_sid_anchor()
    {
        var html = GenerateHtml(TwoFeaturesSharingAScenarioName());

        Assert.Contains("function reveal_url_anchor(", html);
        Assert.Contains("[data-stable-id=\"", html);
    }
}
