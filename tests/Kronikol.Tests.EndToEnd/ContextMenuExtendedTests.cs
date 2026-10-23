using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Diagrams)]
public class ContextMenuExtendedTests : PlaywrightTestBase
{
    public ContextMenuExtendedTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task<string> GetComputedStyle(ILocator el, string prop) =>
        await el.EvaluateAsync<string>($"(e, p) => window.getComputedStyle(e).getPropertyValue(p)", prop);

    // ── Context menu positioning ──

    [Fact]
    public async Task Context_menu_z_index_is_high_enough()
    {
        await Page.GotoReportAsync(GenerateReport("CtxZIndex.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });

        var zIndex = await GetComputedStyle(menu, "z-index");
        Assert.True(int.Parse(zIndex) >= 10000, $"Context menu z-index should be >= 10000 but was {zIndex}");
    }

    [Fact]
    public async Task Context_menu_has_box_shadow()
    {
        await Page.GotoReportAsync(GenerateReport("CtxShadow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });

        var boxShadow = await GetComputedStyle(menu, "box-shadow");
        Assert.NotEqual("none", boxShadow);
    }

    // ── Submenu structure ──

    [Fact]
    public async Task Copy_image_submenu_has_submenu_children()
    {
        await Page.GotoReportAsync(GenerateReport("CtxSubmenuChildren.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });

        var submenuParents = Page.Locator(".diagram-ctx-menu .submenu-parent");
        var count = await submenuParents.CountAsync();
        Assert.True(count >= 3, $"Should have at least 3 submenu parents but found {count}");

        for (var i = 0; i < count; i++)
        {
            var submenu = submenuParents.Nth(i).Locator(".submenu");
            Assert.Equal(1, await submenu.CountAsync());
        }
    }

    [Fact]
    public async Task Submenu_is_hidden_by_default()
    {
        await Page.GotoReportAsync(GenerateReport("CtxSubmenuHidden.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });

        var submenu = Page.Locator(".diagram-ctx-menu .submenu").First;
        var display = await GetComputedStyle(submenu, "display");
        Assert.Equal("none", display);
    }

    [Fact]
    public async Task Submenu_shows_on_parent_hover()
    {
        await Page.GotoReportAsync(GenerateReport("CtxSubmenuHover.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });

        var parent = Page.Locator(".diagram-ctx-menu .submenu-parent").First;
        await parent.HoverAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var p = document.querySelector('.diagram-ctx-menu .submenu-parent');
                var s = p && p.querySelector('.submenu');
                return s && window.getComputedStyle(s).display !== 'none';
            }
        """, null, new() { Timeout = 3000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Submenu_parent_has_arrow_indicator()
    {
        await Page.GotoReportAsync(GenerateReport("CtxSubmenuArrow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });

        var content = await Page.EvaluateAsync<string>("""
            () => {
                var p = document.querySelector('.diagram-ctx-menu .submenu-parent');
                return window.getComputedStyle(p, '::after').content;
            }
        """);
        Assert.False(string.IsNullOrEmpty(content) || content == "none",
            "Submenu parent should have ::after arrow indicator");
    }

    // ── Show Browser Menu item ──

    [Fact]
    public async Task Context_menu_has_show_browser_menu_item()
    {
        await Page.GotoReportAsync(GenerateReport("CtxBrowserMenu.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });

        var items = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.diagram-ctx-menu > div'))
                .map(i => i.textContent.split('\n')[0].trim())
        """);
        Assert.Contains("Show default browser menu", items);
    }

    // ── Copy PlantUML source item ──

    [Fact]
    public async Task Context_menu_has_copy_source_item()
    {
        await Page.GotoReportAsync(GenerateReport("CtxCopySource.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });

        var items = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.diagram-ctx-menu > div'))
                .map(i => i.textContent.split('\n')[0].trim())
        """);
        Assert.True(items.Any(t => t.Contains("Copy") && t.Contains("source")),
            $"Expected a 'Copy ... source' item. Items: {string.Join(", ", items)}");
    }

    // ── Context menu replaced on new right-click ──

    [Fact]
    public async Task New_right_click_replaces_existing_context_menu()
    {
        await Page.GotoReportAsync(GenerateReport("CtxReplace.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });

        // Right-click again
        await svg.ClickAsync(new() { Button = MouseButton.Right, Position = new() { X = 10, Y = 10 } });

        var count = await Page.Locator(".diagram-ctx-menu").CountAsync();
        Assert.Equal(1, count);
    }

    // ── Open in new tab items ──

    [Fact]
    public async Task Context_menu_has_open_in_new_tab_submenu()
    {
        await Page.GotoReportAsync(GenerateReport("CtxOpenTab.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });

        var items = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.diagram-ctx-menu > div'))
                .map(i => {
                    // Get only direct text content, excluding child element text
                    var text = '';
                    for (var n = i.firstChild; n; n = n.nextSibling) {
                        if (n.nodeType === 3) text += n.textContent;
                    }
                    return text.trim() || i.textContent.split('\n')[0].trim();
                })
                .filter(t => t.length > 0)
        """);
        Assert.Contains("Open image in new tab", items);
    }

    // ── Separator exists in menu ──

    [Fact]
    public async Task Context_menu_has_separator()
    {
        await Page.GotoReportAsync(GenerateReport("CtxSeparator.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });

        var separators = Page.Locator(".diagram-ctx-menu > hr");
        Assert.True(await separators.CountAsync() >= 1, "Context menu should have at least one separator");
    }

    [Fact]
    public async Task Context_menu_show_browser_menu_shows_toast()
    {
        await Page.GotoReportAsync(GenerateReport("CtxBrowserMenuToast.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        await DispatchContextMenu(svg);
        var menu = Page.Locator(".diagram-ctx-menu");
        await Expect(menu).ToBeVisibleAsync(new() { Timeout = 5000 });

        var showBrowserItem = Page.Locator(".diagram-ctx-menu > div", new() { HasTextRegex = new System.Text.RegularExpressions.Regex("browser menu", System.Text.RegularExpressions.RegexOptions.IgnoreCase) });
        await showBrowserItem.ClickAsync();

        await menu.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 });

        var toast = Page.Locator(".diagram-ctx-toast");
        await Expect(toast).ToBeVisibleAsync(new() { Timeout = 5000 });
        var toastText = await toast.TextContentAsync();
        Assert.Contains("right-click", toastText!, StringComparison.OrdinalIgnoreCase);
    }
}
