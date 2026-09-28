using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Diagrams)]
public class IflowPopupTests : PlaywrightTestBase
{
    public IflowPopupTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    private async Task<ILocator> WaitForActivityDiagramSvg(int timeoutMs = 15000)
    {
        var svg = Page.Locator(".iflow-diagram svg");
        await svg.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        return svg;
    }

    [Fact]
    public async Task Clicking_trigger_opens_popup_overlay()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage()));

        await Page.Locator("#trigger-seg-1").ClickAsync();

        var overlay = Page.Locator(".iflow-overlay");
        await Expect(overlay).ToBeVisibleAsync();

        var popup = Page.Locator(".iflow-popup");
        await Expect(popup).ToBeVisibleAsync();

        await WaitForActivityDiagramSvg();
        var diagramText = await Page.Locator(".iflow-diagram").First.TextContentAsync();
        Assert.DoesNotContain("Loading", diagramText!);
    }

    [Fact]
    public async Task Popup_shows_segment_title_and_content()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage()));
        await Page.Locator("#trigger-seg-1").ClickAsync();

        var popup = Page.Locator(".iflow-popup");
        await Expect(popup).ToBeVisibleAsync();

        await Expect(popup.Locator("h3")).ToContainTextAsync("Internal Flow");

        await WaitForActivityDiagramSvg();
        var svgText = await Page.Locator(".iflow-diagram svg").TextContentAsync();
        Assert.Contains("HTTP GET /api/orders", svgText!);
        Assert.Contains("SELECT", svgText!);
    }

    [Fact]
    public async Task Close_button_removes_overlay()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage()));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Page.Locator(".iflow-overlay").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await Page.Locator(".iflow-popup-close").ClickAsync();

        await Page.Locator(".iflow-overlay").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    [Fact]
    public async Task Escape_key_closes_popup()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage()));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Page.Locator(".iflow-overlay").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await Page.Keyboard.PressAsync("Escape");

        await Page.Locator(".iflow-overlay").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    [Fact]
    public async Task Clicking_overlay_background_closes_popup()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage()));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        var overlay = Page.Locator(".iflow-overlay");
        await overlay.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await overlay.EvaluateAsync("el => el.click()");

        await overlay.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    [Fact]
    public async Task Missing_segment_shows_no_data_message()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage()));
        await Page.Locator("#trigger-seg-missing").ClickAsync();

        var noData = Page.Locator(".iflow-popup .iflow-no-data");
        await Expect(noData).ToBeVisibleAsync();
        await Expect(noData).ToContainTextAsync("No internal flow data");
    }

    [Fact]
    public async Task Empty_segment_shows_no_activity_message()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeEmptySegment: true)));
        await Page.Locator("#trigger-seg-empty").ClickAsync();

        var noData = Page.Locator(".iflow-popup .iflow-no-data");
        await Expect(noData).ToBeVisibleAsync();
        await Expect(noData).ToContainTextAsync("No internal activity");
    }

    [Fact]
    public async Task Toggle_buttons_are_rendered_when_flame_chart_enabled()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();

        var popup = Page.Locator(".iflow-popup");
        await Expect(popup).ToBeVisibleAsync();

        // The popup is in the page before its segment is decoded (3.31.9), so the buttons are waited for.
        var toggleBtns = popup.Locator(".iflow-toggle-btn");
        await Expect(toggleBtns).ToHaveCountAsync(2);
        await Expect(toggleBtns.First).ToHaveTextAsync("Activity");
        await Expect(toggleBtns.Nth(1)).ToHaveTextAsync("Flame Chart");
    }

    [Fact]
    public async Task Activity_view_is_visible_by_default_flame_is_hidden()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();

        await Expect(Page.Locator(".iflow-view-main")).ToBeVisibleAsync();
        await Expect(Page.Locator(".iflow-view-flame")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Clicking_flame_chart_toggle_shows_flame_hides_activity()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();
        await WaitForActivityDiagramSvg();

        await Page.Locator(".iflow-toggle-btn").Nth(1).ClickAsync();

        await Expect(Page.Locator(".iflow-view-main")).Not.ToBeVisibleAsync();
        await Expect(Page.Locator(".iflow-view-flame")).ToBeVisibleAsync();

        var flameBars = Page.Locator(".iflow-flame-bar");
        Assert.True(await flameBars.CountAsync() >= 2);
    }

    [Fact]
    public async Task Clicking_activity_toggle_back_restores_activity_view()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();

        var toggleBtns = Page.Locator(".iflow-toggle-btn");
        await toggleBtns.Nth(1).ClickAsync();
        await toggleBtns.First.ClickAsync();

        await Expect(Page.Locator(".iflow-view-main")).ToBeVisibleAsync();
        await Expect(Page.Locator(".iflow-view-flame")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Active_toggle_button_has_active_class()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();

        var toggleBtns = Page.Locator(".iflow-toggle-btn");
        await Expect(toggleBtns.First).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("iflow-toggle-active"));
        var cls1 = await toggleBtns.Nth(1).GetAttributeAsync("class");
        Assert.DoesNotContain("iflow-toggle-active", cls1!);

        await toggleBtns.Nth(1).ClickAsync();
        var cls0After = await toggleBtns.First.GetAttributeAsync("class");
        Assert.DoesNotContain("iflow-toggle-active", cls0After!);
        await Expect(toggleBtns.Nth(1)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("iflow-toggle-active"));
    }

    [Fact]
    public async Task Flame_chart_has_flame_bars()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();

        await Page.Locator(".iflow-toggle-btn").Nth(1).ClickAsync();

        var flameBars = Page.Locator(".iflow-flame-bar");
        Assert.True(await flameBars.CountAsync() >= 2);
    }

    [Fact]
    public async Task Call_tree_view_renders_nested_list()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeCallTree: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();

        var callTree = Page.Locator(".iflow-call-tree");
        await Expect(callTree).ToBeVisibleAsync();

        var items = callTree.Locator("li");
        Assert.True(await items.CountAsync() >= 2);

        var treeText = await callTree.TextContentAsync();
        Assert.Contains("HTTP GET /api/orders", treeText!);
    }

    // ── The segment map as one blob (3.31.9, INTERNAL_FLOW_BLOB_PLAN §3.2 to §3.4) ──

    private const string SegmentElementPattern = "<script id=\"iflow-segments\" type=\"application/json\">[^<]*</script>";

    [Fact]
    public async Task The_popup_opens_before_the_map_is_decoded_and_fills_when_it_is()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage()));
        // Hold the decode until the test lets it go.
        await Page.EvaluateAsync("""
            () => {
                const real = window.decompressGzipBase64;
                window.decompressGzipBase64 = b64 => new Promise(resolve => { window._releaseDecode = () => resolve(real(b64)); });
            }
            """);

        await Page.Locator("#trigger-seg-1").ClickAsync();

        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();
        await Expect(Page.Locator(".iflow-popup .iflow-loading")).ToHaveTextAsync("Loading…");
        Assert.Equal(0, await Page.Locator(".iflow-popup h3").CountAsync());

        await Page.EvaluateAsync("() => window._releaseDecode()");

        await Expect(Page.Locator(".iflow-popup h3")).ToContainTextAsync("Internal Flow");
        await WaitForActivityDiagramSvg();
        Assert.Equal(0, await Page.Locator(".iflow-popup .iflow-loading").CountAsync());
    }

    [Fact]
    public async Task The_map_is_decoded_once_however_many_popups_open()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeEmptySegment: true)));
        // Count the decodes of the map itself; an activity diagram's own island goes through the same helper.
        await Page.EvaluateAsync("""
            () => {
                const z = JSON.parse(document.getElementById('iflow-segments').textContent).z;
                const real = window.decompressGzipBase64;
                window._mapDecodes = 0;
                window.decompressGzipBase64 = b64 => { if (b64 === z) window._mapDecodes++; return real(b64); };
            }
            """);

        await Page.Locator("#trigger-seg-1").ClickAsync();
        await WaitForActivityDiagramSvg();
        await Page.Locator(".iflow-popup-close").ClickAsync();
        await Page.EvaluateAsync("() => window._iflowShowPopup('iflow-seg-empty')");
        await Expect(Page.Locator(".iflow-popup .iflow-no-data")).ToContainTextAsync("No internal activity");
        await Page.EvaluateAsync("() => window._iflowShowPopup('iflow-seg-1')");
        await Expect(Page.Locator(".iflow-popup h3")).ToContainTextAsync("Internal Flow");

        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window._mapDecodes"));
    }

    [Fact]
    public async Task A_popup_draws_its_activity_diagram_with_no_decode_of_its_own()
    {
        // Q7: the diagram's PlantUML is in data-plantuml inside the map, so the map is the only thing decoded.
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage()));
        await Page.EvaluateAsync("""
            () => {
                const real = window.decompressGzipBase64;
                window._decodes = 0;
                window.decompressGzipBase64 = b64 => { window._decodes++; return real(b64); };
            }
            """);

        await Page.Locator("#trigger-seg-1").ClickAsync();
        await WaitForActivityDiagramSvg();

        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window._decodes"));
        Assert.NotNull(await Page.Locator(".iflow-diagram .plantuml-browser").First.GetAttributeAsync("data-plantuml"));
    }

    [Fact]
    public async Task A_second_popup_whose_diagram_is_already_drawn_still_draws_it()
    {
        // F12: the render shim writes a cached diagram at once, into whatever its target id names then. A popup that was
        // not in the page yet drew nothing; the popup is attached before it renders.
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeTwin: true)));

        await Page.Locator("#trigger-seg-1").ClickAsync();
        await WaitForActivityDiagramSvg();
        await Page.Locator(".iflow-popup-close").ClickAsync();
        await Page.Locator(".iflow-overlay").WaitForAsync(new() { State = WaitForSelectorState.Detached });

        await Page.Locator("#trigger-seg-twin").ClickAsync();
        var svg = await WaitForActivityDiagramSvg();
        Assert.Contains("HTTP GET /api/orders", await svg.First.TextContentAsync());
    }

    [Fact]
    public async Task A_map_that_is_not_gzip_says_so_in_the_popup()
    {
        var html = System.Text.RegularExpressions.Regex.Replace(
            TestPageGenerator.GenerateIflowPopupTestPage(), "\"z\":\"[^\"]*\"", "\"z\":\"bm90IGd6aXA=\"");
        var errors = new List<string>();
        Page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        await Page.GotoAsync(ServePage(html));

        await Page.Locator("#trigger-seg-1").ClickAsync();

        var failed = Page.Locator(".iflow-popup .iflow-load-failed");
        await Expect(failed).ToBeVisibleAsync();
        await Expect(failed).ToContainTextAsync("Internal flow data could not be decompressed: ");
        Assert.Equal(0, await Page.Locator(".iflow-popup .iflow-loading").CountAsync());
        // The console event reaches the test after the page wrote the message.
        for (var i = 0; i < 50 && !errors.Any(e => e.Contains("internal flow data could not be decompressed", StringComparison.Ordinal)); i++)
            await Task.Delay(200);
        Assert.Contains(errors, e => e.Contains("internal flow data could not be decompressed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("no DecompressionStream")]
    [InlineData("not base64")]
    public async Task A_map_the_browser_cannot_start_to_decode_says_so_in_the_popup(string cause)
    {
        // §3.4 and §3.5 name a browser without DecompressionStream (Chrome before 80, Firefox before 113, Safari before
        // 16.4) as the case the message is for; a blob atob refuses is the other way in. The decompressor throws for both
        // before it returns a promise, which left the popup on "Loading…" for good.
        var html = TestPageGenerator.GenerateIflowPopupTestPage();
        if (cause == "not base64")
            html = System.Text.RegularExpressions.Regex.Replace(html, "\"z\":\"[^\"]*\"", "\"z\":\"*not base64*\"");
        else
            await Page.AddInitScriptAsync("delete window.DecompressionStream;");
        var errors = new List<string>();
        Page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        await Page.GotoAsync(ServePage(html));

        await Page.Locator("#trigger-seg-1").ClickAsync();

        var failed = Page.Locator(".iflow-popup .iflow-load-failed");
        await Expect(failed).ToBeVisibleAsync();
        await Expect(failed).ToContainTextAsync("Internal flow data could not be decompressed: ");
        if (cause == "no DecompressionStream")
            await Expect(failed).ToContainTextAsync("DecompressionStream");
        Assert.Equal(0, await Page.Locator(".iflow-popup .iflow-loading").CountAsync());
        for (var i = 0; i < 50 && !errors.Any(e => e.Contains("internal flow data could not be decompressed", StringComparison.Ordinal)); i++)
            await Task.Delay(200);
        Assert.Contains(errors, e => e.Contains("internal flow data could not be decompressed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_popup_draws_a_span_whose_name_holds_attribute_markup()
    {
        // Q7 (§3.7, §8.5): the popup's activity diagram travels in a data-plantuml attribute with &, " and < escaped. The
        // browser's own reading of the attribute gives back the name as the report wrote it, a literal "&lt" included (a
        // reference a browser decodes even without its semicolon), and the engine draws it.
        const string name = "a \"b\" < c && d\tsays &lt";
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(childSpanName: name)));

        await Page.Locator("#trigger-seg-1").ClickAsync();
        var svg = await WaitForActivityDiagramSvg();

        var source = await Page.Locator(".iflow-popup .plantuml-browser").First.GetAttributeAsync("data-plantuml");
        Assert.Contains(":" + name + " (", source, StringComparison.Ordinal);
        var drawn = string.Concat((await svg.First.TextContentAsync())!.Where(c => !char.IsWhiteSpace(c)));
        Assert.Contains("a\"b\"<c&&d", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_with_no_segment_element_has_no_segment()
    {
        var page = TestPageGenerator.GenerateIflowPopupTestPage();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, SegmentElementPattern));
        await Page.GotoAsync(ServePage(System.Text.RegularExpressions.Regex.Replace(page, SegmentElementPattern, "")));

        Assert.True(await Page.EvaluateAsync<bool>("() => document.getElementById('iflow-segments') === null"));
        Assert.False(await Page.EvaluateAsync<bool>("() => window._iflowHasSegment('iflow-seg-1')"));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup .iflow-no-data")).ToContainTextAsync("No internal flow data");
    }

    [Fact]
    public async Task A_page_that_sets_the_old_global_is_read_as_before()
    {
        // A page built by hand, or carrying an older emitter's script, sets window.__iflowSegments itself.
        var html = System.Text.RegularExpressions.Regex.Replace(TestPageGenerator.GenerateIflowPopupTestPage(), SegmentElementPattern,
            "<script>window.__iflowSegments = { 'iflow-seg-1': { title: 'Legacy flow', content: '<p class=\"legacy\">flow</p>' } };</script>");
        await Page.GotoAsync(ServePage(html));

        Assert.True(await Page.EvaluateAsync<bool>("() => window._iflowHasSegment('iflow-seg-1')"));
        Assert.False(await Page.EvaluateAsync<bool>("() => window._iflowHasSegment('iflow-nonexistent')"));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup h3")).ToHaveTextAsync("Legacy flow");
        await Expect(Page.Locator(".iflow-popup p.legacy")).ToHaveTextAsync("flow");
    }

    [Fact]
    public async Task Under_HideLink_a_server_drawn_link_with_no_segment_opens_nothing()
    {
        // F4 (INTERNAL_FLOW_BLOB_PLAN S3): Server and Local rendering put the SVG in the page with every link Kronikol wrote
        // as an <a>, and no render script runs to hide one; the popup script's click handler opened a popup saying
        // there was no data for a link whose segment HideLink had dropped. The page is shaped as such a report is.
        var withSegment = TestPageGenerator.GenerateIflowPopupTestPage();
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="400" height="120">
              <a href="#iflow-seg-1" xlink:href="#iflow-seg-1"><text id="t-live" x="10" y="30" fill="#0000FF">GET /live</text></a>
              <a href="#iflow-dropped" xlink:href="#iflow-dropped"><text id="t-dead" x="10" y="80" fill="#0000FF">GET /dead</text></a>
            </svg>
            """;
        await Page.GotoAsync(ServePage(withSegment.Replace("<h1 id=\"page-title\">", svg + "<h1 id=\"page-title\">")));

        var dead = Page.Locator("#t-dead");
        Assert.Null(await dead.EvaluateAsync<string?>("t => t.parentNode.getAttribute('href')"));
        Assert.Null(await dead.EvaluateAsync<string?>("t => t.parentNode.getAttribute('xlink:href')"));
        Assert.NotEqual("pointer", await dead.EvaluateAsync<string>("t => getComputedStyle(t).cursor"));
        await dead.EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))");
        await Page.WaitForTimeoutAsync(500);
        Assert.Equal(0, await Page.Locator(".iflow-overlay").CountAsync());

        // The link with a segment keeps its href and opens it.
        await Page.Locator("#t-live").EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))");
        await Expect(Page.Locator(".iflow-popup h3")).ToContainTextAsync("Internal Flow");
    }

    [Fact]
    public async Task A_server_drawn_link_added_after_the_page_loaded_is_checked_when_it_is_clicked()
    {
        // The load-time pass cannot see a link that arrives later: the click handler asks, and a link with no segment
        // loses its href there and opens nothing, while one with a segment opens it.
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage()));
        await Page.EvaluateAsync("""
            () => {
                const ns = 'http://www.w3.org/2000/svg', xlink = 'http://www.w3.org/1999/xlink';
                const svg = document.createElementNS(ns, 'svg');
                svg.setAttribute('width', '400');
                svg.setAttribute('height', '120');
                [['iflow-dropped', 't-late-dead', 30], ['iflow-seg-1', 't-late-live', 80]].forEach(([id, textId, y]) => {
                    const a = document.createElementNS(ns, 'a');
                    a.setAttribute('href', '#' + id);
                    a.setAttributeNS(xlink, 'xlink:href', '#' + id);
                    const text = document.createElementNS(ns, 'text');
                    text.id = textId;
                    text.setAttribute('x', '10');
                    text.setAttribute('y', String(y));
                    text.setAttribute('fill', '#0000FF');
                    text.textContent = 'GET /' + textId;
                    a.appendChild(text);
                    svg.appendChild(a);
                });
                document.body.insertBefore(svg, document.getElementById('page-title'));
            }
            """);

        var dead = Page.Locator("#t-late-dead");
        Assert.Equal("#iflow-dropped", await dead.EvaluateAsync<string?>("t => t.parentNode.getAttribute('href')"));
        await dead.EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))");
        await Page.WaitForTimeoutAsync(500);
        Assert.Equal(0, await Page.Locator(".iflow-overlay").CountAsync());
        Assert.Null(await dead.EvaluateAsync<string?>("t => t.parentNode.getAttribute('href')"));

        await Page.Locator("#t-late-live").EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))");
        await Expect(Page.Locator(".iflow-popup h3")).ToContainTextAsync("Internal Flow");
    }

    [Fact]
    public async Task Opening_new_popup_replaces_existing_one()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeEmptySegment: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Page.Locator(".iflow-popup").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await Page.EvaluateAsync("window._iflowShowPopup('iflow-seg-empty')");
        await Page.Locator(".iflow-no-data").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.Equal(1, await Page.Locator(".iflow-overlay").CountAsync());
    }

    [Fact]
    public async Task Context_menu_appears_on_activity_diagram_right_click()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeContextMenu: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();
        var svg = await WaitForActivityDiagramSvg();

        await DispatchContextMenu(svg.First);

        var menu = Page.Locator(".diagram-ctx-menu");
        await Expect(menu).ToBeVisibleAsync(new() { Timeout = 5000 });

        var menuText = await menu.TextContentAsync();
        Assert.Contains("Copy image", menuText!);
        Assert.Contains("Save image", menuText!);
        Assert.Contains("Open image in new tab", menuText!);
    }

    [Fact]
    public async Task Context_menu_z_index_is_above_popup_overlay()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeContextMenu: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();
        var svg = await WaitForActivityDiagramSvg();

        await DispatchContextMenu(svg.First);
        var menu = Page.Locator(".diagram-ctx-menu");
        await Expect(menu).ToBeVisibleAsync();

        var zIndex = await menu.EvaluateAsync<string>("el => getComputedStyle(el).zIndex");
        Assert.Equal("20001", zIndex);
    }

    [Fact]
    public async Task Context_menu_on_flame_chart_has_png_only()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true, includeContextMenu: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();
        await WaitForActivityDiagramSvg();

        await Page.Locator(".iflow-toggle-btn").Nth(1).ClickAsync();

        await DispatchContextMenu(Page.Locator(".iflow-flame-bar").First);
        var menu = Page.Locator(".diagram-ctx-menu");
        await Expect(menu).ToBeVisibleAsync();

        var menuText = await menu.TextContentAsync();
        Assert.Contains("Copy as PNG", menuText!);
        Assert.Contains("Save as PNG", menuText!);
        Assert.DoesNotContain("Copy as SVG", menuText!);
        Assert.DoesNotContain("Copy PlantUML source", menuText!);
    }

    [Fact]
    public async Task PNG_no_transparency_opaque_for_InlineSvg_sequence_diagram()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateInlineSvgSequenceDiagramPage()));
        await Page.Locator(".plantuml-inline-svg svg").WaitForAsync();

        var result = await Page.EvaluateAsync<string>(PngOpacityCheckScript(".plantuml-inline-svg svg"));
        var data = System.Text.Json.JsonDocument.Parse(result).RootElement;
        Assert.False(data.TryGetProperty("error", out _), $"Error: {result}");
        Assert.Equal(255, data.GetProperty("tl").GetInt32());
        Assert.Equal(255, data.GetProperty("tr").GetInt32());
        Assert.Equal(255, data.GetProperty("bl").GetInt32());
        Assert.Equal(255, data.GetProperty("br").GetInt32());
    }

    [Fact]
    public async Task PNG_no_transparency_opaque_for_BrowserJs_sequence_diagram()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateBrowserJsSequenceDiagramPage()));
        await Page.Locator(".plantuml-browser svg").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var result = await Page.EvaluateAsync<string>(PngOpacityCheckScript(".plantuml-browser svg"));
        var data = System.Text.Json.JsonDocument.Parse(result).RootElement;
        Assert.False(data.TryGetProperty("error", out _), $"Error: {result}");
        Assert.Equal(255, data.GetProperty("tl").GetInt32());
        Assert.Equal(255, data.GetProperty("tr").GetInt32());
        Assert.Equal(255, data.GetProperty("bl").GetInt32());
        Assert.Equal(255, data.GetProperty("br").GetInt32());
    }

    [Fact]
    public async Task Save_as_PNG_no_transparency_produces_opaque_pixels()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeContextMenu: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Expect(Page.Locator(".iflow-popup")).ToBeVisibleAsync();
        await WaitForActivityDiagramSvg();

        var result = await Page.EvaluateAsync<string>(PngOpacityCheckScript(".iflow-popup svg"));
        var data = System.Text.Json.JsonDocument.Parse(result).RootElement;
        Assert.False(data.TryGetProperty("error", out _), $"Error: {result}");
        Assert.Equal(255, data.GetProperty("tl").GetInt32());
        Assert.Equal(255, data.GetProperty("tr").GetInt32());
        Assert.Equal(255, data.GetProperty("bl").GetInt32());
        Assert.Equal(255, data.GetProperty("br").GetInt32());
    }

    [Fact]
    public async Task Popup_styles_are_applied_from_stylesheet()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        var popup = Page.Locator(".iflow-popup");
        await Expect(popup).ToBeVisibleAsync();

        // Overlay: fixed positioning
        var overlay = Page.Locator(".iflow-overlay");
        var overlayPosition = await overlay.EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.Equal("fixed", overlayPosition);
        var overlayZIndex = await overlay.EvaluateAsync<string>("el => getComputedStyle(el).zIndex");
        Assert.Equal("20000", overlayZIndex);

        // Popup: white background, rounded corners, box-shadow
        var popupBg = await popup.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.Equal("rgb(255, 255, 255)", popupBg);
        var popupRadius = await popup.EvaluateAsync<string>("el => getComputedStyle(el).borderRadius");
        Assert.Equal("8px", popupRadius);
        var popupShadow = await popup.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
        Assert.NotEqual("none", popupShadow);

        // Close button: absolute positioning
        var closePos = await popup.Locator(".iflow-popup-close")
            .EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.Equal("absolute", closePos);

        // Toggle buttons: border-radius
        var toggleBtns = popup.Locator(".iflow-toggle-btn");
        var count = await toggleBtns.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var radius = await toggleBtns.Nth(i).EvaluateAsync<string>("el => getComputedStyle(el).borderRadius");
            Assert.Equal("4px", radius);
        }

        // Active toggle: blue background
        var activeBg = await popup.Locator(".iflow-toggle-active")
            .EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.Equal("rgb(66, 133, 244)", activeBg);
    }

    private static string PngOpacityCheckScript(string svgSelector) => $$"""
        (async () => {
            var svg = document.querySelector('{{svgSelector}}');
            if (!svg) return JSON.stringify({error:'no svg'});
            var svgData = new XMLSerializer().serializeToString(svg);
            var url = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svgData)));
            return await new Promise((resolve) => {
                var img = new Image();
                img.onload = function() {
                    var w = img.naturalWidth, h = img.naturalHeight;
                    if (w === 0 || h === 0) { resolve(JSON.stringify({error:'zero size'})); return; }
                    var canvas = document.createElement('canvas');
                    canvas.width = w; canvas.height = h;
                    var ctx = canvas.getContext('2d');
                    ctx.fillStyle = '#ffffff';
                    ctx.fillRect(0, 0, w, h);
                    ctx.drawImage(img, 0, 0);
                    var tl = ctx.getImageData(0, 0, 1, 1).data[3];
                    var tr = ctx.getImageData(w-1, 0, 1, 1).data[3];
                    var bl = ctx.getImageData(0, h-1, 1, 1).data[3];
                    var br = ctx.getImageData(w-1, h-1, 1, 1).data[3];
                    resolve(JSON.stringify({tl:tl,tr:tr,bl:bl,br:br,w:w,h:h}));
                };
                img.onerror = function() { resolve(JSON.stringify({error:'img load failed'})); };
                img.src = url;
            });
        })()
    """;
}
