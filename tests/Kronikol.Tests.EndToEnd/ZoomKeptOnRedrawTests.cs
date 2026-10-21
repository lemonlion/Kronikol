using System.Text.Json;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A zoomed diagram drawn again keeps its zoom: the level it was zoomed to, and its place across the picture. A note's
/// buttons and the report's controls draw a diagram again with a new picture and a new slider, and until 5.2.3 the page
/// kept only whether the diagram was zoomed: a diagram zoomed partway came back at full size and scrolled to its left
/// edge, and one that the expanded note made tall enough to be drawn in parts came back at its fit.
/// </summary>
[Collection(PlaywrightCollections.Zoom)]
public class ZoomKeptOnRedrawTests : PlaywrightTestBase
{
    public ZoomKeptOnRedrawTests(PlaywrightFixture fixture) : base(fixture) { }

    private const int ScrolledTo = 150;

    private Task OpenAsync(string fileName, int noteLines, bool headerLine = false) =>
        OpenUrlAsync(ReportTestHelper.GenerateReportWithWideLongNoteDiagram(TempDir, OutputDir, fileName, noteLines, headerLine));

    private async Task OpenUrlAsync(string url)
    {
        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && c.querySelector(':scope > svg') && c.querySelector(':scope > .diagram-zoom-controls') && c.querySelector('.note-hover-rect');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    /// <summary>Zooms the diagram halfway between its fit and full size and scrolls it sideways; returns the level. It
    /// waits for the next frames, which carry the scroll's event, as a reader's scroll comes long before anything else.</summary>
    private async Task<int> ZoomPartwayAsync()
    {
        var level = await Page.EvaluateAsync<int>($$"""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                s.value = String(Math.round((parseInt(s.min) + 100) / 2));
                s.dispatchEvent(new Event('input', { bubbles: true }));
                c.scrollLeft = {{ScrolledTo}};
                return parseInt(s.value);
            }
            """);
        await TwoAnimationFramesAsync(Page);
        return level;
    }

    private sealed record BoxState(string Box, int Slider, double Level, int ScrollLeft, bool Zoomed);

    /// <summary>Each box that zooms (the diagram's parts, or the diagram): its slider, the level its picture is drawn at
    /// (its width against its natural width, in percent), its scroll and whether it is marked zoomed.</summary>
    private async Task<BoxState[]> BoxesAsync()
    {
        var json = await Page.EvaluateAsync<string>("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return JSON.stringify((parts.length ? parts : [c]).map(b => {
                    var svg = b.querySelector(':scope > svg'), s = b.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                    return { Box: b === c ? 'diagram' : 'part ' + b.dataset.fragment, Slider: s ? parseInt(s.value) : -1,
                        Level: svg.getBoundingClientRect().width / window._getDiagramNaturalWidth(b) * 100,
                        ScrollLeft: Math.round(b.scrollLeft), Zoomed: b.classList.contains('diagram-natural-size') };
                }));
            }
            """);
        return JsonSerializer.Deserialize<BoxState[]>(json)!;
    }

    /// <summary>Runs <paramref name="redraw"/> and waits for the diagram drawn again: its old picture gone, each box drawn
    /// with its slider.</summary>
    private async Task RedrawAsync(string redraw)
    {
        await Page.EvaluateAsync("() => { window.__before = document.querySelector('[data-diagram-type=\"plantuml\"] svg'); }");
        await Page.EvaluateAsync(redraw);
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (document.contains(window.__before) || c._noteRendering) return false;
                var parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return (parts.length ? parts : [c]).every(b => b.querySelector(':scope > svg') && b.querySelector(':scope > .diagram-zoom-controls'));
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    private const string ExpandTheNote = """
        async () => {
            var c = document.querySelector('[data-diagram-type="plantuml"]');
            c.querySelector('.note-hover-rect').dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
            for (var i = 0; i < 50 && !c.querySelector("[data-note-btn='expand-bottom'] rect"); i++) await new Promise(r => setTimeout(r, 100));
            c.querySelector("[data-note-btn='expand-bottom'] rect").dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
        }
        """;

    private static void AssertKept(BoxState box, int level)
    {
        Assert.True(box.Zoomed, $"{box.Box} is still zoomed");
        Assert.Equal(level, box.Slider);
        Assert.True(Math.Abs(box.Level - level) <= 1, $"{box.Box} is drawn at {box.Level:F1}% of its natural width, zoomed to {level}%");
        Assert.Equal(ScrolledTo, box.ScrollLeft);
    }

    [Fact]
    public async Task Expanding_a_note_in_a_diagram_zoomed_partway_keeps_its_level_and_its_place()
    {
        await OpenAsync("ZoomKeptExpand.html", noteLines: 30);
        var level = await ZoomPartwayAsync();
        Assert.InRange(level, 60, 99);

        await RedrawAsync(ExpandTheNote);

        var boxes = await BoxesAsync();
        Assert.Single(boxes);
        AssertKept(boxes[0], level);
    }

    // Expanded, the note makes the diagram tall enough to be drawn in parts: each part takes the diagram's zoom.
    [Fact]
    public async Task Expanding_a_note_that_has_the_diagram_drawn_in_parts_gives_each_part_the_diagrams_zoom()
    {
        await OpenAsync("ZoomKeptExpandParts.html", noteLines: 900);
        var level = await ZoomPartwayAsync();

        await RedrawAsync(ExpandTheNote);

        var boxes = await BoxesAsync();
        Assert.True(boxes.Length >= 2, $"the expanded note has the diagram drawn in parts ({boxes.Length})");
        Assert.All(boxes, b => AssertKept(b, level));
        Assert.False(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"]').classList.contains('diagram-natural-size')"),
            "the zoom moved to the parts: the diagram itself is not marked zoomed");
    }

    // A report control draws again the diagrams of closed scenarios too, and a box that is not laid out measures nothing:
    // its zoom waits for the scenario to open.
    [Fact]
    public async Task A_diagram_zoomed_partway_and_drawn_again_while_its_scenario_is_closed_keeps_its_level_and_its_place()
    {
        await OpenAsync("ZoomKeptClosed.html", noteLines: 30, headerLine: true);
        var level = await ZoomPartwayAsync();
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').open = false; }");

        await Page.EvaluateAsync("() => { window.__before = document.querySelector('[data-diagram-type=\"plantuml\"] svg'); }");
        await Page.EvaluateAsync("() => document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\").click()");
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return !document.contains(window.__before) && !c._noteRendering && c.querySelector(':scope > svg');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').open = true; }");
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('[data-diagram-type=\"plantuml\"] > .diagram-zoom-controls')",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        var boxes = await BoxesAsync();
        Assert.Single(boxes);
        AssertKept(boxes[0], level);
    }

    // Chromium lays out a closed scenario's content when a script asks for a size; Firefox and WebKit, like a hidden panel,
    // lay out nothing, and a box that is not laid out measures 0: a zoom restored there would be measured from nothing
    // and lost. The scenario not displayed at all stands for them.
    [Fact]
    public async Task A_diagram_zoomed_partway_and_drawn_again_while_not_displayed_keeps_its_level_and_its_place()
    {
        await OpenAsync("ZoomKeptNotDisplayed.html", noteLines: 30, headerLine: true);
        var level = await ZoomPartwayAsync();
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = 'none'; }");

        await Page.EvaluateAsync("() => { window.__before = document.querySelector('[data-diagram-type=\"plantuml\"] svg'); }");
        await Page.EvaluateAsync("() => document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\").click()");
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return !document.contains(window.__before) && !c._noteRendering && c.querySelector(':scope > svg');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = ''; }");
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('[data-diagram-type=\"plantuml\"] > .diagram-zoom-controls')",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        var boxes = await BoxesAsync();
        Assert.Single(boxes);
        AssertKept(boxes[0], level);
    }

    /// <summary>A point inside the visible half of box <paramref name="index"/> (a part, or the diagram), scrolled to.</summary>
    private async Task<(float X, float Y)> PointInBoxAsync(int index)
    {
        var p = await Page.EvaluateAsync<float[]>("""
            (i) => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                var box = (parts.length ? parts : [c])[i];
                box.scrollIntoView({ block: 'center' });
                var r = box.getBoundingClientRect(), top = Math.max(r.top, 160), bottom = Math.min(r.bottom, innerHeight - 20);
                return [r.left + r.width / 2, (top + bottom) / 2];
            }
            """, index);
        await TwoAnimationFramesAsync(Page);
        return (p[0], p[1]);
    }

    /// <summary>Waits for the diagram drawn again with the old picture gone and every box drawn, sliders or not.</summary>
    private async Task WaitForDrawnAgainAsync()
    {
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (document.contains(window.__before) || c._noteRendering) return false;
                var parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return (parts.length ? parts : [c]).every(b => b.querySelector(':scope > svg'));
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        await TwoAnimationFramesAsync(Page);
    }

    // Three participants and a note too wide for the page: zoomed, then drawn again with the note folded to its preview,
    // the diagram fits its box and takes no zoom (5.2.5: 5.2.3 gave the new picture the zoom it could no longer have, and
    // a drag over it panned it, so its text could not be selected). A part drawn again the same way goes the same road.
    [Fact]
    public async Task A_diagram_drawn_again_to_fit_its_box_takes_no_zoom()
    {
        await OpenUrlAsync(ReportTestHelper.GenerateReportWithNarrowDiagramAndWideNote(TempDir, OutputDir, "ZoomKeptFits.html", noteLines: 10, callPairs: 20));
        await ZoomPartwayAsync();

        await Page.EvaluateAsync("() => { window.__before = document.querySelector('[data-diagram-type=\"plantuml\"] svg'); }");
        await Page.EvaluateAsync("() => window._setReportDetails('collapsed')");
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return !document.contains(window.__before) && !c._noteRendering && !!c.querySelector(':scope > svg');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        await TwoAnimationFramesAsync(Page);

        var box = JsonSerializer.Deserialize<JsonElement>(await Page.EvaluateAsync<string>("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return JSON.stringify({ fits: c.querySelector(':scope > svg').getBoundingClientRect().width <= c.clientWidth,
                    slider: !!c.querySelector(':scope > .diagram-zoom-controls'), zoomed: c.classList.contains('diagram-natural-size'),
                    cursor: c.style.cursor, overflow: c.style.overflowX });
            }
            """));
        Assert.True(box.GetProperty("fits").GetBoolean(), "folded, the note leaves a diagram that fits its box");
        Assert.False(box.GetProperty("slider").GetBoolean(), "a diagram that fits its box has no zoom slider");
        Assert.False(box.GetProperty("zoomed").GetBoolean(), "a diagram that fits its box is not marked zoomed");
        Assert.Equal("", box.GetProperty("cursor").GetString());
        Assert.Equal("", box.GetProperty("overflow").GetString());
    }

    // A diagram in parts goes back to one part (the Details choice from expanded to truncated): the selected last part is
    // gone, and the selection goes to the part in its place (5.2.5: 5.2.3 left it on the part taken off the page, and
    // Ctrl with + or - went nowhere).
    [Fact]
    public async Task The_selection_on_a_part_a_redraw_takes_away_goes_to_the_last_part_in_its_place()
    {
        await OpenAsync("ZoomKeptSelection.html", noteLines: 900);
        await RedrawAsync("() => window._setReportDetails('expanded')");
        var parts = await BoxesAsync();
        Assert.True(parts.Length >= 2, $"the expanded note has the diagram drawn in parts ({parts.Length})");
        var (x, y) = await PointInBoxAsync(parts.Length - 1);
        await Page.Mouse.ClickAsync(x, y);
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => { var p = document.querySelectorAll('[data-diagram-type=\"plantuml\"] > .puml-fragment'); return p[p.length - 1].classList.contains('diagram-selected'); }"));

        // the Details choice, set as its control sets it without the click (a click outside a diagram unselects it)
        await RedrawAsync("() => window._setReportDetails('truncated')");

        Assert.True(await Page.EvaluateAsync<bool>("""
            () => {
                var s = document.querySelector('.diagram-selected'), p = document.querySelectorAll('[data-diagram-type="plantuml"] > .puml-fragment');
                return !!s && s === p[p.length - 1];
            }
            """), "the part in the selected part's place is selected");
        var before = (await BoxesAsync()).Last().Level;
        for (var i = 0; i < 3; i++) await Page.Keyboard.PressAsync("Control+=");
        await TwoAnimationFramesAsync(Page);
        Assert.True((await BoxesAsync()).Last().Level > before + 1, "the keys zoom the selected part");
    }

    // A selected diagram taken off the page (a popup's closed, a part drawn again away): Ctrl with + or - zoomed the box
    // no longer shown and kept the keys from the browser, so the page could not be zoomed (5.2.5).
    [Fact]
    public async Task A_selected_diagram_taken_off_the_page_leaves_the_zoom_keys_to_the_browser()
    {
        await OpenAsync("ZoomKeysDetached.html", noteLines: 30);
        var (x, y) = await PointInBoxAsync(0);
        await Page.Mouse.ClickAsync(x, y);
        Assert.Equal(1, await Page.Locator(".diagram-selected").CountAsync());

        var taken = await Page.EvaluateAsync<bool>("""
            () => {
                document.querySelector('[data-diagram-type="plantuml"]').remove();
                var e = new KeyboardEvent('keydown', { key: '-', ctrlKey: true, bubbles: true, cancelable: true });
                document.body.dispatchEvent(e);
                return e.defaultPrevented;
            }
            """);
        Assert.False(taken, "the keys go to the browser");
    }

    // A place kept for a diagram drawn again while not displayed, then shown far from the screen and drawn again: the
    // second drawing keeps it too. A guard: 5.2.3 kept it already, the page laying the box out as the second drawing reads it.
    [Fact]
    public async Task A_place_not_yet_given_back_survives_a_second_redraw_while_the_diagram_is_far_from_the_screen()
    {
        await OpenAsync("ZoomKeptFar.html", noteLines: 30, headerLine: true);
        var level = await ZoomPartwayAsync();
        await Page.EvaluateAsync("() => { var d = document.createElement('div'); d.style.height = '30000px'; document.body.appendChild(d); }");
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = 'none'; }");
        await RedrawWhileAwayAsync();

        await Page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = ''; }");
        await TwoAnimationFramesAsync(Page);
        await RedrawWhileAwayAsync();

        await Page.EvaluateAsync("() => document.querySelector('[data-diagram-type=\"plantuml\"]').scrollIntoView({ block: 'center' })");
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('[data-diagram-type=\"plantuml\"] > .diagram-zoom-controls')",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        AssertKept((await BoxesAsync()).Single(), level);
    }

    private async Task RedrawWhileAwayAsync()
    {
        await Page.EvaluateAsync("() => { window.__before = document.querySelector('[data-diagram-type=\"plantuml\"] svg'); }");
        await Page.EvaluateAsync("() => document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\").click()");
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return !document.contains(window.__before) && !c._noteRendering && c.querySelector(':scope > svg');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    // The page gives a diagram coming near the screen its zoom slider. Asked while the diagram is drawn again in parts,
    // with the new parts on the page but hidden, the diagram keeps its zoom for its parts to take (5.2.5: 5.2.3 took it
    // off the diagram then, and every part came back at its fit).
    [Fact]
    public async Task A_diagram_asked_for_its_zoom_while_it_is_drawn_again_in_parts_keeps_it_for_its_parts()
    {
        await OpenAsync("ZoomKeptMidRedraw.html", noteLines: 900);
        var level = await ZoomPartwayAsync();
        await Page.EvaluateAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var watch = new MutationObserver(() => {
                    if (!c.querySelector(':scope > .puml-fragment-new')) return;
                    watch.disconnect();
                    window._addZoomButton(c);
                });
                watch.observe(c, { childList: true });
            }
            """);

        await RedrawAsync(ExpandTheNote);

        var boxes = await BoxesAsync();
        Assert.True(boxes.Length >= 2, $"the expanded note has the diagram drawn in parts ({boxes.Length})");
        Assert.All(boxes, b => AssertKept(b, level));
    }

    // Zoomed, scrolled, then back to its fit and zoomed again without a scroll: the place it was left at is its left
    // edge, not the place it had before it went back to its fit (5.2.5; 5.2.3 kept the old one for a box not laid out).
    [Fact]
    public async Task A_diagram_zoomed_again_without_a_scroll_keeps_its_left_edge_when_drawn_again_while_not_displayed()
    {
        await OpenAsync("ZoomKeptRezoomed.html", noteLines: 30, headerLine: true);
        var level = await ZoomPartwayAsync();
        await Page.EvaluateAsync($$"""
            () => {
                var s = document.querySelector('[data-diagram-type="plantuml"] > .diagram-zoom-controls .diagram-zoom-slider');
                s.value = s.min;
                s.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """);
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync($$"""
            () => {
                var s = document.querySelector('[data-diagram-type="plantuml"] > .diagram-zoom-controls .diagram-zoom-slider');
                s.value = '{{level}}';
                s.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """);
        await TwoAnimationFramesAsync(Page);
        Assert.Equal(0, (await BoxesAsync())[0].ScrollLeft);
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = 'none'; }");

        await Page.EvaluateAsync("() => { window.__before = document.querySelector('[data-diagram-type=\"plantuml\"] svg'); }");
        await Page.EvaluateAsync("() => document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\").click()");
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return !document.contains(window.__before) && !c._noteRendering && c.querySelector(':scope > svg');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = ''; }");
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('[data-diagram-type=\"plantuml\"] > .diagram-zoom-controls')",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        var box = (await BoxesAsync()).Single();
        Assert.True(box.Zoomed);
        Assert.Equal(level, box.Slider);
        Assert.Equal(0, box.ScrollLeft);
    }

    [Fact]
    public async Task The_headers_control_drawing_a_diagram_zoomed_partway_again_keeps_its_level_and_its_place()
    {
        await OpenAsync("ZoomKeptHeaders.html", noteLines: 30, headerLine: true);
        var level = await ZoomPartwayAsync();

        await RedrawAsync("() => document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\").click()");

        var boxes = await BoxesAsync();
        Assert.Single(boxes);
        AssertKept(boxes[0], level);
    }
}
