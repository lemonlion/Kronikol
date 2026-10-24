using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Zoom)]
public class GradualZoomTests : PlaywrightTestBase
{
    public GradualZoomTests(PlaywrightFixture fixture) : base(fixture) { }

    private ILocator GetDiagramContainer() =>
        Page.Locator("[data-diagram-type='plantuml']").First;

    private async Task SetupWideDiagram(string fileName)
    {
        await Page.GotoReportAsync(GenerateReportWithWideDiagram(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        // Wait for zoom controls to be added
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('[data-diagram-type=plantuml]').querySelector(':scope > .diagram-zoom-controls')",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    // ── Diagram Selection ──

    [Fact]
    public async Task Clicking_diagram_selects_it()
    {
        await SetupWideDiagram("SelectOnClick.html");
        var container = GetDiagramContainer();

        await container.EvaluateAsync("el => el.click()");

        await Expect(container).ToHaveClassAsync(new Regex("diagram-selected"));
    }

    [Fact]
    public async Task Selected_diagram_has_blue_glow()
    {
        await SetupWideDiagram("SelectGlow.html");
        var container = GetDiagramContainer();

        await container.EvaluateAsync("el => el.click()");

        var glow = await container.EvaluateAsync<string[]>(
            "e => { const p = document.createElement('div'); p.style.color = 'color-mix(in srgb, var(--kron-accent) 50%, transparent)'; "
            + "document.body.appendChild(p); const ring = getComputedStyle(p).color; p.remove(); return [getComputedStyle(e).boxShadow, ring]; }");
        // the theme's accent at half strength, blue in the default theme (5.0.0, plans/OPTION_C_PLAN.md S13b: 4.x wrote
        // rgba(59, 130, 246, 0.5) in every theme)
        Assert.StartsWith(glow[1], glow[0]);
    }

    [Fact]
    public async Task Clicking_outside_diagram_deselects_it()
    {
        await SetupWideDiagram("DeselectClickOutside.html");
        var container = GetDiagramContainer();

        await container.EvaluateAsync("el => el.click()");
        await Expect(container).ToHaveClassAsync(new Regex("diagram-selected"));

        // Click body away from any diagram
        await Page.EvaluateAsync("() => document.querySelector('h1, h2, body').click()");

        await Expect(container).Not.ToHaveClassAsync(new Regex("diagram-selected"));
    }

    [Fact]
    public async Task Escape_deselects_diagram()
    {
        await SetupWideDiagram("DeselectEscape.html");
        var container = GetDiagramContainer();

        await container.EvaluateAsync("el => el.click()");
        await Expect(container).ToHaveClassAsync(new Regex("diagram-selected"));

        await Page.Keyboard.PressAsync("Escape");

        await Expect(container).Not.ToHaveClassAsync(new Regex("diagram-selected"));
    }

    [Fact]
    public async Task Only_one_diagram_can_be_selected_at_a_time()
    {
        await SetupWideDiagram("SingleSelection.html");
        // This report has at least one diagram. Click it, then verify selection.
        var container = GetDiagramContainer();
        await container.EvaluateAsync("el => el.click()");
        await Expect(container).ToHaveClassAsync(new Regex("diagram-selected"));

        // Click outside to deselect, then ensure no diagram has class
        await Page.Keyboard.PressAsync("Escape");
        var selectedCount = await Page.Locator(".diagram-selected").CountAsync();
        Assert.Equal(0, selectedCount);
    }

    // ── Zoom Slider ──

    [Fact]
    public async Task Zoom_slider_appears_on_zoomable_diagram()
    {
        await SetupWideDiagram("SliderExists.html");

        var sliderCount = await Page.Locator(".diagram-zoom-slider").CountAsync();
        Assert.True(sliderCount > 0, "Zoom slider should exist on zoomable diagram");
    }

    // Until 5.7.0 a diagram that fit its box got no slider (plans/ZOOM_EVERY_DIAGRAM_PLAN.md R1).
    [Fact]
    public async Task Zoom_slider_on_a_diagram_that_fits_starts_at_100()
    {
        await Page.GotoReportAsync(GenerateReport("SliderNonZoomable.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await ZoomFacts.WaitForSliderAsync(Page, 0);

        var d = await ZoomFacts.ReadAsync(Page, 0);
        Assert.Equal(100, d.Slider);
        Assert.Equal(50, d.Min);
        Assert.Equal(100, d.Max);
    }

    // A box that is not laid out (a hidden panel, a scenario closed in Firefox, one closed while its diagram was drawn)
    // measures nothing, so it gets no slider and its natural width reads 0 until it shows, as when the width was measured.
    // 5.5.1 read the width from the engine's attribute, so such a box was zoomable with a fit level of 1%, and kept that
    // slider once shown, as a box with a slider is not asked again (5.5.3).
    [Fact]
    public async Task A_box_not_laid_out_gets_no_zoom_slider_until_it_shows()
    {
        await SetupWideDiagram("SliderNotLaidOut.html");

        var result = await GetDiagramContainer().EvaluateAsync<string>("""
            c => {
                c.querySelectorAll('.diagram-zoom-controls').forEach(function (z) { z.remove(); });
                var wrap = c.parentElement;
                wrap.style.display = 'none';
                window._addZoomButton(c);
                var hidden = c.querySelectorAll('.diagram-zoom-controls').length + '|' + window._getDiagramNaturalWidth(c);
                wrap.style.display = '';
                window._addZoomButton(c);
                var s = c.querySelector('.diagram-zoom-slider');
                var svg = c.querySelector('svg');
                // the fit is measured against the box's content, its padding left out (5.7.0, B1)
                var cs = getComputedStyle(c), content = c.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
                var fit = Math.max(1, Math.min(100, Math.round(content / svg.width.baseVal.value * 100)));
                return hidden + '|' + (s ? s.min : 'none') + '|' + fit;
            }
            """);

        var parts = result.Split('|');
        Assert.True(parts[0] == "0" && parts[1] == "0", $"A box not laid out should get no slider and measure 0 (slider count|natural width|min|fit): {result}");
        Assert.True(parts[2] == parts[3], $"Shown again, its slider should start at its fit level (slider count|natural width|min|fit): {result}");
    }

    [Fact]
    public async Task Zoom_slider_is_hidden_until_hover()
    {
        await SetupWideDiagram("SliderHidden.html");

        var opacity = await GetDiagramContainer().Locator(":scope > .diagram-zoom-controls")
            .EvaluateAsync<string>("e => window.getComputedStyle(e).opacity");
        Assert.Equal("0", opacity);
    }

    [Fact]
    public async Task Zoom_slider_becomes_visible_on_container_hover()
    {
        await SetupWideDiagram("SliderHoverVisible.html");

        var container = GetDiagramContainer();
        await container.HoverAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var ctrl = document.querySelector('[data-diagram-type=plantuml]').querySelector(':scope > .diagram-zoom-controls');
                return ctrl && window.getComputedStyle(ctrl).opacity !== '0';
            }
        """, null, new() { Timeout = 3000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Zoom_slider_has_correct_min_max()
    {
        await SetupWideDiagram("SliderMinMax.html");

        var max = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<string>("e => e.max");
        Assert.Equal("100", max);

        var min = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.min)");
        Assert.True(min > 0 && min < 100, $"Min should be between 0-100 (fit-to-width ratio) but was {min}");
    }

    [Fact]
    public async Task Moving_slider_changes_svg_width()
    {
        await SetupWideDiagram("SliderChangesWidth.html");

        var container = GetDiagramContainer();

        // Get initial SVG width
        var initialWidth = await container.EvaluateAsync<double>("""
            c => {
                var svg = c.querySelector('svg');
                return svg.getBoundingClientRect().width;
            }
        """);

        // Set slider to 100 (max zoom) via JS
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type=plantuml]').querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        var newWidth = await container.EvaluateAsync<double>("""
            c => {
                var svg = c.querySelector('svg');
                return svg.getBoundingClientRect().width;
            }
        """);

        Assert.True(newWidth > initialWidth, $"SVG width should increase from {initialWidth} to larger but got {newWidth}");
    }

    [Fact]
    public async Task Slider_syncs_when_zoomed_via_keyboard()
    {
        await SetupWideDiagram("SliderSyncKeyboard.html");

        var container = GetDiagramContainer();
        await container.EvaluateAsync("el => el.click()");
        await Expect(container).ToHaveClassAsync(new Regex("diagram-selected"));

        var initialVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        // Press Ctrl+= to zoom in
        await Page.Keyboard.PressAsync("Control+=");

        var newVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");
        Assert.True(newVal > initialVal, $"Slider value should increase from {initialVal} but got {newVal}");
    }

    [Fact]
    public async Task Slider_syncs_when_ctrl_wheel_zooms()
    {
        await SetupWideDiagram("SliderSyncWheel.html");

        var container = GetDiagramContainer();

        var initialVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        // Ctrl+wheel up to zoom in
        await container.EvaluateAsync("""
            (c) => {
                var rect = c.getBoundingClientRect();
                c.dispatchEvent(new WheelEvent('wheel', {
                    bubbles: true, cancelable: true,
                    ctrlKey: true,
                    deltaY: -100,
                    clientX: rect.left + rect.width / 2,
                    clientY: rect.top + rect.height / 2
                }));
            }
        """);

        var sliderVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");
        Assert.True(sliderVal > initialVal, $"Slider should sync after Ctrl+wheel zoom: {initialVal} -> {sliderVal}");
    }

    [Fact]
    public async Task Container_gets_overflow_x_auto_when_slider_above_fit()
    {
        await SetupWideDiagram("SliderOverflow.html");

        // Set slider to max
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type=plantuml]').querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        var container = GetDiagramContainer();
        var overflowX = await container.EvaluateAsync<string>("e => e.style.overflowX");
        Assert.Equal("auto", overflowX);
    }

    // ── Keyboard Zoom ──

    [Fact]
    public async Task Ctrl_plus_zooms_selected_diagram()
    {
        await SetupWideDiagram("CtrlPlusZoom.html");

        var container = GetDiagramContainer();
        await container.EvaluateAsync("el => el.click()");
        await Expect(container).ToHaveClassAsync(new Regex("diagram-selected"));

        var initialVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        // Press Ctrl+=  (which is Ctrl+Plus on most keyboards)
        await Page.Keyboard.PressAsync("Control+=");

        var newVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        Assert.True(newVal > initialVal, $"Slider value should increase from {initialVal} but got {newVal}");
    }

    [Fact]
    public async Task Ctrl_minus_zooms_out_selected_diagram()
    {
        await SetupWideDiagram("CtrlMinusZoom.html");

        var container = GetDiagramContainer();
        await container.EvaluateAsync("el => el.click()");

        // First zoom in fully
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type=plantuml]').querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        var maxVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        // Press Ctrl+-
        await Page.Keyboard.PressAsync("Control+-");

        var newVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        Assert.True(newVal < maxVal, $"Slider value should decrease from {maxVal} but got {newVal}");
    }

    [Fact]
    public async Task Ctrl_plus_does_nothing_without_selection()
    {
        await SetupWideDiagram("CtrlPlusNoSelect.html");

        var initialVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        await Page.Keyboard.PressAsync("Control+=");

        var newVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        Assert.Equal(initialVal, newVal);
    }

    // ── Mouse Wheel Zoom ──

    [Fact]
    public async Task Ctrl_wheel_zooms_diagram_under_cursor()
    {
        await SetupWideDiagram("CtrlWheelZoom.html");

        var container = GetDiagramContainer();
        var initialVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        // Dispatch a Ctrl+wheel event over the diagram container
        await container.EvaluateAsync("""
            (c) => {
                var rect = c.getBoundingClientRect();
                c.dispatchEvent(new WheelEvent('wheel', {
                    bubbles: true, cancelable: true,
                    ctrlKey: true,
                    deltaY: -100,
                    clientX: rect.left + rect.width / 2,
                    clientY: rect.top + rect.height / 2
                }));
            }
        """);

        var newVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        Assert.True(newVal > initialVal, $"Ctrl+wheel up should zoom in: {initialVal} -> {newVal}");
    }

    [Fact]
    public async Task Ctrl_wheel_down_zooms_out()
    {
        await SetupWideDiagram("CtrlWheelOut.html");

        var container = GetDiagramContainer();

        // First zoom in
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type=plantuml]').querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        // Dispatch Ctrl+wheel down
        await container.EvaluateAsync("""
            (c) => {
                var rect = c.getBoundingClientRect();
                c.dispatchEvent(new WheelEvent('wheel', {
                    bubbles: true, cancelable: true,
                    ctrlKey: true,
                    deltaY: 100,
                    clientX: rect.left + rect.width / 2,
                    clientY: rect.top + rect.height / 2
                }));
            }
        """);

        var newVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        Assert.True(newVal < 100, $"Ctrl+wheel down should zoom out from 100 but got {newVal}");
    }

    [Fact]
    public async Task Plain_wheel_does_not_zoom_even_when_selected()
    {
        await SetupWideDiagram("PlainWheelZoom.html");

        var container = GetDiagramContainer();

        // Select the diagram first
        await container.EvaluateAsync("el => el.click()");
        await Expect(container).ToHaveClassAsync(new Regex("diagram-selected"));

        var initialVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        // Dispatch a plain wheel event (no ctrlKey) over the selected diagram
        await container.EvaluateAsync("""
            (c) => {
                var rect = c.getBoundingClientRect();
                c.dispatchEvent(new WheelEvent('wheel', {
                    bubbles: true, cancelable: true,
                    ctrlKey: false,
                    deltaY: -100,
                    clientX: rect.left + rect.width / 2,
                    clientY: rect.top + rect.height / 2
                }));
            }
        """);

        var newVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        Assert.Equal(initialVal, newVal);
    }

    [Fact]
    public async Task Plain_wheel_does_not_zoom_unselected_diagram()
    {
        await SetupWideDiagram("PlainWheelNoZoom.html");

        var container = GetDiagramContainer();
        // Do NOT select the diagram

        var initialVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        // Dispatch a plain wheel event (no ctrlKey) over the unselected diagram
        await container.EvaluateAsync("""
            (c) => {
                var rect = c.getBoundingClientRect();
                c.dispatchEvent(new WheelEvent('wheel', {
                    bubbles: true, cancelable: true,
                    ctrlKey: false,
                    deltaY: -100,
                    clientX: rect.left + rect.width / 2,
                    clientY: rect.top + rect.height / 2
                }));
            }
        """);

        var newVal = await ZoomSliderOf(GetDiagramContainer())
            .EvaluateAsync<int>("e => parseInt(e.value)");

        Assert.Equal(initialVal, newVal);
    }

    // ── Zoom-to-Cursor (scroll adjustment) ──

    [Fact]
    public async Task Zoom_in_keeps_artifact_under_cursor_in_place()
    {
        await SetupWideDiagram("ZoomToCursor.html");

        var container = GetDiagramContainer();

        // First partially zoom so there's a scrollable area
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type=plantuml]').querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                var mid = Math.round((parseInt(slider.min) + 100) / 2);
                slider.value = String(mid);
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        // Record scroll position and SVG width before further zoom
        var before = await container.EvaluateAsync<double[]>("""
            (c) => {
                var svg = c.querySelector('svg');
                return [c.scrollLeft, svg.getBoundingClientRect().width, c.getBoundingClientRect().left, c.getBoundingClientRect().width];
            }
        """);
        var scrollLeftBefore = before[0];
        var svgWidthBefore = before[1];
        var containerLeft = before[2];
        var containerWidth = before[3];

        // Cursor position: center of the visible container area
        var cursorX = containerLeft + containerWidth / 2;

        // The SVG-space X under cursor before zoom
        var svgXBefore = (cursorX - containerLeft + scrollLeftBefore) / svgWidthBefore;

        // Zoom in via Ctrl+wheel at cursor position
        await container.EvaluateAsync($$"""
            (c) => {
                var rect = c.getBoundingClientRect();
                c.dispatchEvent(new WheelEvent('wheel', {
                    bubbles: true, cancelable: true,
                    ctrlKey: true,
                    deltaY: -300,
                    clientX: {{cursorX}},
                    clientY: rect.top + rect.height / 2
                }));
            }
        """);

        // Record after
        var after = await container.EvaluateAsync<double[]>("""
            (c) => {
                var svg = c.querySelector('svg');
                return [c.scrollLeft, svg.getBoundingClientRect().width];
            }
        """);
        var scrollLeftAfter = after[0];
        var svgWidthAfter = after[1];

        // The SVG-space X under cursor after zoom
        var svgXAfter = (cursorX - containerLeft + scrollLeftAfter) / svgWidthAfter;

        // Positions should be approximately the same (within 2% tolerance)
        Assert.True(Math.Abs(svgXBefore - svgXAfter) < 0.02,
            $"SVG point under cursor shifted: before={svgXBefore:F4}, after={svgXAfter:F4}");
    }

    [Fact]
    public async Task Clicking_selected_diagram_deselects_it()
    {
        await SetupWideDiagram("ClickToDeselect.html");

        var container = GetDiagramContainer();

        // Click to select
        await container.EvaluateAsync("el => el.click()");
        await Expect(container).ToHaveClassAsync(new Regex("diagram-selected"));

        // Click again to deselect
        await container.EvaluateAsync("el => el.click()");
        await Expect(container).Not.ToHaveClassAsync(new Regex("diagram-selected"));
    }

    // ── What the zoom measures against, and what it keeps in place (plans/ZOOM_EVERY_DIAGRAM_PLAN.md section 5) ──

    /// <summary>Opens the wide fixture with the window <paramref name="width"/> px wide and waits for its diagram.</summary>
    private async Task OpenWideAtAsync(string fileName, int width)
    {
        await Page.SetViewportSizeAsync(width, ViewportHeight);
        await Page.GotoReportAsync(GenerateReportWithWideDiagram(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await ZoomFacts.WaitForDrawnAsync(Page, 0);
        await Page.WaitForTimeoutAsync(500);
    }

    // B1: the fit was measured against the box with its padding, and a picture drawn no wider than the box's content was
    // left a little scaled down with no slider. The window is sized so that the box's content is 10 px narrower than the
    // picture.
    [Fact]
    public async Task A_picture_wider_than_its_box_s_content_has_a_slider_starting_at_the_level_it_is_drawn_at()
    {
        await OpenWideAtAsync("ContentNarrowerProbe.html", ViewportWidth);
        var m = await Page.EvaluateAsync<double[]>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']"), cs = getComputedStyle(c);
                return [c.querySelector(':scope > svg').width.baseVal.value, c.clientWidth, parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight)];
            }
            """);
        var width = (int)Math.Round(ViewportWidth + (m[0] - 10 + m[2] - m[1]));

        await OpenWideAtAsync("ContentNarrower.html", width);

        var d = await ZoomFacts.ReadAsync(Page, 0);
        Assert.True(d.Levels[0] < 100, $"the picture is drawn scaled down: {d.Levels[0]:F2}%");
        Assert.True(d.Slider >= 0, "a picture drawn scaled down has a slider");
        Assert.True(Math.Abs(d.Slider - d.Levels[0]) <= 0.5, $"the slider reads {d.Slider} for a picture drawn at {d.Levels[0]:F2}%");
    }

    // B1: a wider picture's slider started above the level it was drawn at, so its first step jumped
    [Fact]
    public async Task The_slider_s_lowest_level_is_the_level_the_picture_is_drawn_at()
    {
        await SetupWideDiagram("SliderLowestIsDrawn.html");
        await ZoomFacts.WaitForSliderAsync(Page, 0);

        var d = await ZoomFacts.ReadAsync(Page, 0);
        Assert.Equal(d.Min, d.Slider);
        Assert.True(Math.Abs(d.Min - d.Levels[0]) <= 0.5, $"the slider's lowest level is {d.Min}, the picture is drawn at {d.Levels[0]:F2}%");
    }

    /// <summary>A whole diagram wider than the page and about 6,000 px tall: a hundred calls, one with a long label.</summary>
    private async Task OpenTallWideAsync(string fileName)
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, fileName,
            ReportTestHelper.PartsSource(100, new Dictionary<int, int> { [3] = 260 }), partHeight: 100000));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await ZoomFacts.WaitForSliderAsync(Page, 0);
    }

