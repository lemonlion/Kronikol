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

    private async Task OpenAsync(string fileName, int noteLines, bool headerLine = false)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithWideLongNoteDiagram(TempDir, OutputDir, fileName, noteLines, headerLine));
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
