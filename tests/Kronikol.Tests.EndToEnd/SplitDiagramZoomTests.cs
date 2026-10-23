using System.Text.Json;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Zoom on a long diagram the page draws in parts (<c>.puml-fragment</c>, each its own picture inside the one diagram
/// container). Each part zooms on its own: until 5.2.2 the wheel and the keyboard over any part zoomed the first part,
/// a part's own slider never showed, and a part could not be panned.
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
        await Page.WaitForFunctionAsync("""
            () => {
                var parts = document.querySelectorAll('[data-diagram-type] > .puml-fragment');
                return parts.length >= 2 && Array.from(parts).every(p => p.querySelector(':scope > svg')
                    && p.querySelector(':scope > .diagram-zoom-controls'));
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    private sealed record PartState(int Width, int Slider, int Min, int ScrollLeft, bool Selected);

    private async Task<PartState[]> PartsAsync()
    {
        var json = await Page.EvaluateAsync<string>("""
            () => JSON.stringify(Array.from(document.querySelectorAll('[data-diagram-type] > .puml-fragment')).map(p => {
                var s = p.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                return { Width: Math.round(p.querySelector(':scope > svg').getBoundingClientRect().width),
                    Slider: s ? parseInt(s.value) : -1, Min: s ? parseInt(s.min) : -1,
                    ScrollLeft: Math.round(p.scrollLeft), Selected: p.classList.contains('diagram-selected') };
            }))
            """);
        return JsonSerializer.Deserialize<PartState[]>(json)!;
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

    // The scroll observer gives the diagram's container its zoom when it nears the screen, and each part's drawing gives
    // that part its zoom: whichever comes first, the container gets no slider of its own (it drew the first part's).
    [Fact]
    public async Task Each_part_has_its_own_zoom_slider_and_the_diagram_has_none_of_its_own_whichever_comes_first()
    {
        await OpenAsync("SplitZoomSliders.html");

        await Page.EvaluateAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type] > .puml-fragment').parentElement;
                container.querySelectorAll('.diagram-zoom-controls').forEach(c => c.remove());
                window._addZoomButton(container);
                container.querySelectorAll(':scope > .puml-fragment').forEach(p => window._addZoomButton(p));
            }
            """);

        var parts = await PartsAsync();
        Assert.All(parts, p => Assert.True(p.Min is > 0 and < 100, $"each part's slider starts at its fit, min {p.Min}"));
        Assert.Equal(parts.Length, await Page.Locator("[data-diagram-type] .diagram-zoom-controls").CountAsync());
        Assert.Equal(0, await Page.Locator("[data-diagram-type] > .diagram-zoom-controls").CountAsync());
    }

    [Fact]
    public async Task Ctrl_wheel_over_the_second_part_zooms_that_part_alone()
    {
        await OpenAsync("SplitZoomWheel.html");
        var before = await PartsAsync();

        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.MoveAsync(x, y);
        await Page.Keyboard.DownAsync("Control");
        for (var i = 0; i < 3; i++) await Page.Mouse.WheelAsync(0, -100);
        await Page.Keyboard.UpAsync("Control");
        await TwoAnimationFramesAsync(Page);

        var after = await PartsAsync();
        Assert.True(after[1].Width > before[1].Width, $"the second part grows: {before[1].Width} -> {after[1].Width}");
        Assert.True(after[1].Slider > before[1].Slider, $"its slider follows: {before[1].Slider} -> {after[1].Slider}");
        Assert.Equal(before[0].Width, after[0].Width);
        Assert.Equal(before[0].Slider, after[0].Slider);
    }

    [Fact]
    public async Task Ctrl_plus_after_clicking_the_second_part_zooms_that_part_alone()
    {
        await OpenAsync("SplitZoomKeys.html");
        var before = await PartsAsync();

        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.ClickAsync(x, y);
        for (var i = 0; i < 3; i++) await Page.Keyboard.PressAsync("Control+=");
        await TwoAnimationFramesAsync(Page);

        var after = await PartsAsync();
        Assert.True(after[1].Selected, "clicking a part selects that part");
        Assert.False(after[0].Selected);
        Assert.True(after[1].Width > before[1].Width, $"the second part grows: {before[1].Width} -> {after[1].Width}");
        Assert.Equal(before[0].Width, after[0].Width);
    }

    [Fact]
    public async Task Hovering_the_second_part_shows_its_own_slider()
    {
        await OpenAsync("SplitZoomHover.html");

        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.MoveAsync(x, y);

        await Page.WaitForFunctionAsync("""
            () => {
                var c = getComputedStyle(document.querySelectorAll('[data-diagram-type] > .puml-fragment')[1]
                    .querySelector(':scope > .diagram-zoom-controls'));
                return parseFloat(c.opacity) > 0 && c.pointerEvents === 'auto';
            }
            """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    private Task ZoomPartFullyAsync(int index) => Page.EvaluateAsync("""
        (i) => {
            var s = document.querySelectorAll('[data-diagram-type] > .puml-fragment')[i]
                .querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
            s.value = '100';
            s.dispatchEvent(new Event('input', { bubbles: true }));
        }
        """, index);

    [Fact]
    public async Task Dragging_a_zoomed_second_part_pans_it_and_leaves_it_selected()
    {
        await OpenAsync("SplitZoomPan.html");
        await ZoomPartFullyAsync(1);
        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.ClickAsync(x, y);
        var before = await PartsAsync();
        Assert.True(before[1].Selected);

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x - 200, y, new() { Steps = 5 });
        await Page.Mouse.UpAsync();
        await TwoAnimationFramesAsync(Page);

        var after = await PartsAsync();
        Assert.True(after[1].ScrollLeft > before[1].ScrollLeft, $"the part pans: {before[1].ScrollLeft} -> {after[1].ScrollLeft}");
        Assert.True(after[1].Selected, "a drag is not a click: the part stays selected");
    }

    private async Task RedrawAndWaitAsync(string redraw)
    {
        await Page.EvaluateAsync("() => { window.__before = document.querySelectorAll('[data-diagram-type] > .puml-fragment')[1]; }");
        await Page.Locator("button[data-toggle='headers'][onclick*='_toggleHeaders(']").EvaluateAsync(redraw);
        await WaitForNewPartsAsync();
    }

    /// <summary>Waits for the diagram drawn again: the part held in <c>window.__before</c> gone, every new part drawn.</summary>
    private async Task WaitForNewPartsAsync()
    {
        await Page.WaitForFunctionAsync("""
            () => {
                var parts = document.querySelectorAll('[data-diagram-type] > .puml-fragment');
                return parts.length >= 2 && !document.contains(window.__before) && Array.from(parts).every(p => p.querySelector(':scope > svg')
                    && p.querySelector(':scope > .diagram-zoom-controls'));
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    // The headers toggle draws the diagram again, in new parts: the second part, zoomed, stays zoomed, and the first
    // stays at its fit.
    [Fact]
    public async Task A_zoomed_second_part_stays_zoomed_when_the_headers_toggle_draws_the_diagram_again()
    {
        await OpenAsync("SplitZoomRedraw.html", headerNotes: true);
        await ZoomPartFullyAsync(1);

        await RedrawAndWaitAsync("b => b.click()");

        var after = await PartsAsync();
        Assert.Equal(100, after[1].Slider);
        Assert.True(after[1].Width > after[0].Width, $"the second part is drawn wider than the first: {after[1].Width} against {after[0].Width}");
        Assert.Equal(after[0].Min, after[0].Slider);
    }

    // Drawn again twice while the scenario is not displayed (Headers on and off), the second part is a new part each time
    // and never laid out in between: the place it was left at goes from part to part until it is shown (5.2.3).
    [Fact]
    public async Task A_second_part_zoomed_partway_keeps_its_place_when_drawn_again_twice_while_not_displayed()
    {
        await OpenAsync("SplitZoomRedrawHidden.html", headerNotes: true);
        var level = await Page.EvaluateAsync<int>("""
            () => {
                var p = document.querySelectorAll('[data-diagram-type] > .puml-fragment')[1];
                var s = p.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                s.value = String(Math.round((parseInt(s.min) + 100) / 2));
                s.dispatchEvent(new Event('input', { bubbles: true }));
                p.scrollLeft = 150;
                return parseInt(s.value);
            }
            """);
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = 'none'; }");

        for (var i = 0; i < 2; i++)
        {
            await Page.EvaluateAsync("() => { window.__before = document.querySelectorAll('[data-diagram-type] > .puml-fragment')[1]; }");
            await Page.EvaluateAsync("() => document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\").click()");
            await Page.WaitForFunctionAsync("""
                () => {
                    var parts = document.querySelectorAll('[data-diagram-type] > .puml-fragment');
                    return parts.length >= 2 && !document.contains(window.__before) && Array.from(parts).every(p => p.querySelector(':scope > svg'));
                }
                """, null, new() { Timeout = 60000, PollingInterval = 200 });
            await TwoAnimationFramesAsync(Page);
        }
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').style.display = ''; }");
        await PointInPartAsync(1);
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelectorAll('[data-diagram-type] > .puml-fragment')[1].querySelector(':scope > .diagram-zoom-controls')",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        var after = await PartsAsync();
        Assert.Equal(level, after[1].Slider);
        Assert.Equal(150, after[1].ScrollLeft);
    }

    // Zoomed partway and scrolled sideways, the second part keeps both its level and its place when drawn again (5.2.3).
    [Fact]
    public async Task A_second_part_zoomed_partway_keeps_its_level_and_its_place_when_the_headers_toggle_draws_it_again()
    {
        await OpenAsync("SplitZoomRedrawPartway.html", headerNotes: true);
        var level = await Page.EvaluateAsync<int>("""
            () => {
                var p = document.querySelectorAll('[data-diagram-type] > .puml-fragment')[1];
                var s = p.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                s.value = String(Math.round((parseInt(s.min) + 100) / 2));
                s.dispatchEvent(new Event('input', { bubbles: true }));
                p.scrollLeft = 150;
                return parseInt(s.value);
            }
            """);
        Assert.InRange(level, 60, 99);

        await RedrawAndWaitAsync("b => b.click()");

        var after = await PartsAsync();
        Assert.Equal(level, after[1].Slider);
        Assert.Equal(150, after[1].ScrollLeft);
        Assert.Equal(after[0].Min, after[0].Slider);
    }

    // A click outside a diagram unselects it, so the toggle is called here as a redraw that comes without one (a note's
    // own button, a key): the new second part is the one selected, and the keys zoom it.
    [Fact]
    public async Task The_selected_second_part_stays_selected_when_the_diagram_is_drawn_again_and_the_keys_zoom_it()
    {
        await OpenAsync("SplitZoomRedrawKeys.html", headerNotes: true);
        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.ClickAsync(x, y);

        await RedrawAndWaitAsync("b => window._toggleHeaders(b)");
        var before = await PartsAsync();
        Assert.True(before[1].Selected, "the new second part is the one selected");
        for (var i = 0; i < 3; i++) await Page.Keyboard.PressAsync("Control+=");
        await TwoAnimationFramesAsync(Page);

        var after = await PartsAsync();
        Assert.True(after[1].Width > before[1].Width, $"the keys zoom the new second part: {before[1].Width} -> {after[1].Width}");
        Assert.Equal(before[0].Width, after[0].Width);
    }

    // A note's own button draws the diagram again by its own path, and its click goes no further than the button: the
    // zoomed and selected second part is drawn again zoomed and still selected.
    [Fact]
    public async Task A_note_button_drawing_the_diagram_again_leaves_the_second_part_zoomed_and_selected()
    {
        await OpenAsync("SplitZoomNoteButton.html", headerNotes: true);
        await ZoomPartFullyAsync(1);
        var (x, y) = await PointInPartAsync(1);
        await Page.Mouse.ClickAsync(x, y);
        Assert.True((await PartsAsync())[1].Selected);
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

        var after = await PartsAsync();
        Assert.Equal(100, after[1].Slider);
        Assert.True(after[1].Width > after[0].Width, $"the second part is drawn wider than the first: {after[1].Width} against {after[0].Width}");
        Assert.True(after[1].Selected, "the new second part is the one selected");
        Assert.Equal(after[0].Min, after[0].Slider);
    }
}