    // B2: a zoom kept the point under the pointer in place across and moved it down the page, as the picture's height
    // changes with its width
    [Fact]
    public async Task A_ctrl_wheel_step_keeps_the_point_under_the_pointer_in_place_down_the_page()
    {
        await OpenTallWideAsync("PointKeptDown.html");
        var at = await Page.EvaluateAsync<float[]>("""
            () => {
                const svg = document.querySelector("[data-diagram-type='plantuml'] svg");
                const r0 = svg.getBoundingClientRect();
                window.scrollBy(0, r0.top + r0.height / 2 - innerHeight / 2);
                const r = svg.getBoundingClientRect();
                return [r.left + Math.min(r.width, innerWidth - r.left) / 2, innerHeight / 2];
            }
            """);
        await TwoAnimationFramesAsync(Page);

        foreach (var notches in new[] { 1, -1 })
        {
            await ZoomFacts.MarkPointAsync(Page, at[0], at[1]);
            await ZoomFacts.CtrlWheelAsync(Page, at[0], at[1], notches);
            var (across, down) = await ZoomFacts.PointMovedAsync(Page);
            Assert.True(Math.Abs(across) <= 2, $"a notch {(notches > 0 ? "in" : "out")} keeps the point across: {across:F1} px");
            Assert.True(Math.Abs(down) <= 2, $"a notch {(notches > 0 ? "in" : "out")} keeps the point down the page: {down:F1} px");
        }
    }

