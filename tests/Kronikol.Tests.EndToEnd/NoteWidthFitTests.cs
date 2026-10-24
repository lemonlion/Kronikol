using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Payload width Wide fits a diagram's notes to its column: a first pass from the note's measured slack, then one
/// correction pass after the drawing, since widening a note moves the participants and the labels over them. Until 5.2.4
/// the correction pass read its flag on the element it decorated, so a diagram drawn in parts (each part decorated on
/// its own, the flag on the diagram) never ran it, and a report's first drawing decorated a diagram while its own
/// drawing still counted as in flight, which the pass waits out, so a report that starts Wide kept its first, fallback
/// width (<c>plans/LONG_NOTE_SCROLL_PLAN.md</c> F3).
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteWidthFitTests : PlaywrightTestBase
{
    public NoteWidthFitTests(PlaywrightFixture fixture) : base(fixture) { }

    private const int Tolerance = 24;

    private async Task OpenAsync(string fileName, int calls, NoteWidthMode noteWidth, int shortFirst = 0)
    {
        await Page.GotoReportAsync(LongPayloadReports.WideQueries(TempDir, OutputDir, fileName, calls, noteWidth, shortFirst));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await SettledAsync();
    }

    /// <summary>Waits until every picture of the diagram is drawn and decorated, nothing is drawing, and the pictures
    /// have stayed the same for a second and a half (a correction pass draws them again).</summary>
    private Task SettledAsync() => Page.WaitForFunctionAsync("""
        () => {
            var c = document.querySelector('[data-diagram-type="plantuml"]');
            var parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
            var boxes = parts.length ? parts : [c];
            var ready = !c._noteRendering && !window._plantumlRendering
                && boxes.every(b => b.querySelector(':scope > svg .note-hover-rect'));
            var first = boxes[0].querySelector(':scope > svg');
            if (!ready || window.__settledSvg !== first) { window.__settledSvg = ready ? first : null; window.__settled = 0; return false; }
            return ++window.__settled >= 8;
        }
        """, null, new() { Timeout = 120000, PollingInterval = 200 });

    /// <summary>The diagram's column and the width its widest picture is drawn at.</summary>
    private async Task<(double Column, double Drawn, int Parts)> WidthsAsync()
    {
        var w = await Page.EvaluateAsync<double[]>("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                var pad = parseFloat(getComputedStyle(c).paddingLeft) || 0;
                var drawn = Math.max.apply(null, (parts.length ? parts : [c]).map(b => window._getDiagramNaturalWidth(b)));
                return [c.clientWidth - pad, drawn, parts.length];
            }
            """);
        return (w[0], w[1], (int)w[2]);
    }

    private static void AssertFits((double Column, double Drawn, int Parts) w) =>
        Assert.True(Math.Abs(w.Column - w.Drawn) <= Tolerance,
            $"drawn {w.Drawn:F0} px in a column of {w.Column:F0} px ({w.Parts} parts)");

    [Fact]
    public async Task A_report_that_starts_Wide_fits_a_diagram_drawn_whole_to_its_column()
    {
        await OpenAsync("width-fit-whole-start.html", calls: 4, NoteWidthMode.Full);
        var w = await WidthsAsync();
        Assert.Equal(0, w.Parts);
        AssertFits(w);
    }

    [Fact]
    public async Task A_report_that_starts_Wide_fits_a_diagram_drawn_in_parts_to_its_column()
    {
        await OpenAsync("width-fit-parts-start.html", calls: 80, NoteWidthMode.Full);
        var w = await WidthsAsync();
        Assert.True(w.Parts > 1, $"premise: the diagram is drawn in parts ({w.Parts})");
        AssertFits(w);
    }

    /// <summary>The first pass measures the diagram's first picture; a first part narrower than the widest part makes it
    /// overshoot, and only the correction pass, measuring every part, brings the widest part back to the column. Since
    /// 5.7.0 the page lines a diagram's parts up, which makes them as wide as each other (1,247 and 1,248 px here), so the
    /// case is a diagram it does not line up: lining up is turned off.</summary>
    [Fact]
    public async Task Payload_width_Wide_fits_a_diagram_drawn_in_parts_to_its_column()
    {
        await Page.AddInitScriptAsync("window.__kronikolLineUp = false;");
        await OpenAsync("width-fit-parts-control.html", calls: 100, NoteWidthMode.Default, shortFirst: 70);
        var parts = await Page.EvaluateAsync<double[]>("""
            () => Array.from(document.querySelectorAll('[data-diagram-type="plantuml"] > .puml-fragment'))
                .map(p => window._getDiagramNaturalWidth(p))
            """);
        Assert.True(parts.Length > 1 && parts[0] < parts.Max() - 100,
            $"premise: drawn in parts, the first narrower than the widest ({string.Join(", ", parts.Select(p => p.ToString("F0")))})");

        await Page.EvaluateAsync("""
            () => {
                var sel = document.querySelector('.toolbar-right .note-width-select');
                sel.value = 'full';
                window._setNoteWidth(sel);
            }
            """);
        await Page.WaitForFunctionAsync(
            "() => (document.querySelector('[data-diagram-type=\"plantuml\"]').getAttribute('data-plantuml') || '').indexOf('kronNoteWide') >= 0",
            null, new() { Timeout = 30000, PollingInterval = 200 });
        await SettledAsync();
        AssertFits(await WidthsAsync());
    }

    /// <summary>The first estimate after Wide measures the diagram's widest part too, so its first drawing already sits near
    /// the column: measured from the first part, narrower than the rest, it drew the diagram 2,347 px wide in a column of
    /// 1,804 px, and only the correction pass brought it back.</summary>
    [Fact]
    public async Task Payload_width_Wide_draws_a_diagram_in_parts_near_its_column_the_first_time()
    {
        await OpenAsync("width-fit-parts-first.html", calls: 100, NoteWidthMode.Default, shortFirst: 70);
        // The widest part's width at each drawing, read as the new parts take the old ones' place.
        await Page.EvaluateAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var shown = () => Array.from(c.querySelectorAll(':scope > .puml-fragment:not(.puml-fragment-new)'));
                var first = shown()[0];
                window.__drawings = [];
                new MutationObserver(function() {
                    var parts = shown();
                    if (!parts.length || parts[0] === first) return;
                    first = parts[0];
                    window.__drawings.push(Math.max.apply(null, parts.map(p => window._getDiagramNaturalWidth(p))));
                }).observe(c, { childList: true });
            }
            """);

        await Page.EvaluateAsync("""
            () => {
                var sel = document.querySelector('.toolbar-right .note-width-select');
                sel.value = 'full';
                window._setNoteWidth(sel);
            }
            """);
        await Page.WaitForFunctionAsync(
            "() => (document.querySelector('[data-diagram-type=\"plantuml\"]').getAttribute('data-plantuml') || '').indexOf('kronNoteWide') >= 0",
            null, new() { Timeout = 30000, PollingInterval = 200 });
        await SettledAsync();
        var drawings = await Page.EvaluateAsync<double[]>("() => window.__drawings");
        var w = await WidthsAsync();
        Assert.True(drawings.Length > 0 && w.Parts > 1, $"premise: drawn again in parts ({drawings.Length} drawings, {w.Parts} parts)");
        Assert.True(Math.Abs(drawings[0] - w.Column) <= w.Column / 10,
            $"the first drawing after Wide was {drawings[0]:F0} px wide in a column of {w.Column:F0} px (then {string.Join(", ", drawings.Skip(1).Select(d => d.ToString("F0")))})");
    }
}
