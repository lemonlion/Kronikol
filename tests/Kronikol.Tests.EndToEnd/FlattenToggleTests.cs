using Microsoft.Playwright;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Reports)]
public class FlattenToggleTests : PlaywrightTestBase
{
    public FlattenToggleTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateFlatReport(string fileName)
    {
        var scenarios = new[]
        {
            new Scenario
            {
                Id = "flat1", DisplayName = "Bake(recipe)", IsHappyPath = true,
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                OutlineId = "Bake",
                ExampleValues = new Dictionary<string, string> { ["Recipe"] = "{ Flour = Plain, Eggs = 2 }" },
                ExampleRawValues = new Dictionary<string, object?> { ["Recipe"] = new Dictionary<string, object?> { ["Flour"] = "Plain", ["Eggs"] = 2 } },
                ExampleFlatValues = new Dictionary<string, string> { ["RecipeName"] = "Classic", ["Flour"] = "Plain", ["Eggs"] = "2" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a classic recipe", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "When", Text = "baking", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = "the result is bread", Status = ExecutionResult.Passed }
                ]
            },
            new Scenario
            {
                Id = "flat2", DisplayName = "Bake(recipe)", IsHappyPath = false,
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2),
                OutlineId = "Bake",
                ExampleValues = new Dictionary<string, string> { ["Recipe"] = "{ Flour = Whole, Eggs = 3 }" },
                ExampleRawValues = new Dictionary<string, object?> { ["Recipe"] = new Dictionary<string, object?> { ["Flour"] = "Whole", ["Eggs"] = 3 } },
                ExampleFlatValues = new Dictionary<string, string> { ["RecipeName"] = "Rustic", ["Flour"] = "Whole", ["Eggs"] = "3" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a rustic recipe", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "When", Text = "baking", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = "the result is bread", Status = ExecutionResult.Passed }
                ]
            },
            new Scenario
            {
                Id = "flat3", DisplayName = "Bake(recipe)", IsHappyPath = false,
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                OutlineId = "Bake",
                ExampleValues = new Dictionary<string, string> { ["Recipe"] = "{ Flour = Rye, Eggs = 1 }" },
                ExampleRawValues = new Dictionary<string, object?> { ["Recipe"] = new Dictionary<string, object?> { ["Flour"] = "Rye", ["Eggs"] = 1 } },
                ExampleFlatValues = new Dictionary<string, string> { ["RecipeName"] = "Nordic", ["Flour"] = "Rye", ["Eggs"] = "1" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a nordic recipe", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "When", Text = "baking", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = "the result is bread", Status = ExecutionResult.Passed }
                ]
            }
        };

        var features = new[]
        {
            new Feature { DisplayName = "Baking Recipes", Scenarios = scenarios }
        };

        var diagrams = scenarios.Select(s => new DiagramAsCode(s.Id, "",
            $"@startuml\nActor -> Oven : {s.ExampleFlatValues!["RecipeName"]}\n@enduml")).ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Flatten Toggle Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task ExpandFeatures()
    {
        await ExpandAllFeaturesAsync();
    }

    private async Task ExpandAll()
    {
        await ExpandFeatures();
        await ExpandAllScenariosAsync();
    }

    // ── Toggle button presence ──