    // B4: the controls are sticky, but the box's horizontal scroll made the box their scroll box, which never scrolls
    // down: in the middle of a tall diagram its slider stayed at the diagram's top
    [Fact]
    public async Task The_slider_of_a_tall_diagram_stays_in_the_window_while_the_diagram_does()
    {
        await OpenTallWideAsync("SliderFollows.html");
        var at = await Page.EvaluateAsync<float[]>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                window.scrollBy(0, c.getBoundingClientRect().top + 1500);
                const r = c.getBoundingClientRect();
                return [r.left + 200, innerHeight / 2];
            }
            """);
        await TwoAnimationFramesAsync(Page);

        const string inWindow = """
            () => {
                const s = document.querySelector("[data-diagram-type='plantuml'] > .diagram-zoom-controls .diagram-zoom-slider");
                const r = s.getBoundingClientRect();
                const bar = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h')) || 0;
                return r.top >= bar && r.bottom <= innerHeight;
            }
            """;
        await Page.Mouse.MoveAsync(at[0], at[1]);
        await Page.Mouse.MoveAsync(at[0] + 4, at[1] + 4);
        await TwoAnimationFramesAsync(Page);
        Assert.True(await Page.EvaluateAsync<bool>(inWindow), "hovered, the slider is in the window, below the report's bar");

        await Page.Mouse.ClickAsync(at[0] + 4, at[1] + 4);
        await Page.EvaluateAsync("() => window.scrollBy(0, 700)");
        await TwoAnimationFramesAsync(Page);
        await TwoAnimationFramesAsync(Page);
        Assert.True(await Page.EvaluateAsync<bool>(inWindow), "selected and scrolled on, the slider is still in the window");
    }
}
