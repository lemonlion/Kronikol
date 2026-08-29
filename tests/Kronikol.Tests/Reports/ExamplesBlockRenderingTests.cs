using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ExamplesBlockRenderingTests
{
    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string GenerateReport(Feature[] features, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            FixedTime, FixedTime,
            null, fileName, "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    private static Scenario MakeScenario(
        string id,
        string displayName,
        ExecutionResult result = ExecutionResult.Passed,
        string? blockName = null,
        string? blockDescription = null,
        int? blockIndex = null,
        Dictionary<string, string>? exampleValues = null,
        Dictionary<string, string>? exampleFlatValues = null,
        ScenarioStep[]? steps = null,
        string? errorMessage = null)
    {
        return new Scenario
        {
            Id = id,
            DisplayName = displayName,
            Result = result,
            OutlineId = "Market share movement is reported",
            ExampleValues = exampleValues ?? new Dictionary<string, string> { ["Period"] = id },
            ExampleFlatValues = exampleFlatValues,
            ExamplesBlockName = blockName,
            ExamplesBlockDescription = blockDescription,
            ExamplesBlockIndex = blockIndex,
            Steps = steps,
            ErrorMessage = errorMessage
        };
    }

    private static Feature[] MultiBlockFeatures(bool withFlatValues = false)
    {
        Dictionary<string, string>? Flat(string id) =>
            withFlatValues ? new Dictionary<string, string> { ["Period"] = id } : null;

        return
        [
            new Feature
            {
                DisplayName = "Market Share",
                Scenarios =
                [
                    MakeScenario("OneWeek", "Row OneWeek", blockName: "the merchant gained share", blockIndex: 0, exampleFlatValues: Flat("OneWeek")),
                    MakeScenario("OneYear", "Row OneYear", blockName: "the merchant gained share", blockIndex: 0, exampleFlatValues: Flat("OneYear")),
                    MakeScenario("FourWeeks", "Row FourWeeks", result: ExecutionResult.Failed, errorMessage: "wrong direction",
                        blockName: "the merchant lost share", blockDescription: "movement is negative", blockIndex: 1, exampleFlatValues: Flat("FourWeeks")),
                    MakeScenario("OneMonth", "Row OneMonth", blockName: "the merchant lost share", blockDescription: "movement is negative", blockIndex: 1, exampleFlatValues: Flat("OneMonth")),
                    MakeScenario("RollingYear", "Row RollingYear", blockIndex: 2, exampleFlatValues: Flat("RollingYear"))
                ]
            }
        ];
    }

    [Fact]
    public void Multi_block_group_renders_one_band_row_per_block()
    {
        var content = GenerateReport(MultiBlockFeatures(), "ExamplesBlocks_bands.html");
        var bandCount = Regex.Matches(content, "<tr class=\"examples-block-row\"").Count;
        Assert.Equal(3, bandCount);
        Assert.Contains("Examples: the merchant gained share", content);
        Assert.Contains("Examples: the merchant lost share", content);
    }

    [Fact]
    public void Band_rows_render_in_both_flat_and_grouped_tables_when_flat_view_present()
    {
        var content = GenerateReport(MultiBlockFeatures(withFlatValues: true), "ExamplesBlocks_flat.html");
        Assert.Contains("param-table-flat", content);
        Assert.Contains("param-table-grouped", content);
        // 3 blocks x 2 tables
        var bandCount = Regex.Matches(content, "<tr class=\"examples-block-row\"").Count;
        Assert.Equal(6, bandCount);
    }

    [Fact]
    public void Unnamed_block_among_named_ones_falls_back_to_keyword_only()
    {
        var content = GenerateReport(MultiBlockFeatures(), "ExamplesBlocks_unnamed.html");
        // The third block has no name: the band renders the bare keyword.
        Assert.Contains("<span class=\"examples-block-name\">Examples</span>", content);
    }

    [Fact]
    public void Block_name_and_description_are_html_encoded()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    MakeScenario("a", "Row a", blockName: "gain <b>&</b> loss", blockDescription: "desc <i>&</i> more", blockIndex: 0),
                    MakeScenario("b", "Row b", blockName: "other", blockIndex: 1)
                ]
            }
        };
        var content = GenerateReport(features, "ExamplesBlocks_encoding.html");
        Assert.Contains("Examples: gain &lt;b&gt;&amp;&lt;/b&gt; loss", content);
        Assert.Contains("desc &lt;i&gt;&amp;&lt;/i&gt; more", content);
        Assert.DoesNotContain("gain <b>&</b> loss", content);
    }

    [Fact]
    public void Description_span_absent_when_description_null()
    {
        var content = GenerateReport(MultiBlockFeatures(), "ExamplesBlocks_desc.html");
        // Exactly one block has a description.
        var descCount = Regex.Matches(content, "<span class=\"examples-block-desc\">").Count;
        Assert.Equal(1, descCount);
        Assert.Contains("movement is negative", content);
    }

    [Fact]
    public void Per_block_counts_show_failures_and_all_pass_abbreviation()
    {
        var content = GenerateReport(MultiBlockFeatures(), "ExamplesBlocks_counts.html");
        // Block 0: both pass -> abbreviated form.
        Assert.Contains("<span class=\"examples-block-counts\">2/2 passed</span>", content);
        // Block 1: one failed, one passed.
        Assert.Contains("<span class=\"examples-block-counts\">1 failed, 1/2 passed</span>", content);
        // Block 2: single passing row.
        Assert.Contains("<span class=\"examples-block-counts\">1/1 passed</span>", content);
    }

    [Fact]
    public void Single_unnamed_block_is_byte_equal_to_nulled_baseline()
    {
        Feature[] Build(bool withBlock) =>
        [
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    MakeScenario("a", "Row a", blockIndex: withBlock ? 0 : null),
                    MakeScenario("b", "Row b", blockIndex: withBlock ? 0 : null)
                ]
            }
        ];

        var withBlockContent = GenerateReport(Build(true), "ExamplesBlocks_single.html");
        var baseline = GenerateReport(Build(false), "ExamplesBlocks_single.html");
        Assert.DoesNotContain("examples-block-row\"", withBlockContent);
        Assert.Equal(baseline, withBlockContent);
    }

    [Fact]
    public void All_null_block_fields_render_no_band()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    MakeScenario("a", "Row a"),
                    MakeScenario("b", "Row b")
                ]
            }
        };
        var content = GenerateReport(features, "ExamplesBlocks_nulls.html");
        Assert.DoesNotContain("<tr class=\"examples-block-row\"", content);
    }

    [Fact]
    public void Band_row_is_inert_carries_no_row_attributes()
    {
        var content = GenerateReport(MultiBlockFeatures(withFlatValues: true), "ExamplesBlocks_inert.html");
        foreach (Match m in Regex.Matches(content, "<tr class=\"examples-block-row\"[^>]*>"))
        {
            Assert.DoesNotContain("data-row-idx", m.Value);
            Assert.DoesNotContain("onclick", m.Value);
            Assert.DoesNotContain("data-row-search", m.Value);
            Assert.DoesNotContain(" id=", m.Value);
        }
    }

    [Fact]
    public void Member_row_search_and_section_search_contain_block_name_and_description()
    {
        var content = GenerateReport(MultiBlockFeatures(), "ExamplesBlocks_search.html");

        var sectionSearch = Regex.Match(content, "data-search=\"([^\"]*)\"").Groups[1].Value;
        Assert.Contains("the merchant gained share", sectionSearch);
        Assert.Contains("movement is negative", sectionSearch);

        // A member row of block 1 carries the block name + description in its own search text.
        var rowSearches = Regex.Matches(content, "data-row-search=\"([^\"]*)\"")
            .Select(m => m.Groups[1].Value).ToArray();
        Assert.Contains(rowSearches, s => s.Contains("row fourweeks") && s.Contains("the merchant lost share") && s.Contains("movement is negative"));
        Assert.Contains(rowSearches, s => s.Contains("row oneweek") && s.Contains("the merchant gained share"));
        // Block 1's name must not leak into block 0's rows.
        Assert.Contains(rowSearches, s => s.Contains("row oneweek") && !s.Contains("the merchant lost share"));
    }

    [Fact]
    public void Row_numbering_is_continuous_across_blocks_and_detail_ids_unchanged()
    {
        var features = MultiBlockFeatures();
        // Give every scenario steps so all five detail panels render.
        foreach (var s in features[0].Scenarios)
            s.Steps = [new ScenarioStep { Keyword = "Given", Text = $"step of {s.Id}", Status = ExecutionResult.Passed }];

        var content = GenerateReport(features, "ExamplesBlocks_numbering.html");

        for (var i = 0; i < 5; i++)
            Assert.Contains($"-detail-{i}\"", content);
        Assert.DoesNotContain("-detail-5\"", content);

        // Member rows are numbered 1..5 continuously (numbering does not restart per block).
        var grouped = Regex.Match(content, "<table class=\"param-test-table\"[^>]*>.*?</table>", RegexOptions.Singleline).Value;
        for (var i = 0; i < 5; i++)
            Assert.Contains($"data-row-idx=\"{i}\"", grouped);
    }
}