    [Fact]
    public async Task Flatten_toggle_button_renders_with_minus_when_flat_is_default()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatToggleRender.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var toggleBtn = group.Locator("button.flatten-toggle").First;
        await Expect(toggleBtn).ToBeVisibleAsync();
        var text = await toggleBtn.InnerTextAsync();
        Assert.Equal("\u2212", text.Trim()); // Unicode minus — flat table visible by default
    }

    // ── Default visibility ──

    [Fact]
    public async Task Flat_table_visible_by_default_grouped_hidden()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatDefaultVisibility.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var grouped = group.Locator("table.param-table-grouped").First;
        var flat = group.Locator("table.param-table-flat").First;

        await Expect(flat).ToBeVisibleAsync();
        await Expect(grouped).ToBeHiddenAsync();
    }

    // ── Toggle click shows flat, hides grouped ──

    [Fact]
    public async Task Toggle_click_shows_grouped_table_hides_flat()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatToggleClick.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var grouped = group.Locator("table.param-table-grouped").First;
        var flat = group.Locator("table.param-table-flat").First;

        // Click − toggle (flat is default, so first visible button is −)
        var toggleBtn = group.Locator("button.flatten-toggle").First;
        await toggleBtn.ClickAsync();

        await Expect(flat).ToBeHiddenAsync();
        await Expect(grouped).ToBeVisibleAsync();

        // Grouped table toggle should show + (plus)
        var groupedToggle = group.Locator("table.param-table-grouped button.flatten-toggle").First;
        var plusText = await groupedToggle.InnerTextAsync();
        Assert.Equal("+", plusText.Trim());
    }

    // ── Toggle back restores grouped ──

    [Fact]
    public async Task Toggle_twice_restores_flat_view()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatToggleTwice.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var grouped = group.Locator("table.param-table-grouped").First;
        var flat = group.Locator("table.param-table-flat").First;

        // Click − to show grouped
        await group.Locator("button.flatten-toggle").First.ClickAsync();
        await Expect(grouped).ToBeVisibleAsync();

        // Click + on grouped to restore flat
        await group.Locator("table.param-table-grouped button.flatten-toggle").First.ClickAsync();
        await Expect(flat).ToBeVisibleAsync();
        await Expect(grouped).ToBeHiddenAsync();
    }

    // ── Flat table has original column headers ──

    [Fact]
    public async Task Flat_table_shows_original_column_headers()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatHeaders.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        // Flat table is visible by default — no toggle click needed

        var flatHeaders = group.Locator("table.param-table-flat th.sub-header");
        var headerCount = await flatHeaders.CountAsync();
        Assert.True(headerCount >= 3, $"Expected at least 3 flat sub-headers, got {headerCount}");

        var headerTexts = new List<string>();
        for (var i = 0; i < headerCount; i++)
            headerTexts.Add(await flatHeaders.Nth(i).TextContentAsync() ?? "");

        Assert.Contains("Recipe Name", headerTexts);
        Assert.Contains("Flour", headerTexts);
        Assert.Contains("Eggs", headerTexts);
    }

    // ── Flat table has scalar values ──

    [Fact]
    public async Task Flat_table_rows_contain_scalar_values()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatScalarValues.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        // Flat table is visible by default — no toggle click needed

        var flatRows = group.Locator("table.param-table-flat tbody tr");
        Assert.Equal(3, await flatRows.CountAsync());

        var firstRowHtml = await flatRows.Nth(0).InnerHTMLAsync();
        Assert.Contains("Classic", firstRowHtml);
        Assert.Contains("Plain", firstRowHtml);
        Assert.DoesNotContain("cell-subtable", firstRowHtml);
        Assert.DoesNotContain("param-expand", firstRowHtml);
    }

    // ── Active row syncs between tables on toggle ──

    [Fact]
    public async Task Active_row_syncs_when_toggling()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatRowSync.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        // Click the second row in the flat table (visible by default)
        var flatRows = group.Locator("table.param-table-flat tbody tr");
        await flatRows.Nth(1).ClickAsync();
        await Expect(flatRows.Nth(1)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("row-active"));

        // Toggle to grouped
        await group.Locator("button.flatten-toggle").First.ClickAsync();

        // Second row in grouped table should be active
        var groupedRows = group.Locator("table.param-table-grouped tbody tr");
        await Expect(groupedRows.Nth(1)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("row-active"));
        await Expect(groupedRows.Nth(0)).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("row-active"));
    }

    // ── Clicking flat row switches detail panel ──

    [Fact]
    public async Task Clicking_flat_row_switches_detail_panel()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatRowDetail.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        // Flat view is visible by default — no toggle click needed

        var panels = group.Locator(".param-detail-panel");
        await Expect(panels.Nth(0)).ToBeVisibleAsync();
        await Expect(panels.Nth(1)).ToBeHiddenAsync();

        // Click second flat row
        var flatRows = group.Locator("table.param-table-flat tbody tr");
        await flatRows.Nth(1).ClickAsync();

        await Expect(panels.Nth(0)).ToBeHiddenAsync();
        await Expect(panels.Nth(1)).ToBeVisibleAsync();
    }

    // ── Search skips hidden flat table rows ──

    [Fact]
    public async Task Search_does_not_match_hidden_grouped_table_rows()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatSearchSkip.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Search for a value while grouped table is hidden (flat is default)
        await FillSearchBar("Classic");

        // Wait a moment for search to process
        await Page.WaitForFunctionAsync(
            "() => typeof search_scenarios === 'function'",
            null, new() { Timeout = 3000, PollingInterval = 200 });
        // Re-trigger search to ensure it runs
        await Page.Locator("#searchbar").DispatchEventAsync("keyup");

        // The grouped table rows should NOT be matched (they're in a hidden table)
        var matchedCount = await Page.Locator("table.param-table-grouped tr.row-search-match").CountAsync();
        Assert.Equal(0, matchedCount);
    }

    // ── Wrapper has overflow-x for horizontal scroll ──

    [Fact]
    public async Task Wrapper_allows_horizontal_scrolling()
    {
        await Page.GotoReportAsync(GenerateFlatReport("FlatScroll.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var wrapper = group.Locator(".param-table-wrapper").First;
        await Expect(wrapper).ToBeAttachedAsync();

        var overflowX = await wrapper.EvaluateAsync<string>(
            "e => window.getComputedStyle(e).overflowX");
        Assert.Equal("auto", overflowX);
    }
}
