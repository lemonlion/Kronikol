using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Diagrams)]
public class ComponentFlowPopupTests : PlaywrightTestBase
{
    public ComponentFlowPopupTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    [Fact]
    public async Task Relationship_list_is_rendered()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateComponentFlowPage()));

        var list = Page.Locator(".iflow-rel-list");
        await Expect(list).ToBeVisibleAsync();

        var items = list.Locator("li");
        Assert.Equal(1, await items.CountAsync());
        await Expect(items.First).ToContainTextAsync("API");
        await Expect(items.First).ToContainTextAsync("DB");
    }

    [Fact]
    public async Task Clicking_relationship_opens_popup_with_flow_diagram()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateComponentFlowPage()));
        await Page.Locator("#rel-api-db").ClickAsync();

        var popup = Page.Locator(".iflow-popup");
        await Expect(popup).ToBeVisibleAsync();
        await Expect(popup.Locator("h3")).ToContainTextAsync("API");
        await Expect(popup.Locator("h3")).ToContainTextAsync("DB");

        var svg = popup.Locator(".iflow-diagram svg");
        await svg.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
    }

    [Fact]
    public async Task Relationship_popup_contains_summary_table()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateComponentFlowPage()));
        await Page.Locator("#rel-api-db").ClickAsync();

        var table = Page.Locator(".iflow-popup .iflow-rel-summary-table");
        await Expect(table).ToBeVisibleAsync();

        var rows = table.Locator("tr");
        Assert.True(await rows.CountAsync() >= 3);

        var tableText = await table.TextContentAsync();
        Assert.Contains("Order Creation Test", tableText!);
        Assert.Contains("Payment Flow Test", tableText!);
    }

    [Fact]
    public async Task Relationship_popup_close_button_works()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateComponentFlowPage()));
        await Page.Locator("#rel-api-db").ClickAsync();
        await Page.Locator(".iflow-popup").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await Page.Locator(".iflow-popup-close").ClickAsync();

        await Page.Locator(".iflow-overlay").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    [Fact]
    public async Task Activity_diagram_popup_does_not_show_loading_text_after_render()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateComponentFlowPage()));
        await Page.Locator("#rel-api-db").ClickAsync();

        var popup = Page.Locator(".iflow-popup");
        await Expect(popup).ToBeVisibleAsync();
        await popup.Locator(".iflow-diagram svg").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = PopupFirstDrawTimeout });

        var attrs = await Page.EvaluateAsync<string>("""
            () => {
                var el = document.querySelector('.iflow-popup .plantuml-browser');
                if (!el) return 'element-not-found';
                return (el.dataset.queued || 'missing') + '|' + (el.dataset.rendered || 'missing');
            }
        """);
        Assert.Equal("1|1", attrs);
    }
}
