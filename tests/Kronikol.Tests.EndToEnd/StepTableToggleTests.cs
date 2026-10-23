using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Scenarios)]
public class StepTableToggleTests : PlaywrightTestBase
{
    public StepTableToggleTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Toggle_button_renders_with_param_name()
    {
        await Page.GotoReportAsync(GenerateReportWithStepTableToggle("StepToggleRender.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Expand all to see step content
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        // The toggle button should exist with the param name
        var toggleBtn = Page.Locator("button.step-table-ref");
        await toggleBtn.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await toggleBtn.First.InnerTextAsync();
        Assert.Contains("recipe", text);
    }

    [Fact]
    public async Task Toggle_button_click_scrolls_and_highlights_table()
    {
        await Page.GotoReportAsync(GenerateReportWithStepTableToggle("StepToggleHighlight.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var toggleBtn = Page.Locator("button.step-table-ref").First;
        await toggleBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Table should be visible (always visible, no collapse)
        var table = Page.Locator(".step-param-table[data-param='recipe']").First;
        await Expect(table).ToBeVisibleAsync();

        // Click toggle — table should get highlight class
        await toggleBtn.ClickAsync();

        // The table itself gets the highlight class (per-step table, no data-param cells)
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('.step-param-table[data-param=\"recipe\"]')?.classList.contains('step-param-highlight')",
            null,
            new() { Timeout = 5000, PollingInterval = 200 });

        // Table should still be visible
        await Expect(table).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Toggle_button_highlight_fades_after_timeout()
    {
        await Page.GotoReportAsync(GenerateReportWithStepTableToggle("StepToggleFade.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var toggleBtn = Page.Locator("button.step-table-ref").First;
        await toggleBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Click to trigger highlight
        await toggleBtn.ClickAsync();

        // Wait for highlight to appear
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('.step-param-table[data-param=\"recipe\"]')?.classList.contains('step-param-highlight')",
            null,
            new() { Timeout = 5000, PollingInterval = 200 });

        // Wait for highlight to be removed after timeout (1.5s)
        await Page.WaitForFunctionAsync(
            "() => !document.querySelector('.step-param-table[data-param=\"recipe\"]')?.classList.contains('step-param-highlight')",
            null,
            new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Table_contains_expected_data()
    {
        await Page.GotoReportAsync(GenerateReportWithStepTableToggle("StepToggleData.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var table = Page.Locator(".step-param-table[data-param='recipe']").First;
        await Expect(table).ToBeVisibleAsync();

        // Verify the table has the expected column headers
        var headers = table.Locator("thead th");
        var headerTexts = await headers.AllInnerTextsAsync();
        Assert.Contains("Name", headerTexts);
        Assert.Contains("Flour", headerTexts);

        // Verify the table has data rows
        var cells = table.Locator("tbody td");
        var cellTexts = await cells.AllInnerTextsAsync();
        Assert.Contains("Classic", cellTexts);
        Assert.Contains("Plain Flour", cellTexts);
    }
}
