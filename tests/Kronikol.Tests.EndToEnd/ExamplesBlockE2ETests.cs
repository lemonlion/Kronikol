using System.Text.RegularExpressions;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Named <c>Examples:</c> block separator bands in the parameterized table: rendering,
/// inertness to row selection, flatten-toggle consistency and search highlighting.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ExamplesBlockE2ETests : PlaywrightTestBase
{
    public ExamplesBlockE2ETests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateBlockReport(string fileName)
    {
        Scenario Row(string id, string period, string change, ExecutionResult result,
            string? blockName, string? blockDescription, int blockIndex) => new()
        {
            Id = id,
            DisplayName = $"Movement ({period})",
            Result = result,
            ErrorMessage = result == ExecutionResult.Failed ? "wrong direction" : null,
            Duration = TimeSpan.FromMilliseconds(200),
            OutlineId = "Market share movement is reported",
            ExampleValues = new Dictionary<string, string> { ["Period"] = period, ["Change"] = change },
            ExampleFlatValues = new Dictionary<string, string> { ["Period"] = period, ["Change"] = change },
            ExamplesBlockName = blockName,
            ExamplesBlockDescription = blockDescription,
            ExamplesBlockIndex = blockIndex,
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = $"the market data for {period}", Status = ExecutionResult.Passed },
                new ScenarioStep { Keyword = "Then", Text = $"the change is {change}", Status = result }
            ]
        };

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Market Share",
                Scenarios =
                [
                    Row("mv1", "OneWeek", "2.50", ExecutionResult.Passed, "the merchant gained share", null, 0),
                    Row("mv2", "OneYear", "5.00", ExecutionResult.Passed, "the merchant gained share", null, 0),
                    Row("mv3", "FourWeeks", "-2.50", ExecutionResult.Failed, "the merchant lost share", "movement is negative", 1),
                    Row("mv4", "OneMonth", "-3.50", ExecutionResult.Passed, "the merchant lost share", "movement is negative", 1),
                    Row("mv5", "RollingYear", "0.00", ExecutionResult.Passed, null, null, 2)
                ]
            }
        };

        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Examples Blocks Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenGroup()
    {
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        var group = Page.Locator("details.scenario-parameterized").First;
        if (await group.GetAttributeAsync("open") == null)
            await group.Locator("summary").First.ClickAsync();
    }

    private ILocator Group => Page.Locator("details.scenario-parameterized").First;
    private ILocator FlatBands => Group.Locator("table.param-table-flat tr.examples-block-row");
    private ILocator GroupedBands => Group.Locator("table.param-table-grouped tr.examples-block-row");
    private ILocator FlatMemberRows => Group.Locator("table.param-table-flat tbody tr[data-row-idx]");

    // ── Band rendering ──

    [Fact]
    public async Task Bands_render_with_names_counts_and_description()
    {
        await Page.GotoAsync(GenerateBlockReport("BlockBands.html"));
        await OpenGroup();

        Assert.Equal(3, await FlatBands.CountAsync());

        var firstBand = FlatBands.Nth(0);
        await Expect(firstBand).ToBeVisibleAsync();
        var firstText = await firstBand.InnerTextAsync();
        Assert.Contains("Examples: the merchant gained share", firstText);
        Assert.Contains("2/2 passed", firstText);

        var secondText = await FlatBands.Nth(1).InnerTextAsync();
        Assert.Contains("Examples: the merchant lost share", secondText);
        Assert.Contains("1 failed, 1/2 passed", secondText);
        Assert.Contains("movement is negative", secondText);

        // The unnamed block renders the bare keyword.
        var thirdText = await FlatBands.Nth(2).InnerTextAsync();
        Assert.Contains("Examples", thirdText);
        Assert.Contains("1/1 passed", thirdText);
    }

    [Fact]
    public async Task Bands_sit_before_their_rows_with_continuous_numbering()
    {
        await Page.GotoAsync(GenerateBlockReport("BlockOrder.html"));
        await OpenGroup();

        // All rows of the flat table body: band, r1, r2, band, r3, r4, band, r5
        var allRows = Group.Locator("table.param-table-flat tbody tr");
        Assert.Equal(8, await allRows.CountAsync());
        Assert.Contains("examples-block-row", await allRows.Nth(0).GetAttributeAsync("class") ?? "");
        Assert.Contains("examples-block-row", await allRows.Nth(3).GetAttributeAsync("class") ?? "");
        Assert.Contains("examples-block-row", await allRows.Nth(6).GetAttributeAsync("class") ?? "");

        // Member numbering keeps counting across blocks.
        var texts = new List<string>();
        for (var i = 0; i < 5; i++)
            texts.Add(await FlatMemberRows.Nth(i).Locator("td").First.InnerTextAsync());
        Assert.Equal(["1", "2", "3", "4", "5"], texts);
    }

    // ── Row selection ──

    [Fact]
    public async Task Member_row_below_a_band_still_switches_the_detail_panel()
    {
        await Page.GotoAsync(GenerateBlockReport("BlockRowSelect.html"));
        await OpenGroup();

        var panels = Group.Locator(".param-detail-panel");
        await Expect(panels.Nth(0)).ToBeVisibleAsync();

        // Click the third member row (first row of the second block, below a band).
        await FlatMemberRows.Nth(2).ClickAsync();

        await Expect(FlatMemberRows.Nth(2)).ToHaveClassAsync(new Regex("row-active"));
        await Expect(panels.Nth(0)).ToBeHiddenAsync();
        await Expect(panels.Nth(2)).ToBeVisibleAsync();
        Assert.Contains("FourWeeks", await panels.Nth(2).InnerTextAsync());
    }

    [Fact]
    public async Task Band_row_click_is_inert()
    {
        await Page.GotoAsync(GenerateBlockReport("BlockBandInert.html"));
        await OpenGroup();

        // Select row 2, then click a band: selection and panel must not change.
        await FlatMemberRows.Nth(1).ClickAsync();
        await Expect(FlatMemberRows.Nth(1)).ToHaveClassAsync(new Regex("row-active"));

        await FlatBands.Nth(1).ClickAsync();

        await Expect(FlatMemberRows.Nth(1)).ToHaveClassAsync(new Regex("row-active"));
        await Expect(FlatBands.Nth(1)).Not.ToHaveClassAsync(new Regex("row-active"));
        var panels = Group.Locator(".param-detail-panel");
        await Expect(panels.Nth(1)).ToBeVisibleAsync();
    }

    // ── Flatten toggle ──

    [Fact]
    public async Task Bands_present_in_both_table_variants_and_active_row_survives_toggle()
    {
        await Page.GotoAsync(GenerateBlockReport("BlockToggle.html"));
        await OpenGroup();

        Assert.Equal(3, await FlatBands.CountAsync());
        Assert.Equal(3, await GroupedBands.CountAsync());

        // Activate the fourth member row, then toggle to the grouped view.
        await FlatMemberRows.Nth(3).ClickAsync();
        await Group.Locator("button.flatten-toggle").First.ClickAsync();

        var groupedTable = Group.Locator("table.param-table-grouped").First;
        await Expect(groupedTable).ToBeVisibleAsync();
        await Expect(GroupedBands.Nth(0)).ToBeVisibleAsync();

        var activeGrouped = groupedTable.Locator("tbody tr[data-row-idx='3']");
        await Expect(activeGrouped).ToHaveClassAsync(new Regex("row-active"));
    }

    // ── Search ──

    [Fact]
    public async Task Searching_a_block_name_keeps_the_section_and_highlights_its_rows()
    {
        await Page.GotoAsync(GenerateBlockReport("BlockSearch.html"));
        await OpenGroup();

        await FillSearchBar("lost share");

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('tr.row-search-match').length > 0",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        await Expect(Group).ToBeVisibleAsync();

        // Only the two member rows of "the merchant lost share" match in the visible table.
        var matches = Group.Locator("table.param-table-flat tr.row-search-match");
        Assert.Equal(2, await matches.CountAsync());
        var matchText = await matches.Nth(0).InnerTextAsync() + await matches.Nth(1).InnerTextAsync();
        Assert.Contains("FourWeeks", matchText);
        Assert.Contains("OneMonth", matchText);

        // The first matching row is auto-selected.
        await Expect(FlatMemberRows.Nth(2)).ToHaveClassAsync(new Regex("row-active"));
    }
}
