using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Scenarios)]
public class ComplexInlineParamTests : PlaywrightTestBase
{
    public ComplexInlineParamTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Small_complex_param_renders_inline_span()
    {
        await Page.GotoAsync(GenerateReportWithComplexInlineParams("SmallInline.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        // The small recipe param should render as an inline span, not a button
        var inlineSpan = Page.Locator("span.step-param-inline[title='recipe']");
        await inlineSpan.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var text = await inlineSpan.First.InnerTextAsync();
        Assert.Contains("Name: Classic", text);
        Assert.Contains("Flour: Plain Flour", text);

        // Should NOT have a toggle button for the small param
        var recipeButton = Page.Locator("button.step-table-ref[data-param='recipe']");
        await Expect(recipeButton).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Large_complex_param_renders_as_expandable_button()
    {
        await Page.GotoAsync(GenerateReportWithComplexInlineParams("LargeButton.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        // The large config param should render as a button with data-value
        var configButton = Page.Locator("button.step-table-ref[data-param='config']");
        await configButton.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var hasDataValue = await configButton.First.EvaluateAsync<bool>("el => el.hasAttribute('data-value')");
        Assert.True(hasDataValue, "Large complex param button should have data-value attribute");
    }

    [Fact]
    public async Task Large_complex_param_button_click_expands_pre_block()
    {
        await Page.GotoAsync(GenerateReportWithComplexInlineParams("LargeExpand.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var configButton = Page.Locator("button.step-table-ref[data-param='config']");
        await configButton.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // No pre block before click
        var preBefore = Page.Locator("pre.step-param-expand");
        await Expect(preBefore).ToHaveCountAsync(0);

        // Click the button
        await configButton.First.ClickAsync();

        // Pre block should appear with JSON content
        var preBlock = Page.Locator("pre.step-param-expand");
        await preBlock.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var preText = await preBlock.First.InnerTextAsync();
        Assert.Contains("\"Host\": \"localhost\"", preText);
        Assert.Contains("\"Port\": 8080", preText);

        // Button should have active class
        var isActive = await configButton.First.EvaluateAsync<bool>(
            "el => el.classList.contains('step-table-ref-active')");
        Assert.True(isActive, "Button should have active class when expanded");
    }

    [Fact]
    public async Task Large_complex_param_button_click_again_collapses()
    {
        await Page.GotoAsync(GenerateReportWithComplexInlineParams("LargeCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var configButton = Page.Locator("button.step-table-ref[data-param='config']");
        await configButton.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Click to expand
        await configButton.First.ClickAsync();
        await Page.Locator("pre.step-param-expand").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Click again to collapse
        await configButton.First.ClickAsync();

        // Pre block should be removed
        var preBlock = Page.Locator("pre.step-param-expand");
        await Expect(preBlock).ToHaveCountAsync(0);

        // Button should not have active class
        var isActive = await configButton.First.EvaluateAsync<bool>(
            "el => el.classList.contains('step-table-ref-active')");
        Assert.False(isActive, "Button should not have active class when collapsed");
    }
}
