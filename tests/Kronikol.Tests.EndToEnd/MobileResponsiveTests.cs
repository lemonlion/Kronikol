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
        await Page.GotoAsync(GenerateReport("MobileViewport.html"));
        var meta = Page.Locator("meta[name='viewport']");
        var content = await meta.GetAttributeAsync("content");
        Assert.Contains("width=device-width", content);
    }

    [Fact]
    public async Task Report_has_doctype_and_charset()
    {
        await Page.GotoAsync(GenerateReport("MobileDoctype.html"));
        var charset = Page.Locator("meta[charset]");
        Assert.Equal("utf-8", await charset.GetAttributeAsync("charset"));
    }

    // ── Header row stacks vertically ──

    [Fact]
    public async Task Header_row_stacks_vertically_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileHeaderStack.html"));
        var headerRow = Page.Locator(".header-row");
        await headerRow.WaitForAsync(new() { Timeout = 5000 });
        var flexDirection = await GetComputedStyle(headerRow, "flex-direction");
        Assert.Equal("column", flexDirection);
    }

    // ── Toolbar stacks vertically ──

    [Fact]
    public async Task Toolbar_stacks_vertically_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileToolbar.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var toolbar = Page.Locator(".toolbar-row");
        var flexDirection = await GetComputedStyle(toolbar, "flex-direction");
        Assert.Equal("column", flexDirection);
    }

    // ── No horizontal overflow ──

    [Fact]
    public async Task Page_does_not_overflow_horizontally()
    {
        await Page.GotoAsync(GenerateReport("MobileOverflow.html"));
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
        await Page.GotoAsync(GenerateReport("MobileFilters.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Expand the collapsed filter section first
        await Page.Locator(".mobile-filter-toggle").ClickAsync();

        var filterRow = Page.Locator(".filter-row");
        await filterRow.WaitForAsync(new() { Timeout = 5000 });
        var flexDirection = await GetComputedStyle(filterRow, "flex-direction");
        Assert.Equal("column", flexDirection);
    }

    // ── Tables scroll horizontally ──

    [Fact]
    public async Task Feature_summary_table_scrolls_horizontally()
    {
        await Page.GotoAsync(GenerateReport("MobileTable.html"));
        await Page.Locator(".features-summary-details").WaitForAsync(new() { Timeout = 5000 });

        await Page.Locator(".features-summary-details > summary").ClickAsync();

        var table = Page.Locator(".feature-summary-table");
        var display = await GetComputedStyle(table, "display");
        Assert.Equal("block", display);
    }

    // ── Filtering box takes full width ──

    [Fact]
    public async Task Filtering_box_takes_full_width_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileFilterBox.html"));
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
        await Page.GotoAsync(GenerateReport("MobileJump.html"));
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
        await Page.GotoAsync(GenerateReport("MobileDesktopCheck.html"));
        await Page.Locator(".header-row").WaitForAsync(new() { Timeout = 5000 });

        // At 375px, header-row should be column
        var headerRow = Page.Locator(".header-row");
        Assert.Equal("column", await GetComputedStyle(headerRow, "flex-direction"));

        // Resize viewport to desktop
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.ReloadAsync();
        await Page.Locator(".header-row").WaitForAsync(new() { Timeout = 5000 });
        headerRow = Page.Locator(".header-row");
        Assert.Equal("row", await GetComputedStyle(headerRow, "flex-direction"));

        // Restore mobile size
        await Page.SetViewportSizeAsync(375, 812);
    }

    // ── Summary chart centred on mobile ──

    [Fact]
    public async Task Summary_chart_is_centred_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileChartCenter.html"));
        var chart = Page.Locator(".summary-chart");
        await chart.WaitForAsync(new() { Timeout = 5000 });
        var alignSelf = await GetComputedStyle(chart, "align-self");
        Assert.Equal("center", alignSelf);
    }

    // ── Details/Headers wraps on mobile ──

    [Fact]
    public async Task Toolbar_right_details_does_not_overflow()
    {
        await Page.GotoAsync(GenerateReport("MobileDetailsOverflow.html"));
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
        await Page.GotoAsync(GenerateReport("MobileDiagramToggle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.EvaluateAsync("""
            () => {
                document.querySelectorAll('details.feature')[0]?.setAttribute('open','');
                document.querySelectorAll('details.scenario')[0]?.setAttribute('open','');
                document.querySelectorAll('details.example-diagrams')[0]?.setAttribute('open','');
            }
        """);

        // On a phone the init script hides every scenario toolbar behind a "Diagram Settings" button, so
        // the toolbar is measured after the tap that shows it: read off the hidden box, flex-wrap says
        // nothing about what a reader sees.
        await Page.Locator(".scenario-diagram-controls-toggle").First.ClickAsync();
        var toggle = Page.Locator(".diagram-toggle").First;
        await Expect(toggle).ToBeVisibleAsync();
        Assert.Equal("flex", await GetComputedStyle(toggle, "display"));
        Assert.Equal("wrap", await GetComputedStyle(toggle, "flex-wrap"));
        Assert.Equal("none", await GetComputedStyle(Page.Locator(".diagram-toggle-spacer").First, "display"));

        var pastEdge = await toggle.EvaluateAsync<double>("""
            t => Math.max(0, ...[...t.querySelectorAll('button, select')]
                .map(k => k.getBoundingClientRect().right - t.getBoundingClientRect().right))
            """);
        Assert.True(pastEdge <= 1, $"a toolbar control ends {pastEdge} px past the toolbar's edge");
    }

    [Fact]
    public async Task Headers_toggle_has_no_left_margin_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileHeadersMargin.html"));
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
        await Page.GotoAsync(GenerateReport("MobileDiagramBtns.html"));
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
        await Page.GotoAsync(GenerateReport("MobileZoomHidden.html"));
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var zoomControls = Page.Locator(".diagram-zoom-controls");
        if (await zoomControls.CountAsync() > 0)
        {
            var display = await GetComputedStyle(zoomControls.First, "display");
            Assert.Equal("none", display);
        }
    }

    // ── Context menu is bottom-sheet on mobile ──

    [Fact]
    public async Task Context_menu_renders_as_bottom_sheet_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileContextMenu.html"));
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

    // ── #2 Collapsible filter section ──

    [Fact]
    public async Task Filters_collapsed_by_default_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileFiltersCollapse1.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // The .mobile-filter-toggle should be visible
        var toggle = Page.Locator(".mobile-filter-toggle");
        await Expect(toggle).ToBeVisibleAsync();

        // The .filters div should be hidden
        var filtersVisible = await Page.EvaluateAsync<bool>(
            "() => { var f = document.querySelector('.filters'); return f && f.style.display !== 'none'; }");
        Assert.False(filtersVisible, "Filters should be collapsed by default on mobile");
    }

    [Fact]
    public async Task Filter_toggle_expands_and_collapses_filters()
    {
        await Page.GotoAsync(GenerateReport("MobileFiltersCollapse2.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var toggle = Page.Locator(".mobile-filter-toggle");

        // Click to expand
        await toggle.ClickAsync();
        var expanded = await Page.EvaluateAsync<bool>(
            "() => { var f = document.querySelector('.filters'); return f && f.style.display !== 'none'; }");
        Assert.True(expanded, "Filters should be visible after toggling open");

        // Click to collapse
        await toggle.ClickAsync();
        var collapsed = await Page.EvaluateAsync<bool>(
            "() => { var f = document.querySelector('.filters'); return f && f.style.display === 'none'; }");
        Assert.True(collapsed, "Filters should be hidden after toggling closed");
    }

    // ── #7 Sticky search bar ──

    [Fact]
    public async Task Search_bar_is_sticky_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileStickySearch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var position = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.filter-search')).position");
        Assert.Equal("sticky", position);
    }

    // ── #10 Back-to-top FAB ──

    [Fact]
    public async Task Back_to_top_hidden_initially()
    {
        await Page.GotoAsync(GenerateReport("MobileBackToTop1.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var display = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.getElementById('back-to-top')).display");
        Assert.Equal("none", display);
    }

    [Fact]
    public async Task Back_to_top_appears_after_scrolling()
    {
        await Page.GotoAsync(GenerateReport("MobileBackToTop2.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Expand features and inject a spacer to guarantee enough page height
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
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
        await Page.GotoAsync(GenerateReport("MobileBackToTop3.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
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
        await Page.GotoAsync(GenerateReport("MobileDiagramScrollable.html"));
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
        await Page.GotoAsync(GenerateReport("MobileDiagramFullscreen.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var exists = await Page.EvaluateAsync<bool>(
            "() => document.getElementById('diagram-fullscreen-overlay') !== null");
        Assert.False(exists, "Diagram fullscreen overlay should have been removed");
    }

    [Fact]
    public async Task Diagram_containers_do_not_have_zoom_in_cursor()
    {
        await Page.GotoAsync(GenerateReport("MobileDiagramNoCursor.html"));
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
        await Page.GotoAsync(GenerateReport("MobileDiagramCtrlToggle1.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        // Force diagram rendering
        await Page.EvaluateAsync(
            "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");

        // Wait for SVG
        await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        // The toggle button should be visible
        var toggleBtn = Page.Locator(".scenario-diagram-controls-toggle").First;
        await Expect(toggleBtn).ToBeVisibleAsync();

        // The controls should be hidden initially
        var controlsHidden = await Page.EvaluateAsync<bool>(
            "() => { var c = document.querySelector('.scenario-diagram-controls'); return c && c.style.display === 'none'; }");
        Assert.True(controlsHidden, "Diagram controls should be hidden by default on mobile");
    }

    [Fact]
    public async Task Diagram_controls_toggle_shows_and_hides_controls()
    {
        await Page.GotoAsync(GenerateReport("MobileDiagramCtrlToggle2.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        await Page.EvaluateAsync(
            "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var toggleBtn = Page.Locator(".scenario-diagram-controls-toggle").First;

        // Click to show controls
        await toggleBtn.ClickAsync();
        var shown = await Page.EvaluateAsync<bool>(
            "() => { var c = document.querySelector('.scenario-diagram-controls'); return c && c.style.display === 'flex'; }");
        Assert.True(shown, "Controls should be visible after clicking toggle");

        // Click to hide controls
        await toggleBtn.ClickAsync();
        var hidden = await Page.EvaluateAsync<bool>(
            "() => { var c = document.querySelector('.scenario-diagram-controls'); return c && c.style.display === 'none'; }");
        Assert.True(hidden, "Controls should be hidden after clicking toggle again");
    }

    // ── #6 Touch target sizes ──

    [Fact]
    public async Task Touch_targets_meet_minimum_height()
    {
        await Page.GotoAsync(GenerateReport("MobileTouchTargets.html"));
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
        await Page.GotoAsync(GenerateReport("MobileDepTruncate.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Expand filters first
        var toggle = Page.Locator(".mobile-filter-toggle");
        await toggle.ClickAsync();

        var overflow = await Page.EvaluateAsync<string>(
            "() => { var b = document.querySelector('.dependency-toggle'); return b ? getComputedStyle(b).overflow : ''; }");
        Assert.Equal("hidden", overflow);
    }

    // ── #9 Step duration white-space ──

    [Fact]
    public async Task Step_duration_does_not_wrap()
    {
        await Page.GotoAsync(GenerateReport("MobileStepDuration.html"));
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
        await Page.GotoAsync(GenerateReport("MobileSettingsBtn.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        await Page.EvaluateAsync(
            "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var toggleBtn = Page.Locator(".scenario-diagram-controls-toggle").First;
        await Expect(toggleBtn).ToBeVisibleAsync();

        var display = await GetComputedStyle(toggleBtn, "display");
        Assert.Equal("block", display);
    }

    // ── CI box full width on mobile ──

    [Fact]
    public async Task Ci_chart_group_full_width_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileCiFullWidth.html"));
        var headerRow = Page.Locator(".header-row");
        await headerRow.WaitForAsync(new() { Timeout = 5000 });

        var ciGroup = Page.Locator(".ci-chart-group");
        if (await ciGroup.CountAsync() > 0)
        {
            var headerWidth = await headerRow.EvaluateAsync<double>("e => e.offsetWidth");
            var groupWidth = await ciGroup.EvaluateAsync<double>("e => e.offsetWidth");
            Assert.True(groupWidth >= headerWidth - 2,
                $"CI chart group width ({groupWidth}) should match header row ({headerWidth})");
        }
    }
}
