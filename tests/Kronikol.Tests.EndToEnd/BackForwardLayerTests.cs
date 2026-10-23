using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The image lightbox and the internal-flow popup as layers of the session history (5.2.0,
/// plans/BACK_BUTTON_NAVIGATION_PLAN.md section 7.1, L1 to L5): opening one adds an entry, so Back closes it and leaves
/// the place as it was (on a phone, Back left the report with the layer open), Forward opens it again, and closing it any
/// other way takes its entry back out, so the next Back goes to the entry before it.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class BackForwardLayerTests : BackForwardTestBase
{
    public BackForwardLayerTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 700;

    private const string Lightbox = ".lightbox-overlay.active";

    private Task OpenWithImageAsync(string fileName) => OpenWithImageAsync(Page, fileName);

    private async Task OpenWithImageAsync(IPage page, string fileName)
    {
        await OpenAsync(page, GenerateReportWithAttachments(fileName));
        await FoldAllAsync(page, "features", expand: true);
        await FoldAllAsync(page, "scenarios", expand: true);
        await page.WaitForFunctionAsync(
            "() => { const img = document.querySelector('img.attachment-image'); return !!img && img.complete && img.naturalWidth > 0; }",
            null, new() { PollingInterval = 200, Timeout = 10_000 });
        await page.Locator("img.attachment-image").First.ScrollIntoViewIfNeededAsync();
        await WheelAsync(page, 40);
    }

    private Task OpenLightboxAsync() => OpenLightboxAsync(Page);

    // a layer is no arrival: the page writes its entry as the click runs
    private static async Task OpenLightboxAsync(IPage page)
    {
        await page.Locator("img.attachment-image").First.ClickAsync();
        await page.Locator(Lightbox).WaitForAsync();
    }

    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public Task L1_Back_closes_the_lightbox_and_leaves_the_place_as_it_was(string engine) => InEngineAsync(engine, async page =>
    {
        await OpenWithImageAsync(page, $"BackNav_L1_{engine}.html");
        var tab = await SelectedTabAsync(page);
        var y = await page.EvaluateAsync<double>("() => scrollY");
        var link = await page.Locator("a.attachment-image-link").First.GetAttributeAsync("href");
        await OpenLightboxAsync(page);

        await BackAsync(page);
        await StillInTheReportAsync(page, "Back with the lightbox open");
        await Expect(page.Locator(Lightbox)).ToHaveCountAsync(0);
        Assert.Equal(tab, await SelectedTabAsync(page));
        Assert.InRange(await page.EvaluateAsync<double>("() => scrollY") - y, -2, 2);
        Assert.Equal("href=" + link, await FocusedAsync(page));

        await page.GoBackReportAsync();
        // GoBack can return before the page before has loaded: its title shows once it has
        await Expect(page).ToHaveTitleAsync(BeforeTitle, new() { Timeout = 15000 });
    });

    [Theory]
    [InlineData("escape")]
    [InlineData("backdrop")]
    public async Task L2_A_lightbox_closed_by_Esc_or_its_backdrop_leaves_no_entry_behind(string how)
    {
        // a guard: before 5.2.0 the lightbox had no entry at all, so this held then too
        await OpenWithImageAsync($"BackNav_L2_{how}.html");
        await OpenLightboxAsync();

        if (how == "escape") await Page.Keyboard.PressAsync("Escape");
        else await Page.Mouse.ClickAsync(5, 5);
        await Expect(Page.Locator(Lightbox)).ToHaveCountAsync(0);
        await Page.WaitForFunctionAsync("() => !history.state || !history.state.kronikol || !history.state.kronikol.layer",
            null, new() { PollingInterval = 200, Timeout = 5000 });

        await Page.GoBackReportAsync();
        await Expect(Page).ToHaveTitleAsync(BeforeTitle, new() { Timeout = 15000 });
    }

    [Fact]
    public async Task L3_Forward_after_Back_opens_the_lightbox_again()
    {
        await OpenWithImageAsync("BackNav_L3.html");
        await OpenLightboxAsync();
        await BackAsync();
        await StillInTheReportAsync("Back with the lightbox open");
        await Expect(Page.Locator(Lightbox)).ToHaveCountAsync(0);

        await ForwardAsync();
        await Expect(Page.Locator(Lightbox)).ToHaveCountAsync(1);
        Assert.Equal("screenshot.png", await Page.Locator($"{Lightbox} img").GetAttributeAsync("alt"));
    }

    [Fact]
    public async Task L4_Back_closes_the_internal_flow_popup_Forward_opens_it_and_its_cross_leaves_no_entry()
    {
        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, "BackNav_L4.html", withFlow: 2, withoutFlow: 1);
        await OpenAsync(uri);
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 300000, PollingInterval = 200 });
        var popup = Page.Locator(".iflow-popup");
        // SVG rule: a dispatched click on the arrow's own text, which reaches the <a> the engine draws round it. Cancelable,
        // as a real click is: the link's handlers cancel it, and a click that cannot be cancelled also follows the link's
        // #iflow-<id>, an entry of its own that a real click never adds
        async Task OpenPopupAsync()
        {
            await Page.EvaluateAsync("""
                () => Array.from(document.querySelectorAll('.plantuml-browser:not(.iflow-diagram) svg text'))
                    .find(t => t.textContent.includes('with-flow-0') && !t.textContent.includes('without'))
                    .dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
                """);
            await Expect(popup).ToBeVisibleAsync();
        }

        await OpenPopupAsync();
        await BackAsync();
        await StillInTheReportAsync("Back with the popup open");
        await Expect(popup).ToHaveCountAsync(0);

        await ForwardAsync();
        await Expect(popup).ToBeVisibleAsync();
        await Expect(popup.Locator("h3")).ToHaveTextAsync("Internal Flow (2 spans)");

        await Page.Locator(".iflow-popup-close").ClickAsync();
        await Expect(popup).ToHaveCountAsync(0);
        await Page.WaitForFunctionAsync("() => !history.state || !history.state.kronikol || !history.state.kronikol.layer",
            null, new() { PollingInterval = 200, Timeout = 5000 });
        await Page.GoBackReportAsync();
        await Expect(Page).ToHaveTitleAsync(BeforeTitle, new() { Timeout = 15000 });
    }

    [Fact]
    public async Task L5_A_reload_with_the_lightbox_open_shows_the_place_without_it()
    {
        // a guard: the lightbox never survived a reload, and its entry must not bring it back
        await OpenWithImageAsync("BackNav_L5.html");
        await OpenLightboxAsync();

        await Page.ReloadReportAsync();
        await Page.Locator("details.feature").First.WaitForAsync();
        await SettledAsync(Page, 0);

        await Expect(Page.Locator(Lightbox)).ToHaveCountAsync(0);
        Assert.Null(await Page.EvaluateAsync<string?>("() => history.state && history.state.kronikol && history.state.kronikol.layer ? 'layer' : null"));
    }

    [Fact]
    public async Task L6_A_bare_fragment_leaves_the_place_and_an_open_layer_as_they_are()
    {
        // A fragment the report never writes (a note's #top, a diagram link's #iflow-<id> a click did not cancel) names no
        // place: read as a filter, it put the filters back to none and closed the open layer
        await OpenWithImageAsync("BackNav_L6.html");
        await OpenFilterRowsAsync();
        // the status of the image's own scenario, which the filter keeps on the page
        var status = await Page.Locator("img.attachment-image").First.EvaluateAsync<string>("i => i.closest('details.scenario').getAttribute('data-status')");
        await Page.Locator($".status-toggle[data-status='{status}']").ClickAsync();
        Assert.Equal("#status=" + status, await HashSoonAsync("#status=" + status));
        await OpenLightboxAsync();

        var n = await CountAsync(Page);
        await Page.EvaluateAsync("() => { location.hash = 'iflow-not-a-place'; }");
        await SettledAsync(Page, n);

        await Expect(Page.Locator(Lightbox)).ToHaveCountAsync(1);
        Assert.Equal("#iflow-not-a-place", await HashAsync());
        await Expect(Page.Locator($".status-toggle[data-status='{status}']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bstatus-active\\b"));
    }
}
