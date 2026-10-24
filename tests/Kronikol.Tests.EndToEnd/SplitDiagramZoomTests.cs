using System.Text.Json;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Zoom on a long diagram the page draws in parts (<c>.puml-fragment</c>, each its own picture inside the one diagram
/// container). The diagram has one scale and one zoom (<c>plans/ZOOM_EVERY_DIAGRAM_PLAN.md</c> Z8, 5.7.0): one slider on
/// its container, its parts drawn at one level, zoomed and panned together. From 5.2.2 each part zoomed on its own, so
/// one diagram could be drawn at three text sizes; until 5.2.2 every zoom on it went to its first part.
/// </summary>
[Collection(PlaywrightCollections.Zoom)]
public class SplitDiagramZoomTests : PlaywrightTestBase
{
    public SplitDiagramZoomTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenAsync(string fileName, bool headerNotes = false)
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithWideDiagramInParts(TempDir, OutputDir, fileName, headerNotes));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForPartsAsync();
    }

    private async Task WaitForPartsAsync()
    {
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type] > .puml-fragment');
                c = c && c.parentElement;
                if (!c || c._noteRendering) return false;
                var parts = c.querySelectorAll(':scope > .puml-fragment');
                return parts.length >= 2 && Array.from(parts).every(p => p.querySelector(':scope > svg'))
                    && !!c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    private sealed record PartsState(int Slider, int Min, int Sliders, bool Selected, int PartsSelected, int ScrollLeft,
        double[] Widths, double[] Levels, double[] Lefts);

    private async Task<PartsState> StateAsync()
    {
        var json = await Page.EvaluateAsync<string>("""
            () => {
                var c = document.querySelector('[data-diagram-type] > .puml-fragment').parentElement;
                var s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                var pics = Array.from(c.querySelectorAll(':scope > .puml-fragment')).map(p => p.querySelector(':scope > svg'));
                return JSON.stringify({ Slider: s ? parseInt(s.value) : -1, Min: s ? parseInt(s.min) : -1,
                    Sliders: c.querySelectorAll('.diagram-zoom-controls').length,
                    Selected: c.classList.contains('diagram-selected'),
                    PartsSelected: c.querySelectorAll('.puml-fragment.diagram-selected').length,
                    ScrollLeft: Math.round(c.scrollLeft),
                    Widths: pics.map(p => p.getBoundingClientRect().width),
                    Levels: pics.map(p => p.getBoundingClientRect().width / p.width.baseVal.value * 100),
                    Lefts: pics.map(p => p.getBoundingClientRect().left) });
            }
            """);
        return JsonSerializer.Deserialize<PartsState>(json)!;
    }

    /// <summary>A point inside the visible half of part <paramref name="index"/>, below the sticky bar, scrolled to.</summary>
    private async Task<(float X, float Y)> PointInPartAsync(int index)
    {
        var p = await Page.EvaluateAsync<float[]>("""
            (i) => {
                var part = document.querySelectorAll('[data-diagram-type] > .puml-fragment')[i];
                part.scrollIntoView({ block: 'center' });
                var r = part.getBoundingClientRect(), top = Math.max(r.top, 160), bottom = Math.min(r.bottom, innerHeight - 20);
                return [r.left + r.width / 2, (top + bottom) / 2];
            }
            """, index);
        await TwoAnimationFramesAsync(Page);
        return (p[0], p[1]);
    }

    private static void AssertOneLevel(PartsState s)
    {
        Assert.True(s.Levels.Max() - s.Levels.Min() <= 0.5, $"every part is drawn at one level: {string.Join(", ", s.Levels.Select(l => l.ToString("F1")))}");
    }

    // The scroll observer gives the diagram's container its zoom when it nears the screen, and each part's drawing asks for
    // it again: whichever comes first, the diagram has one slider, on its container (5.2.2 to 5.6 gave each part its own).
    [Fact]
    public async Task The_diagram_has_one_slider_on_its_container_and_no_part_has_one_whichever_comes_first()
    {
        await OpenAsync("SplitZoomSliders.html");

        await Page.EvaluateAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type] > .puml-fragment').parentElement;
                container.querySelectorAll('.diagram-zoom-controls').forEach(c => c.remove());
                container.querySelectorAll(':scope > .puml-fragment').forEach(p => window._addZoomButton(p));
                window._addZoomButton(container);
            }
            """);

        var s = await StateAsync();
        Assert.Equal(1, s.Sliders);
        Assert.Equal(1, await Page.Locator("[data-diagram-type] > .diagram-zoom-controls").CountAsync());
        Assert.True(s.Min is > 0 and < 100, $"the diagram is wide: its slider starts at its fit, min {s.Min}");
        Assert.Equal(s.Min, s.Slider);
        AssertOneLevel(s);
    }

    // Every redraw makes a diagram's slider again, at the level its picture is drawn at, and a toggle redraws every
    // diagram: that changes no size, so the page's scroll and the browser's scroll anchoring are left alone. Held off
    // each time, a reader's jump during a toggle landed 10,863 px short on a report of 60 diagrams, where the browser
    // would have moved it on by what it laid out late (content-visibility) (CI, 5.7.0's first run)
    [Fact]
    public async Task A_slider_made_again_at_the_level_its_parts_are_drawn_at_leaves_the_scroll_alone()
    {
        await OpenAsync("SplitZoomAnchoring.html");
        var r = await Page.EvaluateAsync<string>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const y = scrollY;
                window._addZoomButton(c);
                return document.documentElement.style.overflowAnchor + '|' + (scrollY - y);
            }
            """);
        Assert.Equal("|0", r);
    }

    // 7 of section 6.2: one slider, starting at the smallest fit level of the parts, every part drawn at that level
    [Fact]
    public async Task A_diagram_in_parts_starts_at_its_fit_level_with_every_part_drawn_at_that_level()
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "SplitZoomStart.html",
            ReportTestHelper.PartsSource(30, new Dictionary<int, int> { [3] = 260 })));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForPartsAsync();

        var s = await StateAsync();
        Assert.True(s.Widths.Length >= 3, $"the diagram is drawn in parts: {s.Widths.Length}");
        Assert.Equal(1, s.Sliders);
        Assert.True(s.Min is > 0 and < 100, $"its first part is wide: the slider starts at the diagram's fit, min {s.Min}");
        Assert.Equal(s.Min, s.Slider);
        AssertOneLevel(s);
        Assert.True(Math.Abs(s.Levels[0] - s.Min) <= 1, $"drawn at its fit level: {s.Levels[0]:F1}% against {s.Min}");
    }

    // 7, on a diagram the page does not line up (no participant declared, so each part is laid out alone): its level is
    // the one that lets its widest part fit, here a later one
    [Fact]
    public async Task A_diagram_in_parts_not_lined_up_takes_its_fit_level_from_its_widest_part_a_later_one()
    {
        var sb = new System.Text.StringBuilder("@startuml\n!pragma teoz true\n");
        for (var k = 1; k <= 30; k++)
            sb.Append($"A -> B : call{k}{(k == 18 ? " " + new string('w', 260) : "")}\nB --> A : ok{k}\n");
        sb.Append("@enduml");
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "SplitZoomLaterWidest.html", sb.ToString()));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForPartsAsync();

        var s = await StateAsync();
        Assert.True(s.Widths.Length >= 3, $"the diagram is drawn in parts: {s.Widths.Length}");
        Assert.False(await Page.EvaluateAsync<bool>("() => !!document.querySelector(\"[data-diagram-type='plantuml']\")._lineUp"),
            "premise: the diagram is not lined up");
        Assert.True(s.Widths.Skip(1).Max() > s.Widths[0] + 10, $"premise: a later part is the widest: {string.Join(", ", s.Widths.Select(w => w.ToString("F0")))}");
        Assert.Equal(1, s.Sliders);
        Assert.True(s.Min is > 0 and < 100, $"its widest part is wide: the slider starts at the diagram's fit, min {s.Min}");
        Assert.Equal(s.Min, s.Slider);
        AssertOneLevel(s);
        Assert.True(Math.Abs(s.Levels[0] - s.Min) <= 1, $"drawn at its fit level: {s.Levels[0]:F1}% against {s.Min}");
    }

    // 14 of section 6.2, and 5.2.2's fact turned round: the wheel over the second part zooms every part, keeping the point
    // under the pointer across and down the page
    [Fact]
    public async Task Ctrl_wheel_over_the_second_part_zooms_every_part_and_keeps_the_point_under_the_pointer()
    {
        await OpenAsync("SplitZoomWheel.html");
        var before = await StateAsync();
        var (x, y) = await PointInPartAsync(1);
        await ZoomFacts.MarkPointAsync(Page, x, y);
        await ZoomFacts.WatchWheelAsync(Page);

        await ZoomFacts.CtrlWheelAsync(Page, x, y, 3);

        var after = await StateAsync();
        Assert.True(after.Slider > before.Slider, $"the slider follows: {before.Slider} -> {after.Slider}");
        Assert.True(after.Widths[0] > before.Widths[0] + 1 && after.Widths[1] > before.Widths[1] + 1, "every part grows");
        AssertOneLevel(after);
        var (across, down) = await ZoomFacts.PointMovedAsync(Page);
        Assert.True(Math.Abs(across) <= 2, $"the point under the pointer stays across: {across:F1} px");
        Assert.True(Math.Abs(down) <= 2, $"the point under the pointer stays down the page: {down:F1} px");
        Assert.All(await ZoomFacts.WheelsPreventedAsync(Page), p => Assert.True(p));
    }

    [Fact]
    public async Task Ctrl_plus_after_clicking_the_second_part_selects_the_diagram_and_zooms_every_part()
    {
        await OpenAsync("SplitZoomKeys.html");
        var before = await StateAsync();

        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.ClickAsync(x, y);
        for (var i = 0; i < 3; i++) await Page.Keyboard.PressAsync("Control+=");
        await TwoAnimationFramesAsync(Page);

        var after = await StateAsync();
        Assert.True(after.Selected, "clicking a part selects the diagram");
        Assert.Equal(0, after.PartsSelected);
        Assert.True(after.Widths[0] > before.Widths[0] + 1 && after.Widths[1] > before.Widths[1] + 1, "every part grows");
        AssertOneLevel(after);
    }

    [Fact]
    public async Task Hovering_the_second_part_shows_the_diagrams_slider_in_the_window()
    {
        await OpenAsync("SplitZoomHover.html");

        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.MoveAsync(x + 5, y + 5);

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type] > .puml-fragment').parentElement;
                var ctl = c.querySelector(':scope > .diagram-zoom-controls'), cs = getComputedStyle(ctl);
                var r = ctl.querySelector('.diagram-zoom-slider').getBoundingClientRect();
                return parseFloat(cs.opacity) > 0 && cs.pointerEvents === 'auto' && r.top >= 0 && r.bottom <= innerHeight;
            }
            """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    // 15 of section 6.2
    [Fact]
    public async Task Dragging_the_zoomed_diagram_over_its_second_part_pans_every_part_together_and_leaves_it_selected()
    {
        await OpenAsync("SplitZoomPan.html");
        await ZoomFacts.SetLevelAsync(Page, 0, 100);
        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.ClickAsync(x, y);
        var before = await StateAsync();
        Assert.True(before.Selected);

        await Page.Mouse.MoveAsync(x, y);
        // the grab cursor on the picture under the pointer, and on no other part's (5.7.0: written on the box, it
        // restyled every element of every part)
        var cursors = await PartCursorsAsync();
        Assert.Equal("grab", cursors[1]);
        Assert.All(cursors.Where((_, k) => k != 1), c => Assert.Equal("auto", c));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x - 200, y, new() { Steps = 5 });
        var dragging = await PartCursorsAsync();
        await Page.Mouse.UpAsync();
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("grabbing", dragging[1]);

        var after = await StateAsync();
        Assert.True(after.ScrollLeft > before.ScrollLeft, $"the diagram pans: {before.ScrollLeft} -> {after.ScrollLeft}");
        var moved = before.Lefts.Zip(after.Lefts, (b, a) => b - a).ToArray();
        Assert.True(moved.Max() - moved.Min() <= 1, $"every part moves together: {string.Join(", ", moved.Select(m => m.ToString("F1")))}");
        Assert.True(after.Selected, "a drag is not a click: the diagram stays selected");
    }

    /// <summary>The cursor over each part's picture, in order.</summary>
    private Task<string[]> PartCursorsAsync() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('[data-diagram-type] > .puml-fragment > svg')).map(s => getComputedStyle(s).cursor)");

    private async Task RedrawAndWaitAsync(string redraw)
    {
        await Page.EvaluateAsync("() => { window.__before = document.querySelectorAll('[data-diagram-type] > .puml-fragment')[1]; }");
        await Page.Locator("button[data-toggle='headers'][onclick*='_toggleHeaders(']").EvaluateAsync(redraw);
        await WaitForNewPartsAsync();
    }

    /// <summary>Waits for the diagram drawn again: the part held in <c>window.__before</c> gone, every new part drawn.</summary>
    private async Task WaitForNewPartsAsync()
    {
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before)", null, new() { Timeout = 60000, PollingInterval = 200 });
        await WaitForPartsAsync();
    }

    // The headers toggle draws the diagram again, in new parts: zoomed to full size, it stays so, every part with it.
    [Fact]
    public async Task A_zoomed_diagram_in_parts_stays_zoomed_when_the_headers_toggle_draws_it_again()
    {
        await OpenAsync("SplitZoomRedraw.html", headerNotes: true);
        await ZoomFacts.SetLevelAsync(Page, 0, 100);

        await RedrawAndWaitAsync("b => b.click()");

        var after = await StateAsync();
        Assert.Equal(100, after.Slider);
        AssertOneLevel(after);
        Assert.True(Math.Abs(after.Levels[0] - 100) <= 0.5, $"every part at full size: {after.Levels[0]:F1}%");
    }

    // Drawn again twice while the scenario is not displayed (Headers on and off), the diagram's parts are new parts each
    // time and never laid out in between: the place it was left at stays with the diagram until it is shown (5.2.3).
    [Fact]
    public async Task A_diagram_in_parts_zoomed_partway_keeps_its_place_when_drawn_again_twice_while_not_displayed()
    {
        await OpenAsync("SplitZoomRedrawHidden.html", headerNotes: true);
        var level = await ZoomPartwayAsync();
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = 'none'; }");

        for (var i = 0; i < 2; i++)
        {
            await Page.EvaluateAsync("() => { window.__before = document.querySelectorAll('[data-diagram-type] > .puml-fragment')[1]; }");
            await Page.EvaluateAsync("() => document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\").click()");
            await Page.WaitForFunctionAsync("""
                () => {
                    var parts = document.querySelectorAll('[data-diagram-type] > .puml-fragment');
                    return parts.length >= 2 && !document.contains(window.__before) && Array.from(parts).every(p => p.querySelector(':scope > svg'))
                        && !parts[0].parentElement._noteRendering;
                }
                """, null, new() { Timeout = 60000, PollingInterval = 200 });
            await TwoAnimationFramesAsync(Page);
        }
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = ''; }");
        await PointInPartAsync(1);
        await WaitForPartsAsync();

        var after = await StateAsync();
        Assert.Equal(level, after.Slider);
        Assert.Equal(150, after.ScrollLeft);
        AssertOneLevel(after);
    }

    private async Task<int> ZoomPartwayAsync()
    {
        var level = await Page.EvaluateAsync<int>("""
            () => {
                var c = document.querySelector('[data-diagram-type] > .puml-fragment').parentElement;
                var s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                s.value = String(Math.round((parseInt(s.min) + 100) / 2));
                s.dispatchEvent(new Event('input', { bubbles: true }));
                c.scrollLeft = 150;
                return parseInt(s.value);
            }
            """);
        await TwoAnimationFramesAsync(Page);
        return level;
    }

    // Zoomed partway and scrolled sideways, the diagram keeps both its level and its place when drawn again (5.2.3).
    [Fact]
    public async Task A_diagram_in_parts_zoomed_partway_keeps_its_level_and_its_place_when_the_headers_toggle_draws_it_again()
    {
        await OpenAsync("SplitZoomRedrawPartway.html", headerNotes: true);
        var level = await ZoomPartwayAsync();
        Assert.InRange(level, 60, 99);

        await RedrawAndWaitAsync("b => b.click()");

        var after = await StateAsync();
        Assert.Equal(level, after.Slider);
        Assert.Equal(150, after.ScrollLeft);
        AssertOneLevel(after);
    }

    // A click outside a diagram unselects it, so the toggle is called here as a redraw that comes without one (a note's
    // own button, a key): the diagram stays selected, and the keys zoom every part.
    [Fact]
    public async Task The_selected_diagram_in_parts_stays_selected_when_drawn_again_and_the_keys_zoom_every_part()
    {
        await OpenAsync("SplitZoomRedrawKeys.html", headerNotes: true);
        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.ClickAsync(x, y);

        await RedrawAndWaitAsync("b => window._toggleHeaders(b)");
        var before = await StateAsync();
        Assert.True(before.Selected, "the diagram is still the one selected");
        Assert.Equal(0, before.PartsSelected);
        for (var i = 0; i < 3; i++) await Page.Keyboard.PressAsync("Control+=");
        await TwoAnimationFramesAsync(Page);

        var after = await StateAsync();
        Assert.True(after.Widths[0] > before.Widths[0] + 1 && after.Widths[1] > before.Widths[1] + 1, "the keys zoom every part");
        AssertOneLevel(after);
    }

    // A note's own button draws the diagram again by its own path, and its click goes no further than the button: the
    // zoomed and selected diagram is drawn again zoomed and still selected.
    [Fact]
    public async Task A_note_button_drawing_the_diagram_again_leaves_it_zoomed_and_selected()
    {
        await OpenAsync("SplitZoomNoteButton.html", headerNotes: true);
        await ZoomFacts.SetLevelAsync(Page, 0, 100);
        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.ClickAsync(x, y);
        Assert.True((await StateAsync()).Selected);
        await Page.EvaluateAsync("""
            () => {
                window.__before = document.querySelectorAll('[data-diagram-type] > .puml-fragment')[1];
                window.__before.querySelector('.note-hover-rect').dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
            }
            """);
        await Page.WaitForFunctionAsync("() => !!window.__before.querySelector(\"[data-note-btn='minus'] rect\")",
            null, new() { Timeout = 15000, PollingInterval = 200 });

        await Page.EvaluateAsync("""
            () => window.__before.querySelector("[data-note-btn='minus'] rect")
                .dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
            """);
        await WaitForNewPartsAsync();

        var after = await StateAsync();
        Assert.Equal(100, after.Slider);
        AssertOneLevel(after);
        Assert.True(after.Selected, "the diagram is still the one selected");
    }

    // 18 of section 6.2: drawn whole again (Details from expanded to truncated) and in parts again, the diagram keeps its
    // level, its direction and its selection on its container
    [Fact]
    public async Task A_diagram_drawn_whole_and_then_in_parts_again_keeps_its_level_direction_and_selection()
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithWideLongNoteDiagram(TempDir, OutputDir, "SplitZoomWholeAndBack.html", noteLines: 900));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await ZoomFacts.WaitForSliderAsync(Page, 0);
        await RedrawDetailsAsync("expanded");
        Assert.True((await ZoomFacts.ReadAsync(Page, 0)).Levels.Length >= 2, "expanded, the note has the diagram drawn in parts");
        var level = await ZoomPartwayAsync();
        var (x, y) = await PointInPartAsync(0);
        await Page.Mouse.ClickAsync(x, y);

        await RedrawDetailsAsync("truncated");
        var whole = await ZoomFacts.ReadAsync(Page, 0);
        Assert.Single(whole.Levels);
        Assert.True(whole.In && whole.Selected, "drawn whole, the diagram is still zoomed in and selected");
        Assert.Equal(level, whole.Slider);

        await RedrawDetailsAsync("expanded");
        var parts = await ZoomFacts.ReadAsync(Page, 0);
        Assert.True(parts.Levels.Length >= 2);
        Assert.True(parts.In && parts.Selected, "drawn in parts again, the diagram is still zoomed in and selected");
        Assert.Equal(level, parts.Slider);
        Assert.True(parts.Levels.Max() - parts.Levels.Min() <= 0.5);
    }

    private async Task RedrawDetailsAsync(string state)
    {
        await Page.EvaluateAsync("() => { window.__before = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); }");
        await Page.EvaluateAsync("s => window._setReportDetails(s)", state);
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before)", null, new() { Timeout = 60000, PollingInterval = 200 });
        await ZoomFacts.WaitForSliderAsync(Page, 0);
    }
}
