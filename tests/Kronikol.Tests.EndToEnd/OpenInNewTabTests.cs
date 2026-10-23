using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The diagram menu's "in new tab" items (5.0.0, plans/OPTION_C_PLAN.md S12, round 17 item 21): the tab is opened
/// while the click is handled, which a popup blocker allows, and the picture or the text is written into it. A PNG item
/// used to open its tab only once the picture was drawn, after the click, which Firefox refused, and every item opened
/// a blob: URL, which a file:// page makes with an opaque origin. Playwright's browsers block no popup, so what is
/// checked is the order (the tab opens before the picture is drawn) and what the tab holds.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class OpenInNewTabTests : PlaywrightTestBase
{
    public OpenInNewTabTests(PlaywrightFixture fixture) : base(fixture) { }

    // window.open and the drawing of the picture (onto its canvas, then out of it), recorded in the order they happen:
    // a tab opened only once the picture is drawn comes after the drawImage
    private const string Recorder = """
        (() => {
            window.__order = [];
            const open = window.open;
            window.open = function () { window.__order.push('open'); return open.apply(window, arguments); };
            const drawImage = CanvasRenderingContext2D.prototype.drawImage;
            CanvasRenderingContext2D.prototype.drawImage = function () { window.__order.push('drawn'); return drawImage.apply(this, arguments); };
            const toDataURL = HTMLCanvasElement.prototype.toDataURL;
            HTMLCanvasElement.prototype.toDataURL = function () { window.__order.push('drawn'); return toDataURL.apply(this, arguments); };
            const toBlob = HTMLCanvasElement.prototype.toBlob;
            HTMLCanvasElement.prototype.toBlob = function () { window.__order.push('drawn'); return toBlob.apply(this, arguments); };
        })();
        """;

    // the source the menu was opened on (its diagram's data-plantuml)
    private string _source = "";

    // Opens the first scenario diagram's menu and clicks the item, under its submenu where it has one; returns the tab
    // the click opened.
    private async Task<IPage> OpenFromMenuAsync(string fileName, string submenu, string item, Func<string, string>? editSource = null)
    {
        await Context.AddInitScriptAsync(Recorder);
        await Page.GotoReportAsync(GenerateReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;
        _source = await svg.EvaluateAsync<string>("el => { const c = el.closest('[data-plantuml]'); return c ? c.getAttribute('data-plantuml') : ''; }");
        if (editSource is not null)
        {
            // the menu reads the source when it opens; nothing redraws a diagram whose data-plantuml changes
            _source = editSource(_source);
            await svg.EvaluateAsync("(el, s) => el.closest('[data-plantuml]').setAttribute('data-plantuml', s)", _source);
        }
        await DispatchContextMenu(svg);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });

        ILocator target;
        var parent = menu.Locator(".submenu-parent", new() { HasTextRegex = new Regex("^" + Regex.Escape(submenu)) });
        if (await parent.CountAsync() > 0)
        {
            await parent.First.HoverAsync();
            target = parent.First.Locator(".submenu").GetByText(item, new() { Exact = true }).First;
        }
        else
        {
            // the item stands alone where it has no choice to offer (a source no toggle changed)
            target = menu.GetByText(submenu, new() { Exact = true }).First;
        }

        var popupTask = Page.Context.WaitForPageAsync();
        await target.ClickAsync();
        return await popupTask;
    }

    private static Task WaitForTitleAsync(IPage tab, string title) =>
        tab.WaitForFunctionAsync("t => document.title === t", title, new() { Timeout = 15000, PollingInterval = 200 });

    private async Task AssertOpenedBeforeDrawnAsync()
    {
        var order = await Page.EvaluateAsync<string[]>("() => window.__order");
        Assert.True(order.Length >= 2, "recorded: " + string.Join(", ", order));
        Assert.Equal("open", order[0]);
        Assert.Contains("drawn", order);
    }

    private static async Task AssertPictureAsync(IPage tab, string dataPrefix)
    {
        await tab.WaitForFunctionAsync(
            "() => { const i = document.querySelector('img'); return !!i && i.complete && i.naturalWidth > 0; }",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        var src = await tab.Locator("img").First.GetAttributeAsync("src");
        Assert.StartsWith(dataPrefix, src);
        Assert.False(tab.Url.StartsWith("blob:", StringComparison.Ordinal), "the tab shows " + tab.Url);
    }

    [Theory]
    [InlineData("Open as PNG image in new tab")]
    [InlineData("Open as PNG image (no transparency) in new tab")]
    public async Task A_PNG_item_opens_its_tab_in_the_click_and_writes_the_picture_into_it(string item)
    {
        var tab = await OpenFromMenuAsync($"OpenTab_Png_{item.Length}.html", "Open image in new tab", item);

        await WaitForTitleAsync(tab, "Diagram (PNG)");
        await AssertPictureAsync(tab, "data:image/png;base64,");
        await AssertOpenedBeforeDrawnAsync();
    }

    [Fact]
    public async Task The_SVG_item_writes_the_picture_into_its_tab()
    {
        var tab = await OpenFromMenuAsync("OpenTab_Svg.html", "Open image in new tab", "Open as SVG image in new tab");

        await WaitForTitleAsync(tab, "Diagram (SVG)");
        await AssertPictureAsync(tab, "data:image/svg+xml;base64,");
    }

    [Fact]
    public async Task The_source_item_writes_the_source_into_its_tab()
    {
        // markup the tab must show as text: a step bar's <<stepDelimiter>>, a note's <color:...> and a bare & (the
        // fixture's own first diagram holds none)
        var tab = await OpenFromMenuAsync("OpenTab_Source.html", "Open PlantUML source in new tab", "Open current PlantUML in new tab",
            source => source.Replace("@enduml", "hnote across <<stepDelimiter>>: <color:#555>a & b</color>\n@enduml"));

        await WaitForTitleAsync(tab, "PlantUML source");
        var text = await tab.Locator("pre").First.TextContentAsync();
        Assert.Contains("<<stepDelimiter>>", _source);
        Assert.Equal(_source.Replace("\r\n", "\n"), text!.Replace("\r\n", "\n"));
        Assert.False(tab.Url.StartsWith("blob:", StringComparison.Ordinal), "the tab shows " + tab.Url);
    }
}
