using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Mobile)]
public class MobileResponsiveTests : PlaywrightTestBase
{
    public MobileResponsiveTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 375;
    protected override int ViewportHeight => 812;

    private async Task<string> GetComputedStyle(ILocator el, string prop) =>
        await el.EvaluateAsync<string>("(e, p) => window.getComputedStyle(e).getPropertyValue(p)", prop);

    // ── Viewport meta tag ──

    [Fact]
    public async Task Report_has_viewport_meta_tag()
    {
        await Page.GotoReportAsync(GenerateReport("MobileViewport.html"));
        var meta = Page.Locator("meta[name='viewport']");
        var content = await meta.GetAttributeAsync("content");
        Assert.Contains("width=device-width", content);
    }

    [Fact]
    public async Task Report_has_doctype_and_charset()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDoctype.html"));
        var charset = Page.Locator("meta[charset]");
        Assert.Equal("utf-8", await charset.GetAttributeAsync("charset"));
    }

    // ── The run band fits a phone (5.0.0: the band in place of the 4.x header row, which stacked) ──

    [Fact]
    public async Task Run_band_fits_a_phone_with_a_smaller_ring()
    {
        await Page.GotoReportAsync(GenerateReport("MobileHeaderStack.html"));
        var band = Page.Locator(".run-band");
        await band.WaitForAsync(new() { Timeout = 5000 });
        var right = await band.EvaluateAsync<double>("e => e.getBoundingClientRect().right");
        Assert.True(right <= 375, $"the band ends at {right}, past the 375 px viewport");
        Assert.Equal(52, await Page.Locator(".run-ring").EvaluateAsync<double>("e => e.getBoundingClientRect().width"), 1);
    }

    // ── Toolbar stacks vertically ──

    [Fact]
    public async Task Toolbar_stacks_vertically_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileToolbar.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var toolbar = Page.Locator(".toolbar-row");
        var flexDirection = await GetComputedStyle(toolbar, "flex-direction");
        Assert.Equal("column", flexDirection);
    }

    // ── No horizontal overflow ──

    [Fact]
    public async Task Page_does_not_overflow_horizontally()
    {
        await Page.GotoReportAsync(GenerateReport("MobileOverflow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var viewportWidth = await Page.EvaluateAsync<long>("() => window.innerWidth");
        var scrollWidth = await Page.EvaluateAsync<long>("() => document.documentElement.scrollWidth");

        Assert.True(scrollWidth <= viewportWidth + 2,
            $"Page overflows: scrollWidth={scrollWidth}, viewportWidth={viewportWidth}");
    }

    // ── Filter buttons wrap ──

    [Fact]
    public async Task Filter_row_stacks_vertically_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileFilters.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // the box opens with the bar's Filters (plans/SEARCH_PLAN.md F3), which a phone has on its line under the tabs
        await Page.Locator(".nav-filters-toggle").ClickAsync();

        var filterRow = Page.Locator(".filter-row");
        await filterRow.WaitForAsync(new() { Timeout = 5000 });
        var flexDirection = await GetComputedStyle(filterRow, "flex-direction");
        Assert.Equal("column", flexDirection);
    }

    // ── Tables scroll horizontally ──

    [Fact]
    public async Task Feature_summary_table_scrolls_horizontally()
    {
        await Page.GotoReportAsync(GenerateReport("MobileTable.html"));
        // 5.0.0: the table is the Features tab's Summary view (plans/OPTION_C_PLAN.md S4)
        await Page.Locator("#view-summary").ClickAsync();

        var table = Page.Locator(".feature-summary-table");
        var display = await GetComputedStyle(table, "display");
        Assert.Equal("block", display);
    }

    // ── Filtering box takes full width ──

    [Fact]
    public async Task Filtering_box_takes_full_width_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileFilterBox.html"));
        await Page.Locator(".nav-filters-toggle").ClickAsync();
        var filterBox = Page.Locator(".filtering-box");
        await filterBox.WaitForAsync(new() { Timeout = 5000 });

        var boxSizing = await GetComputedStyle(filterBox, "box-sizing");
        Assert.Equal("border-box", boxSizing);

        var viewportWidth = await Page.EvaluateAsync<long>("() => window.innerWidth");
        var box = await filterBox.BoundingBoxAsync();
        Assert.True(box!.Width <= viewportWidth,
            $"Filter box width ({box.Width}) exceeds viewport ({viewportWidth})");
    }

    // ── Jump-to-failure button is accessible ──

    [Fact]
    public async Task Jump_to_failure_button_visible_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileJump.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var buttons = Page.Locator(".jump-to-failure");
        if (await buttons.CountAsync() > 0)
        {
            var btn = buttons.First;
            await Expect(btn).ToBeVisibleAsync();
            var right = await Page.EvaluateAsync<long>(
                "(el) => el.getBoundingClientRect().right", await btn.ElementHandleAsync());
            var viewportWidth = await Page.EvaluateAsync<long>("() => window.innerWidth");
            Assert.True(right <= viewportWidth,
                $"Button right edge ({right}) exceeds viewport ({viewportWidth})");
        }
    }

    // ── Desktop is unaffected at full width ──

    [Fact]
    public async Task Desktop_layout_unaffected_at_1920_width()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDesktopCheck.html"));
        await Page.Locator(".run-band").WaitForAsync(new() { Timeout = 5000 });

        // At 375px the ring is the phone's 52px (5.0.0: the run band in place of the 4.x header row)
        var ring = Page.Locator(".run-ring");
        Assert.Equal(52, await ring.EvaluateAsync<double>("e => e.getBoundingClientRect().width"), 1);

        // Resize viewport to desktop
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.ReloadReportAsync();
        await Page.Locator(".run-band").WaitForAsync(new() { Timeout = 5000 });
        Assert.Equal(64, await Page.Locator(".run-ring").EvaluateAsync<double>("e => e.getBoundingClientRect().width"), 1);

        // Restore mobile size
        await Page.SetViewportSizeAsync(375, 812);
    }

    // ── The ring is centred on the summary beside it (5.0.0) ──

    [Fact]
    public async Task Run_ring_is_centred_on_the_summary_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileChartCenter.html"));
        var ring = Page.Locator(".run-ring");
        await ring.WaitForAsync(new() { Timeout = 5000 });
        var middles = await Page.EvaluateAsync<double[]>(
            "() => ['.run-ring', '.run-summary'].map(s => { const r = document.querySelector(s).getBoundingClientRect(); return r.top + r.height / 2; })");
        Assert.True(Math.Abs(middles[0] - middles[1]) < 1, $"ring middle {middles[0]}, summary middle {middles[1]}");
    }

    // ── Details/Headers wraps on mobile ──

    [Fact]
    public async Task Toolbar_right_details_does_not_overflow()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDetailsOverflow.html"));
        // since 5.0.0 a phone shows the display settings behind the Features tools' gear (plans/OPTION_C_PLAN.md S4)
        await Page.Locator(".tools-gear").ClickAsync();
        await Page.Locator(".toolbar-right").WaitForAsync(new() { Timeout = 5000 });

        var toolbarRight = Page.Locator(".toolbar-right");
        var viewportWidth = await Page.EvaluateAsync<long>("() => window.innerWidth");
        var rightEdge = await Page.EvaluateAsync<long>(
            "(el) => el.getBoundingClientRect().right", await toolbarRight.ElementHandleAsync());

        Assert.True(rightEdge <= viewportWidth + 2,
            $"Toolbar-right overflows: right={rightEdge}, viewport={viewportWidth}");
    }

    [Fact]
    public async Task Diagram_toggle_wraps_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDiagramToggle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.EvaluateAsync("""
            () => {
                document.querySelectorAll('details.feature')[0]?.setAttribute('open','');
                document.querySelectorAll('details.scenario')[0]?.setAttribute('open','');
                document.querySelectorAll('details.example-diagrams')[0]?.setAttribute('open','');
            }
        """);

        // On a phone a scenario's settings open from the gear on its diagrams' title (5.0.0), so the bar is
        // measured after the tap that shows it: read off the hidden box, flex-wrap says nothing about what a
        // reader sees.
        await Page.Locator(".gear-in-summary").First.ClickAsync();
        var toggle = Page.Locator(".diagram-options-bar").First;
        await Expect(toggle).ToBeVisibleAsync();
        Assert.Equal("flex", await GetComputedStyle(toggle, "display"));
        Assert.Equal("wrap", await GetComputedStyle(toggle, "flex-wrap"));

        var pastEdge = await toggle.EvaluateAsync<double>("""
            t => Math.max(0, ...[...t.querySelectorAll('button, select')]
                .map(k => k.getBoundingClientRect().right - t.getBoundingClientRect().right))
            """);
        Assert.True(pastEdge <= 1, $"a toolbar control ends {pastEdge} px past the toolbar's edge");
    }

    [Fact]
    public async Task Headers_toggle_has_no_left_margin_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileHeadersMargin.html"));
        // since 5.0.0 a phone shows the display settings behind the Features tools' gear (plans/OPTION_C_PLAN.md S4)
        await Page.Locator(".tools-gear").ClickAsync();
        await Page.Locator(".toolbar-right").WaitForAsync(new() { Timeout = 5000 });

        var headersToggle = Page.Locator(".toolbar-right .toggle-btn[data-toggle='headers']");
        if (await headersToggle.CountAsync() > 0)
        {
            var marginLeft = await GetComputedStyle(headersToggle.First, "margin-left");
            Assert.Equal("0px", marginLeft);
        }
    }

    // ── Diagram toggle buttons ──

    [Fact]
    public async Task Diagram_toggle_buttons_have_no_max_width_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDiagramBtns.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.EvaluateAsync("""
            () => {
                document.querySelectorAll('details.feature')[0]?.setAttribute('open','');
                document.querySelectorAll('details.scenario')[0]?.setAttribute('open','');
                document.querySelectorAll('details.example-diagrams')[0]?.setAttribute('open','');
            }
        """);

        var btns = Page.Locator(".diagram-toggle-btn");
        if (await btns.CountAsync() > 0)
        {
            var maxWidth = await GetComputedStyle(btns.First, "max-width");
            Assert.Equal("none", maxWidth);
            var textAlign = await GetComputedStyle(btns.First, "text-align");
            Assert.Equal("center", textAlign);
        }
    }

    // ── Zoom controls hidden on mobile ──

    [Fact]
    public async Task Zoom_controls_hidden_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileZoomHidden.html"));
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // since 5.7.0 a diagram that fits has a slider too (plans/ZOOM_EVERY_DIAGRAM_PLAN.md): it is there, and hidden
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('[data-diagram-type=plantuml]').querySelector(':scope > .diagram-zoom-controls')",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        var zoomControls = Page.Locator(".diagram-zoom-controls");
        for (var i = 0; i < await zoomControls.CountAsync(); i++)
            Assert.Equal("none", await GetComputedStyle(zoomControls.Nth(i), "display"));
    }

    // ── Context menu is bottom-sheet on mobile ──

    [Fact]
    public async Task Context_menu_renders_as_bottom_sheet_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileContextMenu.html"));
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg();

        // Trigger context menu via JS dispatchEvent
        await svg.EvaluateAsync("""
            (el) => el.dispatchEvent(new MouseEvent('contextmenu', {
                bubbles: true, cancelable: true, clientX: 100, clientY: 100
            }))
        """);

        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Verify the menu spans the full viewport width (bottom-sheet style)
        var box = await menu.BoundingBoxAsync();
        var viewportWidth = await Page.EvaluateAsync<long>("() => window.innerWidth");
        Assert.True(box!.Width >= viewportWidth - 2,
            $"Menu width ({box.Width}) should span full viewport ({viewportWidth})");

        // Verify it's anchored at the bottom of the viewport
        var viewportHeight = await Page.EvaluateAsync<long>("() => window.innerHeight");
        var menuBottom = box.Y + box.Height;
        Assert.True(menuBottom >= viewportHeight - 2,
            $"Menu bottom ({menuBottom}) should be at viewport bottom ({viewportHeight})");

        // Verify menu items have larger tap targets
        var item = menu.Locator("> div").First;
        var padding = await GetComputedStyle(item, "padding-top");
        var paddingPx = double.Parse(padding.Replace("px", ""));
        Assert.True(paddingPx >= 10, $"Menu item padding too small for touch: {padding}");
    }

    // ── #2 The filter box, hidden until the bar's Filters opens it (plans/SEARCH_PLAN.md F3, F5) ──

    [Fact]
    public async Task Filters_collapsed_by_default_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileFiltersCollapse1.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Expect(Page.Locator(".nav-filters-toggle")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mobile-filter-toggle")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#filtering-box")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Filter_toggle_expands_and_collapses_filters()
    {
        await Page.GotoReportAsync(GenerateReport("MobileFiltersCollapse2.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        var toggle = Page.Locator(".nav-filters-toggle");

        await toggle.ClickAsync();
        await Expect(Page.Locator("#filtering-box")).ToBeVisibleAsync();
        await Expect(Page.Locator(".status-toggle").First).ToBeVisibleAsync();

        await toggle.ClickAsync();
        await Expect(Page.Locator("#filtering-box")).ToBeHiddenAsync();
    }

    // ── #7 The search in reach: the title line's button, and down the page the floating one (B15) ──

    [Fact]
    public async Task On_a_phone_the_way_to_search_stays_in_reach_down_the_page()
    {
        await Page.GotoReportAsync(GenerateReport("MobileStickySearch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Expect(Page.Locator("#search-button")).ToBeVisibleAsync();
        await Expect(Page.Locator("#search-fab")).ToBeHiddenAsync();

        // the features open so the page runs far past the title line
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        await Page.EvaluateAsync("() => window.scrollTo(0, Math.min(3000, document.documentElement.scrollHeight - window.innerHeight))");
        await TwoAnimationFramesAsync(Page);
        Assert.True(await Page.EvaluateAsync<double>("() => window.scrollY") > 600, "the page is too short to leave the title line behind");

        var fab = Page.Locator("#search-fab");
        await Expect(fab).ToBeVisibleAsync();
        var box = (await fab.BoundingBoxAsync())!;
        var size = Page.ViewportSize!;
        Assert.True(box.Y >= 0 && box.Y + box.Height <= size.Height && box.X >= 0 && box.X + box.Width <= size.Width, "the floating search button is outside the window");

        await fab.ClickAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
        await Expect(Page.Locator(".search-foot-filters")).ToBeVisibleAsync();
    }

    // ── #10 Back-to-top FAB ──

    [Fact]
    public async Task Back_to_top_hidden_initially()
    {
        await Page.GotoReportAsync(GenerateReport("MobileBackToTop1.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var display = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.getElementById('back-to-top')).display");
        Assert.Equal("none", display);
    }

    [Fact]
    public async Task Back_to_top_appears_after_scrolling()
    {
        await Page.GotoReportAsync(GenerateReport("MobileBackToTop2.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Expand features and inject a spacer to guarantee enough page height
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
        await Page.EvaluateAsync("() => { var d = document.createElement('div'); d.style.height = '3000px'; document.body.appendChild(d); }");

        // Scroll to bottom of document
        await Page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");

        // Wait for the back-to-top button to appear
        await Page.WaitForFunctionAsync(
            "() => document.getElementById('back-to-top').style.display === 'block'",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var display = await Page.EvaluateAsync<string>(
            "() => document.getElementById('back-to-top').style.display");
        Assert.Equal("block", display);
    }

    [Fact]
    public async Task Back_to_top_scrolls_to_top()
    {
        await Page.GotoReportAsync(GenerateReport("MobileBackToTop3.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
        await Page.EvaluateAsync("() => { var d = document.createElement('div'); d.style.height = '3000px'; document.body.appendChild(d); }");

        // Scroll to bottom of document
        await Page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
        await Page.WaitForFunctionAsync(
            "() => document.getElementById('back-to-top').style.display === 'block'",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        // Click back-to-top
        await Page.Locator("#back-to-top").ClickAsync();

        // Wait for scroll to top
        await Page.WaitForFunctionAsync(
            "() => window.scrollY < 10",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        var scrollY = await Page.EvaluateAsync<double>("() => window.scrollY");
        Assert.True(scrollY < 10, $"Expected scroll near top, but was {scrollY}");
    }

    // ── #1 Diagram scrollable container ──

    [Fact]
    public async Task Diagram_container_is_horizontally_scrollable()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDiagramScrollable.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        var overflowX = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.plantuml-browser')).overflowX");
        Assert.Equal("auto", overflowX);
    }

    // ── #1 Diagram fullscreen overlay removed ──

    [Fact]
    public async Task Diagram_fullscreen_overlay_does_not_exist_in_dom()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDiagramFullscreen.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var exists = await Page.EvaluateAsync<bool>(
            "() => document.getElementById('diagram-fullscreen-overlay') !== null");
        Assert.False(exists, "Diagram fullscreen overlay should have been removed");
    }

    [Fact]
    public async Task Diagram_containers_do_not_have_zoom_in_cursor()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDiagramNoCursor.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        var cursor = await Page.EvaluateAsync<string>(
            "() => { var el = document.querySelector('.plantuml-browser'); return el ? el.style.cursor : ''; }");
        Assert.NotEqual("zoom-in", cursor);
    }

    // ── #4 Per-scenario diagram controls toggle ──

    [Fact]
    public async Task Diagram_controls_toggle_button_appears_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDiagramCtrlToggle1.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        // Force diagram rendering
        await Page.EvaluateAsync(
            "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");

        // Wait for SVG
        await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        // The gear on the diagrams' title is visible, the one at the diagram's corner is not (5.0.0)
        var toggleBtn = Page.Locator(".gear-in-summary").First;
        await Expect(toggleBtn).ToBeVisibleAsync();
        Assert.Equal("Diagram settings", await toggleBtn.GetAttributeAsync("aria-label"));
        await Expect(Page.Locator(".scenario-settings-gear").First).ToBeHiddenAsync();

        // The settings are closed until it is pressed
        await Expect(Page.Locator(".diagram-options-bar").First).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Diagram_controls_toggle_shows_and_hides_controls()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDiagramCtrlToggle2.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        await Page.EvaluateAsync(
            "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var toggleBtn = Page.Locator(".gear-in-summary").First;
        var bar = Page.Locator(".diagram-options-bar").First;

        // Click to show the settings: the section stays open, the gear says it is
        await toggleBtn.ClickAsync();
        await Expect(bar).ToBeVisibleAsync();
        Assert.Equal("true", await toggleBtn.GetAttributeAsync("aria-expanded"));
        Assert.True(await Page.Locator("details.example-diagrams").First.EvaluateAsync<bool>("d => d.open"));

        // Click to hide them
        await toggleBtn.ClickAsync();
        await Expect(bar).ToBeHiddenAsync();
        Assert.Equal("false", await toggleBtn.GetAttributeAsync("aria-expanded"));
    }

    // ── #6 Touch target sizes ──

    [Fact]
    public async Task Touch_targets_meet_minimum_height()
    {
        await Page.GotoReportAsync(GenerateReport("MobileTouchTargets.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Check that status toggle buttons have at least 36px min-height
        var minHeight = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.status-toggle')).minHeight");
        Assert.Equal("36px", minHeight);
    }

    // ── #5 Dependency button overflow truncation ──

    [Fact]
    public async Task Dependency_buttons_truncate_long_text()
    {
        await Page.GotoReportAsync(GenerateReport("MobileDepTruncate.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // the box opens with the bar's Filters
        var toggle = Page.Locator(".nav-filters-toggle");
        await toggle.ClickAsync();

        var overflow = await Page.EvaluateAsync<string>(
            "() => { var b = document.querySelector('.dependency-toggle'); return b ? getComputedStyle(b).overflow : ''; }");
        Assert.Equal("hidden", overflow);
    }

    // ── #9 Step duration white-space ──

    [Fact]
    public async Task Step_duration_does_not_wrap()
    {
        await Page.GotoReportAsync(GenerateReport("MobileStepDuration.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        // Inject a .step-duration element to verify the CSS rule applies
        await Page.EvaluateAsync("""
            () => {
                var step = document.querySelector('.step');
                if (step) {
                    var span = document.createElement('span');
                    span.className = 'step-duration';
                    span.textContent = '(1.234s)';
                    step.appendChild(span);
                }
            }
        """);

        var whiteSpace = await Page.EvaluateAsync<string>(
            "() => { var d = document.querySelector('.step-duration'); return d ? getComputedStyle(d).whiteSpace : ''; }");
        Assert.Equal("nowrap", whiteSpace);
    }

    // ── Diagram settings button full width ──

    [Fact]
    public async Task Diagram_settings_button_is_block_display_on_mobile()
    {
        await Page.GotoReportAsync(GenerateReport("MobileSettingsBtn.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        await Page.EvaluateAsync(
            "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        // 5.0.0: the gear sits at the right end of the diagrams' title, floated, so it computes as a block
        var toggleBtn = Page.Locator(".gear-in-summary").First;
        await Expect(toggleBtn).ToBeVisibleAsync();

        Assert.Equal("block", await GetComputedStyle(toggleBtn, "display"));
        Assert.Equal("right", await GetComputedStyle(toggleBtn, "float"));
    }

    // ── The CI zone under the summary on a phone (5.0.0: the run band; the 4.x test's report had no CI, so it measured nothing) ──
    // covered by RunBandLayoutTests.On_a_narrow_page_the_ci_zone_moves_under_the_summary_and_back
    // ── 5.9.1 (plans/REPORT_UI_FIXES_PLAN.md I7, E1, E19): the phone's sheets and controls use the width they are given ──

    // each case sets its own viewport: the class opens at 375 by 812
    private async Task OpenAtAsync(int width, string url)
    {
        await Page.SetViewportSizeAsync(width, 812);
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    // a box's own children (and the selects inside them) against the room the box gives them, as JSON: { inner, parts }
    private const string AgainstTheirBox = """
        (s, inside) => {
            const cs = getComputedStyle(s);
            const inner = s.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
            const parts = [...s.querySelectorAll(inside)].filter(e => e.getBoundingClientRect().width > 0)
                .map(e => ({ name: (e.className || e.tagName).split(' ')[0], w: e.getBoundingClientRect().width,
                    top: e.getBoundingClientRect().top, bottom: e.getBoundingClientRect().bottom }));
            return JSON.stringify({ inner, parts });
        }
        """;

    private sealed record Part(string Name, double W, double Top, double Bottom);
    private sealed record Parts(double Inner, Part[] List);

    private static async Task<Parts> MeasureAsync(ILocator box, string inside)
    {
        var json = await box.EvaluateAsync<string>(AgainstTheirBox, inside);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var parts = doc.RootElement.GetProperty("parts").EnumerateArray()
            .Select(p => new Part(p.GetProperty("name").GetString()!, p.GetProperty("w").GetDouble(), p.GetProperty("top").GetDouble(), p.GetProperty("bottom").GetDouble()))
            .ToArray();
        return new Parts(doc.RootElement.GetProperty("inner").GetDouble(), parts);
    }

    private static void AllAsWide(Parts measured, string what, int width)
    {
        Assert.NotEmpty(measured.List);
        var narrow = measured.List.Where(p => Math.Abs(p.W - measured.Inner) > 1).Select(p => $"{p.Name} {p.W:F0}").ToArray();
        Assert.True(narrow.Length == 0, $"{width} px: {what} gives {measured.Inner:F0} px, but " + string.Join(", ", narrow));
    }

    // E1, E19: every filter control at least 36 px tall at 768 px and below, as the wiki has said since 5.0.0; the rule
    // stood in the 480 px block, and it left out ANY / ALL and a row's Clear
    [Theory]
    [InlineData(390)]
    [InlineData(600)]
    public async Task The_filter_controls_are_touch_sized_on_a_phone(int width)
    {
        await OpenAtAsync(width, GenerateReport($"MobileTouchSized{width}.html"));
        // the bar's Filters opens the box at every width (plans/SEARCH_PLAN.md F3, F5)
        await OpenFilterRowsAsync();
        // a chip that is set shows its row's Clear
        await Page.Locator(".filtering-box .tag-toggle").First.ClickAsync();
        var heights = await Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('.filtering-box :is(.status-toggle, .percentile-btn, .happy-seg, .tag-toggle, .dependency-toggle, .tag-more, .tag-mode-toggle, .dep-mode-toggle, .tag-ways-toggle, .dep-ways-toggle, .group-clear)')]
                .filter(b => b.getBoundingClientRect().height > 0)
                .map(b => b.className.split(' ')[0] + ' ' + b.getBoundingClientRect().height.toFixed(1))
            """);
        Assert.Contains(heights, h => h.StartsWith("tag-mode-toggle ", StringComparison.Ordinal));
        // and each row's ways switch (5.10.0, C11)
        Assert.Contains(heights, h => h.StartsWith("tag-ways-toggle ", StringComparison.Ordinal));
        Assert.Contains(heights, h => h.StartsWith("group-clear ", StringComparison.Ordinal));
        var low = heights.Where(h => double.Parse(h.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture) < 35.5).ToArray();
        Assert.True(low.Length == 0, $"{width} px: under 36 px: " + string.Join(", ", low));
    }

    // P1: the report's display settings are a column of zones as wide as their sheet, its YAML and WRAP selects with them,
    // as a scenario's settings are; a desktop rule kept them at their content's width (M-I7: 80 px in a 313 px sheet)
    [Theory]
    [InlineData(375)]
    [InlineData(600)]
    public async Task The_report_sheets_zones_and_selects_are_as_wide_as_the_sheet(int width)
    {
        await OpenAtAsync(width, GenerateReport($"MobileReportSheet{width}.html"));
        await Page.Locator(".tools-gear").ClickAsync();
        var sheet = Page.Locator(".features-tools .toolbar-right");
        await Expect(sheet).ToBeVisibleAsync();
        var measured = await MeasureAsync(sheet, ":scope > *, .note-format-select, .note-width-select");
        Assert.Contains(measured.List, p => p.Name == "note-format-select");
        AllAsWide(measured, "the report's sheet", width);
    }

    // a page of features alone has the bar with its Features tab since plans/SEARCH_PLAN.md F7, where 5.9.1 measured the
    // toolbar row it had in its place: its settings are the report's sheet behind the gear, as wide as on any other page
    [Theory]
    [InlineData(375)]
    [InlineData(600)]
    public async Task A_page_of_features_alone_gives_its_settings_the_whole_sheet(int width)
    {
        await OpenAtAsync(width, Tabless($"MobileTablessSheet{width}.html"));
        Assert.Equal(["features"], await Page.Locator(".report-tabs > .report-tab:not([hidden])").EvaluateAllAsync<string[]>("ts => ts.map(t => t.dataset.tab)"));
        await Page.Locator(".tools-gear").ClickAsync();
        var sheet = Page.Locator(".features-tools .toolbar-right");
        await Expect(sheet).ToBeVisibleAsync();
        var measured = await MeasureAsync(sheet, ":scope > *, .note-format-select, .note-width-select");
        Assert.Contains(measured.List, p => p.Name == "note-format-select");
        AllAsWide(measured, "the page's settings", width);
    }

    // P1: the report's zones sit as close as a scenario's (the desktop row's 6 px row gap kept them apart)
    [Fact]
    public async Task The_report_sheet_spaces_its_zones_as_a_scenarios_sheet_does()
    {
        await OpenAtAsync(375, GenerateReport("MobileReportSheetGaps.html"));
        await Page.Locator(".tools-gear").ClickAsync();
        await Expect(Page.Locator(".features-tools .toolbar-right")).ToBeVisibleAsync();
        await ExpandFirstScenarioWithDiagram();
        var gear = Page.Locator(".gear-in-summary").First;
        await gear.ClickAsync();
        var report = await MeasureAsync(Page.Locator(".features-tools .toolbar-right"), ":scope > *");
        var scenario = await MeasureAsync(Page.Locator(".scenario-settings.is-open .diagram-options-bar").First, ":scope > :is(.details-radio, .diagram-format, .diagram-flags)");
        static double[] Gaps(Parts p) => p.List.Zip(p.List.Skip(1), (a, b) => Math.Round(b.Top - a.Bottom, 1)).ToArray();
        Assert.True(report.List.Length >= 2 && scenario.List.Length >= 2, "two zones or more in each sheet");
        Assert.Equal(Gaps(scenario).Distinct().Single(), Gaps(report).Distinct().Single(), 0.5);
    }

    // P2: on a phone the Timeline's control groups each take the line, the label above, the buttons sharing it equally
    // (M-I7: groups at their content's width, Order's labels wrapping)
    [Theory]
    [InlineData(375)]
    [InlineData(600)]
    public async Task The_timelines_control_groups_span_the_line_and_share_it_between_their_buttons(int width)
    {
        await OpenAtAsync(width, GenerateReport($"MobileTimelineControls{width}.html"));
        await Page.Locator("#tab-timeline").ClickAsync();
        await SpanTheLineAsync(Page.Locator("#panel-timeline .timeline-controls"), width);
    }

    // P2 (Q10): the Components tab uses the Timeline's classes, so it takes the same look
    [Theory]
    [InlineData(375)]
    [InlineData(600)]
    public async Task The_components_controls_do_the_same(int width)
    {
        await OpenAtAsync(width, ComponentsReport($"MobileComponentControls{width}.html"));
        await Page.Locator("#tab-components").ClickAsync();
        await SpanTheLineAsync(Page.Locator("#component-diagram .timeline-controls"), width);
    }

    private async Task SpanTheLineAsync(ILocator controls, int width)
    {
        await Expect(controls).ToBeVisibleAsync();
        var line = await controls.EvaluateAsync<double>("""
            c => { const p = c.parentElement, cs = getComputedStyle(p); return p.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight); }
            """);
        var groups = await MeasureAsync(controls, ":scope > .tl-seg");
        Assert.NotEmpty(groups.List);
        Assert.True(Math.Abs(groups.Inner - line) <= 1, $"{width} px: the controls take {groups.Inner:F0} of the {line:F0} px line");
        AllAsWide(groups, "the controls", width);
        var shares = await controls.EvaluateAsync<string>("""
            c => JSON.stringify([...c.querySelectorAll(':scope > .tl-seg')].map(g => {
                const label = g.querySelector('.tl-seg-label').getBoundingClientRect();
                const buttons = [...g.querySelectorAll('.tl-seg-btn')].map(b => b.getBoundingClientRect());
                return { group: g.getAttribute('aria-label'), labelBottom: label.bottom, firstTop: Math.min(...buttons.map(b => b.top)),
                    widths: buttons.map(b => b.width), lines: [...g.querySelectorAll('.tl-seg-btn')].map(b => {
                        const r = document.createRange(); r.selectNodeContents(b);
                        return new Set([...r.getClientRects()].filter(x => x.width > 0).map(x => Math.round(x.top))).size; }),
                    used: buttons.reduce((s, b) => s + b.width, 0), width: g.getBoundingClientRect().width };
            }))
            """);
        using var doc = System.Text.Json.JsonDocument.Parse(shares);
        foreach (var g in doc.RootElement.EnumerateArray())
        {
            var name = g.GetProperty("group").GetString();
            Assert.True(g.GetProperty("labelBottom").GetDouble() <= g.GetProperty("firstTop").GetDouble() + 0.5, $"{width} px: {name}'s label is not above its buttons");
            var widths = g.GetProperty("widths").EnumerateArray().Select(w => w.GetDouble()).ToArray();
            Assert.True(widths.Max() - widths.Min() <= 1, $"{width} px: {name}'s buttons are {string.Join("/", widths.Select(w => w.ToString("F0")))} px");
            Assert.True(g.GetProperty("used").GetDouble() >= g.GetProperty("width").GetDouble() - 24, $"{width} px: {name}'s buttons leave the line half empty");
            Assert.All(g.GetProperty("lines").EnumerateArray(), l => Assert.Equal(1, l.GetInt32()));
        }
    }

    // the Components tab with its controls: calls the report tracked, written by the whole pipeline as ComponentsTabTests
    // writes its run (one fixture at a time, from a fresh cache)
    private string ComponentsReport(string fileName)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (shop, payments, orders) = ("Shop" + tag, "Payments" + tag, "Orders" + tag);
        var reportsDir = Path.Combine(TempDir, "components-" + tag);
        Directory.CreateDirectory(reportsDir);
        var (pays, ships) = ("mr-pays-" + tag, "mr-ships-" + tag);
        var names = new HashSet<string> { shop, payments, orders };
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            Kronikol.Tracking.RequestResponseLogger.LogPair("Pays", pays, HttpMethod.Post, new Uri("http://payments/pay"), payments, shop, statusCode: System.Net.HttpStatusCode.OK);
            Kronikol.Tracking.RequestResponseLogger.LogPair("Ships", ships, HttpMethod.Post, new Uri("http://orders/orders"), orders, shop, statusCode: System.Net.HttpStatusCode.Created);
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature { DisplayName = "Shop", Scenarios = [
                    new Scenario { Id = pays, DisplayName = "Pays", Result = ExecutionResult.Passed },
                    new Scenario { Id = ships, DisplayName = "Ships", Result = ExecutionResult.Failed }] }],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = reportsDir,
                    ComponentDiagramOptions = new Kronikol.ComponentDiagram.ComponentDiagramOptions { ParticipantFilter = names.Contains },
                    GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false,
                });
            DefaultDiagramsFetcher.Reset();
        }
        var report = Path.Combine(reportsDir, "TestRunReport.html");
        File.Copy(report, Path.Combine(OutputDir, fileName), true);
        return new Uri(report).AbsoluteUri;
    }

    // no durations, no component diagram, no history: a page with nothing beside its features has no tabs; one diagram with
    // a payload, so the settings have their Format zone
    private string Tabless(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios = [new Scenario { Id = "t1", DisplayName = "Pay", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "it pays", Status = ExecutionResult.Passed }] }]
            }
        };
        var diagram = new DefaultDiagramsFetcher.DiagramAsCode("t1", "",
            "@startuml\nactor \"Caller\" as caller\nparticipant \"Pay Service\" as pay\ncaller -> pay : POST /pay\nnote left\n{\"amount\": 3}\nend note\npay --> caller : OK\n@enduml");
        var path = ReportGenerator.GenerateHtmlReport([diagram], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, fileName), "Tabless Report", true, diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
