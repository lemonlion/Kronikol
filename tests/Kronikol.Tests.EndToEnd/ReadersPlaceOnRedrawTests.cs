using System.Net;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A control that draws diagrams again keeps the reader's place (#227, plans/TOGGLE_JUMP_PLAN.md): the drawn text at the
/// reading line, the middle of the window below the bar, stands where it stood once the redraw is done, however many
/// diagrams above it grow, are zoomed or are cut by the window's edge. Until then the page left a redraw to the browser's
/// scroll anchoring, which anchors on what is at the top of the window: a redraw that removed that picture moved the page
/// by its change of height (fault B), and putting a zoomed diagram's zoom back after its redraw scrolled the page by a
/// place read on the new picture at its fit size, and by all the height its zoom gives back for a zoomed diagram far above
/// the window (fault A). Every fact measures what the plan's probe measures: the text of the diagram read nearest the
/// reading line before the control is used, found again after by its words and which time they occur in the diagram; and
/// no scroll is written by the zoom code's own place keeping meanwhile.
/// </summary>
[Collection(PlaywrightCollections.Zoom)]
public class ReadersPlaceOnRedrawTests : PlaywrightTestBase
{
    public ReadersPlaceOnRedrawTests(PlaywrightFixture fixture) : base(fixture) { }

    // the window the plan measured in: what a 1920 x 1080 screen shows at about 140%
    protected override int ViewportWidth => 1366;
    protected override int ViewportHeight => 768;

    /// <summary>How far the text read may stand from where it stood: the browser scrolls by whole pixels.</summary>
    private const double Kept = 2;

    // eight scenarios of forty calls, each diagram wider than the page and taller than the window; written once for the
    // class, outside any test's folder
    private static readonly Lazy<string> Tall = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-place-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return ReportTestHelper.GenerateReportWithTallWideDiagrams(dir, OutputDir, "ReadersPlace_Tall.html");
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    // the same, the first scenario's diagram long enough for the page to draw it in parts
    private static readonly Lazy<string> PartsAbove = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-place-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return ReportTestHelper.GenerateReportWithTallWideDiagrams(dir, OutputDir, "ReadersPlace_PartsAbove.html", scenarios: 3,
            firstInParts: true);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The plan's measure in the page (plans/TOGGLE_JUMP_PLAN.harness/probe/jump.js): placing a scenario's diagram, zooming
    /// it, the text nearest the reading line remembered, every write to the window's scroll from then on with its caller,
    /// and the text's and the diagram's place every frame.
    /// </summary>
    private const string Kit = """
        () => {
            const frames = (n) => new Promise((done) => { const t = () => (--n <= 0 ? setTimeout(done, 0) : requestAnimationFrame(t)); requestAnimationFrame(t); });
            // a scenario's diagram by the scenario's index, or a diagram by a selector
            const boxOf = (i) => typeof i === 'number' ? document.querySelectorAll('details.scenario')[i].querySelector('[data-diagram-type="plantuml"]')
                : document.querySelector(i);
            // the texts of the pictures a diagram shows: its own, or its drawn parts' (never one drawn out of sight)
            const textsOf = (c) => Array.from(c.querySelectorAll(':scope > svg text, :scope > .puml-fragment:not(.puml-fragment-new) > svg text'));
            const middleOf = (el) => { const r = el.getBoundingClientRect(); return r.height ? (r.top + r.bottom) / 2 : null; };
            // the middle of the window below the bar and any settings stuck under it
            const readingLine = () => {
                const t = document.elementFromPoint(innerWidth / 2, innerHeight / 2);
                return ((t && window.kronikol_freeTop ? window.kronikol_freeTop(t) : 0) + innerHeight) / 2;
            };
            const movedNow = () => {
                const m = window.__mark, same = textsOf(boxOf(m.i)).filter((t) => t.textContent === m.words);
                const y = same[m.k] ? middleOf(same[m.k]) : null;
                return y === null ? null : y - m.y0;
            };
            window.__k227 = {
                async zoom(i, level) {
                    const c = boxOf(i);
                    c.scrollIntoView({ block: 'center' });
                    for (let n = 0; n < 150 && !c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider'); n++) await frames(2);
                    const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                    if (!s) return null;
                    s.value = level === 'max' ? '100' : String(Math.round((parseInt(s.min) + 100) / 2));
                    s.dispatchEvent(new Event('input', { bubbles: true }));
                    await frames(3);
                    return { level: parseInt(s.value), min: parseInt(s.min), zoomedIn: c.classList.contains('diagram-natural-size'),
                        zoomedOut: c.classList.contains('diagram-zoomed-out') };
                },
                // where: mid (the diagram across the window's middle), top (its top 80 px below the bar), deep:<px> (the
                // window's middle <px> down it), gap (the window's top edge 20 px below the diagram above, in that
                // scenario's foot), cut (the window's top edge 40 px above the bottom of the picture above); placed again
                // until it holds, as the page lays out late around a scroll (content-visibility)
                async place(i, where) {
                    const c = boxOf(i), all = Array.from(document.querySelectorAll('[data-diagram-type="plantuml"]'));
                    const prev = all[all.indexOf(c) - 1] || null, nav = document.querySelector('.toolbar-row.report-nav');
                    c.scrollIntoView({ block: 'center' });
                    await frames(4);
                    const byPrev = where === 'gap' || where === 'cut';
                    if (byPrev && prev) { prev.scrollIntoView({ block: 'end' }); await frames(4); }
                    const off = () => {
                        const r = c.getBoundingClientRect(), bar = nav ? nav.getBoundingClientRect().bottom : 0;
                        if (where === 'top') return r.top - (bar + 80);
                        if (where.startsWith('deep:')) return r.top + parseInt(where.slice(5), 10) - innerHeight / 2;
                        if (where === 'gap') return prev.getBoundingClientRect().bottom + 20;
                        if (where === 'cut') return prev.getBoundingClientRect().bottom - 40;
                        return r.top + Math.min(r.height, innerHeight) / 2 - innerHeight / 2;
                    };
                    const consistent = () => !byPrev || prev.getBoundingClientRect().bottom <= c.getBoundingClientRect().top;
                    // on a short page the window stops at the page's end: as near as it goes
                    const stopped = (d) => d > 0 ? window.scrollY >= document.documentElement.scrollHeight - innerHeight - 1 : window.scrollY <= 1;
                    for (let n = 0; n < 12; n++) {
                        const d = off();
                        if ((Math.abs(d) <= 1 || stopped(d)) && n > 0 && consistent()) return true;
                        window.scrollBy(0, d);
                        await frames(4);
                    }
                    return (Math.abs(off()) <= 1 || stopped(off())) && consistent();
                },
                // the reading line on a scenario's title row, which no diagram draws
                async placeOnScenario(i) {
                    const s = document.querySelectorAll('details.scenario')[i].querySelector(':scope > summary');
                    const off = () => { const r = s.getBoundingClientRect(); return (r.top + r.bottom) / 2 - readingLine(); };
                    for (let n = 0; n < 12; n++) {
                        if (Math.abs(off()) <= 1 && n > 0) return true;
                        window.scrollBy(0, off());
                        await frames(4);
                    }
                    return Math.abs(off()) <= 1;
                },
                mark(i) {
                    const c = boxOf(i), y = readingLine(), texts = textsOf(c);
                    let best = -1, bd = Infinity;
                    texts.forEach((t, j) => { const m = middleOf(t); if (m !== null && Math.abs(m - y) < bd) { bd = Math.abs(m - y); best = j; } });
                    if (best < 0) return null;
                    const words = texts[best].textContent;
                    // also the nearest text whose words the diagram draws once, which no other drawing of the same words can
                    // stand in for when rows go away
                    let once = null, od = Infinity;
                    texts.forEach((t) => {
                        const m = middleOf(t);
                        if (m === null || Math.abs(m - y) >= od || texts.filter((u) => u.textContent === t.textContent).length !== 1) return;
                        od = Math.abs(m - y); once = { words: t.textContent, y0: m, distance: od };
                    });
                    window.__mark = { i, words, k: texts.slice(0, best).filter((t) => t.textContent === words).length, y0: middleOf(texts[best]),
                        once, old: c.querySelector('svg'), top0: c.getBoundingClientRect().top, frames: [] };
                    window.__writes = [];
                    const log = (what) => window.__writes.push({ what, stack: String(new Error().stack || '') });
                    for (const name of ['scrollBy', 'scrollTo', 'scroll']) {
                        const orig = window[name];
                        window[name] = function () { log(name); return orig.apply(this, arguments); };
                    }
                    const st = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollTop');
                    Object.defineProperty(Element.prototype, 'scrollTop', { configurable: true, get() { return st.get.call(this); },
                        set(v) { if (this === document.documentElement || this === document.body) log('scrollTop'); st.set.call(this, v); } });
                    const m = window.__mark;
                    const tick = () => {
                        if (window.__mark !== m) return;
                        m.frames.push({ text: movedNow(), box: c.getBoundingClientRect().top - m.top0 });
                        requestAnimationFrame(tick);
                    };
                    requestAnimationFrame(tick);
                    return { words, distance: bd, line: y };
                },
                result() {
                    const m = window.__mark, c = boxOf(m.i), r = c.getBoundingClientRect(), moved = movedNow();
                    window.__mark = null;
                    const zoomCode = (w) => /\bkeepPlace\b|\bapplyZoomLevel\b/.test(w.stack);
                    const once = m.once ? textsOf(c).filter((t) => t.textContent === m.once.words) : [];
                    return JSON.stringify({ Moved: moved, BoxMoved: r.top - m.top0, OnScreen: r.bottom > 0 && r.top < innerHeight,
                        Redrawn: !document.contains(m.old), Words: m.words,
                        Once: m.once ? m.once.words : null, OnceDistance: m.once ? m.once.distance : null,
                        OnceMoved: once.length === 1 ? middleOf(once[0]) - m.once.y0 : null,
                        ScrollY: window.scrollY, MaxScroll: document.documentElement.scrollHeight - innerHeight,
                        FramesText: m.frames.map((f) => f.text), FramesBox: m.frames.map((f) => f.box),
                        ZoomWrites: window.__writes.filter(zoomCode).map((w) => w.what + ' < ' + w.stack.split('\n').slice(2, 6).map((l) => l.trim()).join(' < ')) });
                }
            };
        }
        """;

    private sealed record Outcome(double? Moved, double BoxMoved, bool OnScreen, bool Redrawn, string Words, string? Once,
        double? OnceDistance, double? OnceMoved, double ScrollY, double MaxScroll, double?[] FramesText, double[] FramesBox,
        string[] ZoomWrites);

    /// <summary>Opens the report with every feature and scenario open and every diagram drawn.</summary>
    private static async Task OpenDrawnAsync(IPage page, string url)
    {
        await page.GotoReportAsync(url, new() { Timeout = PageOpenTimeout });
        await page.Locator("details.feature").First.WaitForAsync();
        await page.EvaluateAsync("""
            () => {
                document.querySelectorAll('details.feature').forEach((f) => { f.open = true; });
                document.querySelectorAll('details.scenario').forEach((s) => {
                    s.open = true;
                    if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(s);
                });
            }
            """);
        await page.WaitForFunctionAsync("""
            () => {
                const boxes = Array.from(document.querySelectorAll('details.scenario [data-diagram-type="plantuml"]'));
                return boxes.length && !window._plantumlRendering && boxes.every((c) => {
                    const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                    return parts.length ? parts.every((p) => p.querySelector(':scope > svg')) : !!c.querySelector(':scope > svg');
                });
            }
            """, null, new() { Timeout = 180_000, PollingInterval = 200 });
        await page.EvaluateAsync(Kit);
    }

    /// <summary>Zooms a diagram (a scenario's, by the scenario's index, or one a selector names) halfway up its slider's range
    /// (<c>in</c>; for a diagram that fits, that zooms it out) or to 100 (<c>max</c>).</summary>
    private static async Task ZoomAsync(IPage page, object box, string level)
    {
        var zoom = await page.EvaluateAsync<JsonElement>("([i, level]) => window.__k227.zoom(i, level)", new[] { box, level });
        Assert.True(zoom.ValueKind == JsonValueKind.Object, $"diagram {box} has a zoom slider");
    }

    private static async Task PlaceAsync(IPage page, object box, string where) =>
        Assert.True(await page.EvaluateAsync<bool>("([i, where]) => window.__k227.place(i, where)", new[] { box, where }),
            $"diagram {box} placed '{where}'");

    /// <summary>The report's own toggle in its bar (not a scenario's).</summary>
    private static ILocator ReportToggle(IPage page, string name) =>
        page.Locator($"button[data-toggle='{name}'][onclick^='window._toggle']:not([onclick*='Scenario']):visible").First;

    /// <summary>
    /// Clicks <paramref name="control"/> with the mouse, the text read remembered with the pointer over it, waits for every
    /// diagram the control draws again and for the page to be still, and returns what moved. The mouse goes to the
    /// control's middle itself: a locator's click scrolls the page to bring the control into view first, a few pixels for
    /// a scenario's settings stuck under the bar, which no reader's click does.
    /// </summary>
    private static async Task<Outcome> UseAsync(IPage page, object box, ILocator control, Func<Task>? meanwhile = null,
        bool readRedrawn = true)
    {
        var at = await control.BoundingBoxAsync();
        Assert.True(at is not null && at.Y >= 0 && at.Y + at.Height <= page.ViewportSize!.Height, "the control is in the window");
        var (x, y) = (at!.X + at.Width / 2, at.Y + at.Height / 2);
        await page.Mouse.MoveAsync(x, y);
        await TwoAnimationFramesAsync(page);
        Assert.NotNull(await page.EvaluateAsync<JsonElement?>("(i) => window.__k227.mark(i)", box));
        await page.Mouse.ClickAsync(x, y);
        if (meanwhile is not null) await meanwhile();
        await SettledAsync(page, readRedrawn);
        return JsonSerializer.Deserialize<Outcome>(await page.EvaluateAsync<string>("() => window.__k227.result()"))!;
    }

    /// <summary>Every diagram drawn: none queued, waiting or being drawn, no control busy, and the diagram read drawn again
    /// unless <paramref name="readRedrawn"/> is false (two clicks that end where they began may leave it as it was); then
    /// the page still for twenty frames.</summary>
    private static async Task SettledAsync(IPage page, bool readRedrawn = true)
    {
        await page.WaitForFunctionAsync("""
            (redrawn) => {
                if (window._plantumlRendering || document.querySelector('.details-pending')) return false;
                const boxes = document.querySelectorAll('[data-diagram-type="plantuml"]');
                for (const c of boxes) if (c._noteRendering || c._kQueued > 0 || c._kBatched > 0) return false;
                return !redrawn || !window.__mark || !document.contains(window.__mark.old);
            }
            """, readRedrawn, new() { Timeout = 120_000, PollingInterval = 200 });
        await page.EvaluateAsync("""
            async () => {
                let last = null, still = 0;
                for (let n = 0; n < 900 && still < 20; n++) {
                    await new Promise((r) => requestAnimationFrame(r));
                    const now = window.scrollY + '|' + document.documentElement.scrollHeight;
                    still = now === last ? still + 1 : 0;
                    last = now;
                }
            }
            """);
    }

    private static void AssertKept(Outcome o, string what, bool redrawn = true)
    {
        if (redrawn) Assert.True(o.Redrawn, $"{what}: the diagram read was drawn again");
        Assert.True(o.Moved is not null, $"{what}: the text read ('{o.Words}') is drawn after the redraw");
        Assert.True(Math.Abs(o.Moved!.Value) <= Kept,
            $"{what}: the text read ('{o.Words}') moved {o.Moved:F1} px (the diagram {o.BoxMoved:F0} px); it may move {Kept} px");
        Assert.True(o.OnScreen, $"{what}: the diagram read is on screen");
        Assert.True(o.ZoomWrites.Length == 0, $"{what}: the zoom code scrolled the page: {string.Join(" | ", o.ZoomWrites)}");
    }

    // T1 (M3): the window's top edge on the picture of the diagram above, which Headers makes 553 px taller. The browser
    // anchors inside that picture, the redraw removes it, and nothing made up for it: the text read moved 650 px down, to
    // the window's foot, in every engine. WebKit (Safari) has no scroll anchoring at all.
    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public Task Headers_with_the_window_edge_on_the_picture_above_keeps_the_text_read_in_place(string engine) =>
        InEngineAsync(engine, async page =>
        {
            await OpenDrawnAsync(page, Tall.Value);
            await PlaceAsync(page, 3, "top");
            AssertKept(await UseAsync(page, 3, ReportToggle(page, "headers")), engine);
        });

    // T2 (M3): the same, the diagram read zoomed in (+664 px).
    [Fact]
    public async Task Headers_with_the_window_edge_on_the_picture_above_keeps_a_zoomed_diagrams_text_in_place()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await ZoomAsync(Page, 3, "in");
        await PlaceAsync(Page, 3, "top");
        AssertKept(await UseAsync(Page, 3, ReportToggle(Page, "headers")), "zoomed in");
    }

    // T3 (M3): the diagram read zoomed in, across the window's middle, and 1,117 px into it: its zoom put back read the
    // reader's place on the new picture at its fit size (-16 and -74 px).
    [Theory]
    [InlineData("mid")]
    [InlineData("deep:1117")]
    public async Task Headers_keeps_the_place_read_in_a_zoomed_diagram(string where)
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await ZoomAsync(Page, 3, "in");
        await PlaceAsync(Page, 3, where);
        AssertKept(await UseAsync(Page, 3, ReportToggle(Page, "headers")), where);
    }

    // T4 (M4): the three diagrams above the one read zoomed in, far above the window, where their scenarios are not laid
    // out (content-visibility): their zoom put back scrolled the page by the 1,026 px it gives back, a growth the page
    // never saw there. The text read went 1,886 px up in Chromium and Firefox, 2,795 px in WebKit, off the screen.
    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public Task Headers_with_zoomed_diagrams_far_above_keeps_the_diagram_read_on_screen(string engine) =>
        InEngineAsync(engine, async page =>
        {
            await OpenDrawnAsync(page, Tall.Value);
            foreach (var above in new[] { 0, 1, 2 }) await ZoomAsync(page, above, "in");
            await PlaceAsync(page, 3, "gap");
            AssertKept(await UseAsync(page, 3, ReportToggle(page, "headers")), engine);
        });

    // T5 (M1): one zoomed diagram on a short page, a toggle that changes nothing on it that shows (its header line is in
    // the note's folded part): the moment at fit size cut the page's scroll, +149 px at 74% and +255 px at 100%.
    [Theory]
    [InlineData("in")]
    [InlineData("max")]
    public async Task Headers_on_a_short_page_leaves_a_zoomed_diagram_where_it_was(string level)
    {
        await OpenDrawnAsync(Page, ReportTestHelper.GenerateReportWithWideLongNoteDiagram(TempDir, OutputDir, $"ReadersPlace_Short_{level}.html",
            noteLines: 30, headerLine: true));
        await ZoomAsync(Page, 0, level);
        await PlaceAsync(Page, 0, "mid");
        AssertKept(await UseAsync(Page, 0, ReportToggle(Page, "headers")), level);
    }

    // T6 (M5): a diagram that fits, zoomed out, and Steps, which adds rows unevenly down it: the text read moved 43 px. A
    // fraction of the way down the diagram is 40 px off here, so this fact holds the place to the text read (Q4).
    [Fact]
    public async Task Steps_keeps_the_place_read_in_a_diagram_zoomed_out()
    {
        await OpenDrawnAsync(Page, DenseReportFixture.GenerateWithFlows(TempDir, OutputDir, "ReadersPlace_Dense.html", features: 3,
            scenariosPerFeature: 6, callsPerScenario: 6, everyFilter: true, internalFlow: false));
        await ZoomAsync(Page, 8, "in");
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelectorAll('details.scenario')[8].querySelector('[data-diagram-type=\"plantuml\"]').classList.contains('diagram-zoomed-out')"),
            "the diagram read fits, and is zoomed out");
        await PlaceAsync(Page, 8, "mid");
        AssertKept(await UseAsync(Page, 8, ReportToggle(Page, "steps")), "zoomed out");
    }

    // T18 (found while executing the plan): Databases hidden takes rows away above and below the reading line, in a
    // diagram whose notes draw the same words again and again (each JSON field's name, each brace). The text nearest the
    // line is such words, and the k-th time they are drawn is another row once rows above have gone: the page was kept
    // 207 px off. The measure is the text nearest the line whose words the diagram draws once, within 100 px of it (23 px
    // here, 54 px with a Linux runner's fonts).
    [Theory]
    [InlineData("databases", "mid")]
    [InlineData("setup", "mid")]
    [InlineData("databases", "deep:300")]
    [InlineData("events", "mid")]
    public async Task A_filter_that_takes_rows_away_keeps_the_place_read_among_notes_of_the_same_words(string filter, string where)
    {
        await OpenDrawnAsync(Page, DenseReportFixture.GenerateWithFlows(TempDir, OutputDir, $"ReadersPlace_Dense_{filter}.html", features: 3,
            scenariosPerFeature: 6, callsPerScenario: 6, everyFilter: true, internalFlow: false));
        await PlaceAsync(Page, 8, where);
        var o = await UseAsync(Page, 8, ReportToggle(Page, filter));
        Assert.True(o.Once is not null && o.OnceDistance <= 100, $"a text the diagram draws once stands near the reading line ({o.Once}, {o.OnceDistance:F0} px)");
        Assert.True(o.OnceMoved is not null && Math.Abs(o.OnceMoved.Value) <= Kept,
            $"{filter}: the text read moved {o.OnceMoved:F1} px ('{o.Once}', {o.OnceDistance:F0} px from the line; the nearest text '{o.Words}' {o.Moved:F1} px)");
        Assert.True(o.ZoomWrites.Length == 0, $"the zoom code scrolled the page: {string.Join(" | ", o.ZoomWrites)}");
    }

    // T7: the reader 2,000 px into a zoomed diagram the page draws in parts (+22 px).
    [Fact]
    public async Task Headers_keeps_the_place_read_in_a_zoomed_diagram_in_parts()
    {
        await OpenDrawnAsync(Page, ReportTestHelper.GenerateReportWithWideDiagramInParts(TempDir, OutputDir, "ReadersPlace_InParts.html",
            headerNotes: true));
        Assert.True(await Page.EvaluateAsync<int>(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"]').querySelectorAll(':scope > .puml-fragment').length") >= 2,
            "the diagram is drawn in parts");
        await ZoomAsync(Page, 0, "in");
        await PlaceAsync(Page, 0, "deep:2000");
        AssertKept(await UseAsync(Page, 0, ReportToggle(Page, "headers")), "in parts");
    }

    // T8: a diagram in parts above the one read, the window's top edge on its last part's picture: the parts swap removes
    // the browser's anchor as the whole one does.
    [Fact]
    public async Task Headers_with_the_window_edge_on_a_diagram_in_parts_above_keeps_the_text_read_in_place()
    {
        await OpenDrawnAsync(Page, PartsAbove.Value);
        Assert.True(await Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('details.scenario')[0].querySelector('[data-diagram-type=\"plantuml\"]').querySelectorAll(':scope > .puml-fragment').length") >= 2,
            "the diagram above is drawn in parts");
        await PlaceAsync(Page, 1, "cut");
        AssertKept(await UseAsync(Page, 1, ReportToggle(Page, "headers")), "parts above");
    }

    // T9: a scenario's own Headers, in its settings, on its zoomed diagram across the window's middle (+15 px).
    [Theory]
    [InlineData("mid")]
    [InlineData("top")]
    public async Task A_scenarios_own_headers_keeps_the_place_read_in_its_zoomed_diagram(string where)
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await ZoomAsync(Page, 3, "in");
        var scenario = Page.Locator("details.scenario").Nth(3);
        await OpenScenarioSettingsAsync(Page, scenario);
        await TwoAnimationFramesAsync(Page);
        await PlaceAsync(Page, 3, where);
        AssertKept(await UseAsync(Page, 3, scenario.Locator(".diagram-options-bar button[data-toggle='headers']:visible").First), where);
    }

    // T15 (M3): the window's top edge in the foot of the scenario above, below its picture, nothing zoomed: the browser
    // keeps that edge, and the diagram read grows above the reading line, so the text read moved 166 px.
    [Fact]
    public async Task Headers_keeps_the_text_at_the_middle_of_the_window_where_the_browser_keeps_the_top()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await PlaceAsync(Page, 3, "gap");
        AssertKept(await UseAsync(Page, 3, ReportToggle(Page, "headers")), "gap");
    }

    // P6: a second click while the first one's diagrams are still being drawn keeps where the reader was before the first:
    // Headers hidden and shown again leaves every diagram as it was, and the text read where it stood. The diagram read
    // may not be drawn again at all: on a slower machine (CI's Linux runner) the second click comes before the first's
    // picture of it, and puts back the drawing it still has.
    [Fact]
    public async Task A_second_toggle_while_the_first_draws_keeps_the_place_read_before_the_first()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await ZoomAsync(Page, 2, "in");
        await PlaceAsync(Page, 3, "top");
        var headers = ReportToggle(Page, "headers");
        AssertKept(await UseAsync(Page, 3, headers, async () =>
        {
            Assert.True(await Page.EvaluateAsync<bool>(
                "() => Array.from(document.querySelectorAll('[data-diagram-type=\"plantuml\"]')).some((c) => c._kQueued > 0 || c._noteRendering)"),
                "the first click's diagrams are still being drawn");
            await headers.ClickAsync();
        }, readRedrawn: false), "two clicks", redrawn: false);
    }

    // T10 (P4): the reader scrolls with the wheel while the toggle's diagrams are drawn: the page goes where the wheel took
    // it and is not pulled back. The diagram read is the first, its top near the window's, so what it grows above the
    // reading line is little beside the wheel's 600 px.
    [Fact]
    public async Task The_readers_wheel_while_a_toggle_draws_lets_the_place_go()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await PlaceAsync(Page, 0, "top");
        var outcome = await UseAsync(Page, 0, ReportToggle(Page, "headers"), async () =>
        {
            Assert.True(await Page.EvaluateAsync<bool>(
                "() => Array.from(document.querySelectorAll('[data-diagram-type=\"plantuml\"]')).some((c) => c._kQueued > 0 || c._noteRendering)"),
                "the toggle's diagrams are still being drawn");
            await Page.Mouse.MoveAsync(ViewportWidth / 2, ViewportHeight / 2);
            await Page.Mouse.WheelAsync(0, 600);
        });
        Assert.True(outcome.Moved is not null && outcome.Moved < -400,
            $"the text read went up the window with the wheel: it moved {outcome.Moved:F0} px");
    }

    // T13 (M11): a note's own fold on a zoomed diagram on a short page. Its zoom put back after the redraw scrolled the page
    // by a place read on the picture at its fit size, and one frame showed the diagram 239 px off before the note's own
    // rule brought it back 30 ms later. No frame may show it further off than where it ends, which the page's end decides.
    [Fact]
    public async Task A_notes_own_fold_on_a_zoomed_diagram_paints_no_frame_off_its_place()
    {
        await OpenDrawnAsync(Page, ReportTestHelper.GenerateReportWithWideLongNoteDiagram(TempDir, OutputDir, "ReadersPlace_NoteFold.html",
            noteLines: 30, headerLine: true));
        await ZoomAsync(Page, 0, "in");
        await PlaceAsync(Page, 0, "mid");
        await Page.EvaluateAsync("""
            async () => {
                const c = document.querySelector('[data-diagram-type="plantuml"]');
                c.querySelector('svg').dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
                for (let n = 0; n < 50 && !c.querySelector('.note-hover-rect'); n++) await new Promise((r) => setTimeout(r, 100));
            }
            """);
        Assert.NotNull(await Page.EvaluateAsync<JsonElement?>("() => window.__k227.mark(0)"));
        Assert.True(await Page.EvaluateAsync<bool>("""
            () => {
                const c = document.querySelector('[data-diagram-type="plantuml"]');
                let best = null, bd = Infinity;
                for (const h of c.querySelectorAll('.note-hover-rect')) {
                    const r = h.getBoundingClientRect();
                    if (!r.height || r.top < 0 || r.bottom > innerHeight) continue;
                    const d = Math.abs((r.top + r.bottom) / 2 - innerHeight / 2);
                    if (d < bd) { bd = d; best = h; }
                }
                if (!best) return false;
                const q = best.getBoundingClientRect();
                best.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, clientX: q.left + q.width / 2, clientY: q.top + q.height * 0.75 }));
                return true;
            }
            """), "a note's hover area is on screen");
        await SettledAsync(Page);
        var o = JsonSerializer.Deserialize<Outcome>(await Page.EvaluateAsync<string>("() => window.__k227.result()"))!;
        Assert.True(o.Redrawn, "the note's fold drew the diagram again");
        // every frame between where the diagram stood and where it ends
        var low = Math.Min(0, o.BoxMoved) - Kept;
        var high = Math.Max(0, o.BoxMoved) + Kept;
        Assert.True(o.FramesBox.All(f => f >= low && f <= high),
            $"a frame showed the diagram off the way from where it stood to where it ends ({o.BoxMoved:F0} px): {string.Join(", ", o.FramesBox.Select(f => f.ToString("F0")))}");
        Assert.True(o.ZoomWrites.Length == 0, $"the zoom code scrolled the page: {string.Join(" | ", o.ZoomWrites)}");
    }

    // T17: the diagram API's show, which the search's way to a match calls before it scrolls to the match, takes no place:
    // the search scrolls once the redraw is done, and nothing may hold the page meanwhile. A scenario's own control goes
    // through show too, and takes its place before (renderWithPending and the scenario's controls take it, never show).
    [Fact]
    public async Task The_diagram_apis_show_takes_no_place_for_the_search_to_keep()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await PlaceAsync(Page, 3, "top");
        var held = await Page.EvaluateAsync<bool>("""
            () => {
                const scenario = document.querySelectorAll('details.scenario')[3];
                window.__shown = window.kronikol_diagrams.show(scenario, { headers: true });
                return !!(window.kronikol_readingPlace && window.kronikol_readingPlace.held());
            }
            """);
        Assert.False(held, "show took the reader's place");
        Assert.NotEmpty(await Page.EvaluateAsync<string[]>("() => window.__shown"));
        Assert.False(await Page.EvaluateAsync<bool>("() => !!(window.kronikol_readingPlace && window.kronikol_readingPlace.held())"),
            "a place is held once show's redraw is done");
    }

    // Review 1: a text that reads as a property every object has ("constructor", "toString"), as a label's line of its own
    // does (the engine draws each line, and each run of markup in it, as a text), was looked up among the drawn words in a
    // plain object: the keeper threw after the first picture written, the redraw queue stopped with its controls busy,
    // and the place was never let go.
    [Fact]
    public async Task Headers_keeps_the_place_read_in_a_diagram_that_draws_the_words_every_object_has()
    {
        await OpenDrawnAsync(Page, ReportTestHelper.GenerateReportWithTallWideDiagrams(TempDir, OutputDir, "ReadersPlace_ObjectWords.html",
            scenarios: 5, labelWords: @"\nconstructor\ntoString\nvalueOf\nhasOwnProperty"));
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('details.scenario')[3].querySelectorAll('svg text')).some((t) => t.textContent === 'constructor')"),
            "the diagram read draws 'constructor' as a text of its own");
        await PlaceAsync(Page, 3, "top");
        AssertKept(await UseAsync(Page, 3, ReportToggle(Page, "headers")), "object words");
    }

    // Review 2: the place on what no diagram draws (a scenario's title row), taken out of the layout while the toggle's
    // diagrams are drawn, as an outline's row switch hides the row it leaves: it read its top as 0 then, and was put back
    // from there after every picture written, the page running away by half a window each time.
    [Fact]
    public async Task A_place_taken_out_of_the_layout_while_a_toggle_draws_scrolls_the_page_no_more()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__k227.placeOnScenario(4)"), "scenario 5's title on the reading line");
        await UseAsync(Page, 3, ReportToggle(Page, "headers"), () => Page.EvaluateAsync("""
            () => {
                window.__hiddenAt = window.__writes.length;
                document.querySelectorAll('details.scenario')[4].style.display = 'none';
            }
            """));
        var writes = await Page.EvaluateAsync<int>(
            "() => window.__writes.slice(window.__hiddenAt).filter((w) => /keepReadingPlace/.test(w.stack)).length");
        Assert.True(writes == 0, $"the keeper scrolled the page {writes} times once its place had left the layout");
    }

    // Review 3: a jump of the page (a link) while the first toggle's diagrams are drawn, and a second toggle before the
    // next picture is written: the second kept the place the first held, which the next picture then let go as the window
    // had moved, and the second toggle's diagrams moved the text read. The jump, the text read marked and the second click
    // are one task here, right after the first click's picture of the diagram read is written (so that the second draws
    // it again) and before the next, as a reader's can fall between two pictures.
    [Fact]
    public async Task A_second_toggle_after_the_page_jumped_keeps_the_place_read_at_the_second()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await PlaceAsync(Page, 3, "top");
        await Page.EvaluateAsync("""
            () => {
                const c = document.querySelectorAll('details.scenario')[3].querySelector('[data-diagram-type="plantuml"]');
                const old = c.querySelector(':scope > svg');
                const watch = new MutationObserver(() => {
                    if (document.contains(old)) return;
                    watch.disconnect();
                    window.__queued = Array.from(document.querySelectorAll('[data-diagram-type="plantuml"]')).some((d) => d._kQueued > 0);
                    window.scrollBy(0, 300);
                    window.__marked = !!window.__k227.mark(3);
                    Array.from(document.querySelectorAll("button[data-toggle='headers'][onclick^='window._toggle']:not([onclick*='Scenario'])"))
                        .find((b) => b.offsetParent !== null).click();
                });
                watch.observe(c, { childList: true });
            }
            """);
        var at = await ReportToggle(Page, "headers").BoundingBoxAsync();
        await Page.Mouse.ClickAsync(at!.X + at.Width / 2, at.Y + at.Height / 2);
        await Page.WaitForFunctionAsync("() => window.__marked !== undefined", null, new() { Timeout = 60_000, PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__marked && window.__queued"),
            "the second click came while the first click's diagrams were drawn, the text read marked");
        await SettledAsync(Page);
        AssertKept(JsonSerializer.Deserialize<Outcome>(await Page.EvaluateAsync<string>("() => window.__k227.result()"))!, "after a jump");
    }

    // Review 4: a diagram zoomed out after the window changed width, which forgets the width it was zoomed at: its new
    // picture was written at full size, and the zoom given back after the redraw scrolled the page.
    [Fact]
    public async Task Steps_keeps_the_place_read_in_a_diagram_zoomed_out_after_the_window_changed_width()
    {
        await OpenDrawnAsync(Page, DenseReportFixture.GenerateWithFlows(TempDir, OutputDir, "ReadersPlace_DenseResized.html", features: 3,
            scenariosPerFeature: 6, callsPerScenario: 6, everyFilter: true, internalFlow: false));
        await ZoomAsync(Page, 8, "in");
        foreach (var width in new[] { ViewportWidth - 40, ViewportWidth })
        {
            await Page.SetViewportSizeAsync(width, ViewportHeight);
            // the zoom code takes a width change 150 ms after the last
            await Page.EvaluateAsync("() => new Promise((r) => setTimeout(r, 400))");
        }
        Assert.True(await Page.EvaluateAsync<bool>("""
            () => {
                const c = document.querySelectorAll('details.scenario')[8].querySelector('[data-diagram-type="plantuml"]');
                return c.classList.contains('diagram-zoomed-out') && !c._kContentWidth;
            }
            """), "the diagram read is zoomed out, the width it was zoomed at forgotten");
        await PlaceAsync(Page, 8, "mid");
        AssertKept(await UseAsync(Page, 8, ReportToggle(Page, "steps")), "zoomed out, width changed");
    }

    /// <summary>
    /// Marks the text read in a scenario's diagram, clicks the report's Headers and dispatches <paramref name="then"/> (a
    /// script given the diagram's picture as <c>svg</c>) on it, all in one task, before the first picture is written: a
    /// reader's wheel or touch can come at once, and one dispatched a moment later found the nearest diagrams drawn
    /// already. Then waits for the page to settle and returns what moved.
    /// </summary>
    private static async Task<Outcome> HeadersThenAsync(IPage page, int box, string then)
    {
        Assert.True(await page.EvaluateAsync<bool>($$"""
            (i) => {
                if (!window.__k227.mark(i)) return false;
                Array.from(document.querySelectorAll("button[data-toggle='headers'][onclick^='window._toggle']:not([onclick*='Scenario'])"))
                    .find((b) => b.offsetParent !== null).click();
                const svg = document.querySelectorAll('details.scenario')[i].querySelector('[data-diagram-type="plantuml"] > svg');
                {{then}}
                return true;
            }
            """, box), "the text read marked");
        await SettledAsync(page);
        return JsonSerializer.Deserialize<Outcome>(await page.EvaluateAsync<string>("() => window.__k227.result()"))!;
    }

    // Review 6: the reader's wheel across a zoomed diagram while the toggle's diagrams are drawn scrolls the diagram, not
    // the window: it let the place go, and the diagram above grew the text read off its place.
    [Fact]
    public async Task A_wheel_across_a_zoomed_diagram_while_a_toggle_draws_keeps_the_place_read()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await ZoomAsync(Page, 3, "in");
        await PlaceAsync(Page, 3, "top");
        AssertKept(await HeadersThenAsync(Page, 3,
            "svg.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaX: 300, deltaY: 0, clientX: innerWidth / 2, clientY: innerHeight / 2 }));"),
            "a wheel across");
    }

    // Review 6: a tap while the toggle's diagrams are drawn scrolls nothing and lets nothing go; a finger that moves does.
    [Fact]
    public async Task A_tap_while_a_toggle_draws_keeps_the_place_read()
    {
        await using var context = await OpenContextAsync(Page.Context.Browser!,
            new() { HasTouch = true, ViewportSize = new() { Width = ViewportWidth, Height = ViewportHeight } });
        var page = await OpenPageAsync(context);
        await OpenDrawnAsync(page, Tall.Value);
        await PlaceAsync(page, 3, "top");
        AssertKept(await HeadersThenAsync(page, 3,
            "for (const type of ['touchstart', 'touchend']) svg.dispatchEvent(new TouchEvent(type, { bubbles: true, cancelable: true }));"),
            "a tap");
    }

    // Review 7: a report's CSS that scrolls the page smoothly (CustomCss) animated every scroll the keeper wrote: the page
    // was painted off its place while the animation ran, and the next picture's check took it for someone else's scroll.
    // No frame may show the text read off its place.
    [Fact]
    public async Task Headers_keeps_the_text_read_in_place_on_a_page_that_scrolls_smoothly()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await PlaceAsync(Page, 3, "top");
        await Page.AddStyleTagAsync(new() { Content = "html { scroll-behavior: smooth; }" });
        var o = await UseAsync(Page, 3, ReportToggle(Page, "headers"));
        AssertKept(o, "smooth");
        var off = o.FramesText.Where(f => f is not null && Math.Abs(f.Value) > Kept).ToArray();
        Assert.True(off.Length == 0, $"{off.Length} frames showed the text read off its place: {string.Join(", ", off.Select(f => f!.Value.ToString("F0")))}");
    }

    // Review 9: the browser's own anchoring scrolls the window with what it keeps in place (a card laid out late above),
    // which leaves the place on its line: the keeper took that for someone else's scroll and let the place go, and the
    // diagram above grew the text read off its place. Here a block is put above the diagram read and the window scrolled
    // by its height, as the browser does, while the toggle's diagrams are drawn.
    [Fact]
    public async Task A_scroll_that_leaves_the_place_on_its_line_lets_nothing_go()
    {
        await OpenDrawnAsync(Page, Tall.Value);
        await PlaceAsync(Page, 3, "top");
        await Page.EvaluateAsync("""
            () => {
                const c = document.querySelectorAll('details.scenario')[3].querySelector('[data-diagram-type="plantuml"]');
                const watch = new MutationObserver(() => {
                    watch.disconnect();
                    const block = document.createElement('div');
                    block.style.height = '300px';
                    c.parentNode.insertBefore(block, c);
                    window.scrollBy(0, 300);
                });
                watch.observe(c, { childList: true });
            }
            """);
        AssertKept(await UseAsync(Page, 3, ReportToggle(Page, "headers")), "anchoring");
    }

    // T16 (F9): the Components tab's Call summaries on a zoomed component diagram taller than the window: its own redraw
    // put the zoom back a frame after its swap, the same way. The summaries are hidden first and then shown, so the text
    // read is a component's name, which both drawings have (hidden, a summary read is gone).
    [Fact]
    public async Task Call_summaries_keeps_the_place_read_in_a_zoomed_component_diagram()
    {
        var box = await OpenZoomedComponentsAsync("ReadersPlace_Components.html");
        AssertKept(await UseAsync(Page, box, Page.Locator("#component-diagram .tl-seg-btn[data-calls='shown']")), "components");
    }

    // Review 10: a scroll of the page's own between the Components tab's new picture and its slider a frame later (Back's
    // settle) stands: the keeper put the place back in that frame without asking whether the window had moved since.
    [Fact]
    public async Task A_scroll_between_the_component_diagrams_picture_and_its_slider_stands()
    {
        var box = await OpenZoomedComponentsAsync("ReadersPlace_ComponentsScrolled.html");
        await Page.EvaluateAsync($$"""
            () => {
                const c = document.querySelector('{{box}}');
                const watch = new MutationObserver((records) => {
                    if (!records.some((r) => Array.from(r.removedNodes).some((n) => n.nodeName.toLowerCase() === 'svg'))) return;
                    watch.disconnect();
                    const was = window.scrollY;
                    window.scrollBy(0, 400);
                    window.__jump = window.scrollY - was;
                });
                watch.observe(c, { childList: true });
            }
            """);
        var o = await UseAsync(Page, box, Page.Locator("#component-diagram .tl-seg-btn[data-calls='shown']"));
        var jump = await Page.EvaluateAsync<double>("() => window.__jump || 0");
        Assert.True(jump > 100, $"the page scrolled between the picture and its slider ({jump:F0} px)");
        Assert.True(o.Moved is not null && Math.Abs(o.Moved.Value + jump) <= Kept,
            $"the text read moved {o.Moved:F1} px; the page's own scroll took it {-jump:F0} px");
    }

    /// <summary>The Components tab's diagram with its call summaries hidden, zoomed in, taller than the window, standing
    /// across the window below the tab's controls, which are just below the bar where a reader clicks them.</summary>
    private async Task<string> OpenZoomedComponentsAsync(string fileName)
    {
        await Page.GotoReportAsync(WriteComponents(fileName), new() { Timeout = PageOpenTimeout });
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenReportTabAsync("components");
        var box = "#component-diagram .plantuml-browser";
        var drawn = $"() => !!document.querySelector('{box} > svg') && !!document.querySelector('{box} > .diagram-zoom-controls')";
        await Page.WaitForFunctionAsync(drawn, null, new() { Timeout = 60000, PollingInterval = 200 });
        await Page.EvaluateAsync($"() => {{ window.__first = document.querySelector('{box} > svg'); }}");
        await Page.Locator("#component-diagram .tl-seg-btn[data-calls='hidden']").ClickAsync();
        await Page.WaitForFunctionAsync($"() => !document.contains(window.__first) && ({drawn})()", null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync(Kit);
        await ZoomAsync(Page, box, "in");
        Assert.True(await Page.EvaluateAsync<bool>($"() => document.querySelector('{box}').classList.contains('diagram-natural-size')"),
            "the component diagram is wider than the page, and zoomed in");
        var tall = await Page.EvaluateAsync<double>($"() => document.querySelector('{box}').getBoundingClientRect().height");
        Assert.True(tall > ViewportHeight, $"the component diagram zoomed in is taller than the window ({tall:F0} px)");
        // the tab's controls just below the bar, where a reader clicks them, and the diagram below them across the window
        Assert.True(await Page.EvaluateAsync<bool>($$"""
            async () => {
                const nav = document.querySelector('.toolbar-row.report-nav'), ctl = document.querySelector('#component-diagram .timeline-controls');
                for (let n = 0; n < 6; n++) {
                    window.scrollBy(0, ctl.getBoundingClientRect().top - (nav ? nav.getBoundingClientRect().bottom : 0) - 8);
                    await new Promise((r) => requestAnimationFrame(() => setTimeout(r, 0)));
                }
                const r = document.querySelector('{{box}} > svg').getBoundingClientRect();
                return r.top < innerHeight / 2 && r.bottom > innerHeight;
            }
            """), "the diagram stands across the window below its controls");
        return box;
    }

    // T16 (P1 for the Components tab's controls): Direction draws the diagram another way, so its top stays where the
    // browser keeps it and what the reader read moves within it; the page keeps the component read at the reading line,
    // which a fraction of the way down the diagram could not (section 4.5). The summaries are hidden, so the text read is
    // a component's, drawn in both directions.
    [Fact]
    public async Task Direction_keeps_the_component_read_in_place()
    {
        await Page.GotoReportAsync(WriteComponents("ReadersPlace_ComponentsDirection.html", services: 8, levels: 10), new() { Timeout = PageOpenTimeout });
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenReportTabAsync("components");
        var box = "#component-diagram .plantuml-browser";
        var drawn = $"() => !!document.querySelector('{box} > svg') && !!document.querySelector('{box} > .diagram-zoom-controls')";
        await Page.WaitForFunctionAsync(drawn, null, new() { Timeout = 60000, PollingInterval = 200 });
        await Page.EvaluateAsync($"() => {{ window.__first = document.querySelector('{box} > svg'); }}");
        await Page.Locator("#component-diagram .tl-seg-btn[data-calls='hidden']").ClickAsync();
        await Page.WaitForFunctionAsync($"() => !document.contains(window.__first) && ({drawn})()", null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync(Kit);
        Assert.True(await Page.EvaluateAsync<bool>($$"""
            async () => {
                const nav = document.querySelector('.toolbar-row.report-nav'), ctl = document.querySelector('#component-diagram .timeline-controls');
                for (let n = 0; n < 6; n++) {
                    window.scrollBy(0, ctl.getBoundingClientRect().top - (nav ? nav.getBoundingClientRect().bottom : 0) - 8);
                    await new Promise((r) => requestAnimationFrame(() => setTimeout(r, 0)));
                }
                const r = document.querySelector('{{box}} > svg').getBoundingClientRect();
                return r.top < innerHeight / 2 && r.bottom > innerHeight / 2;
            }
            """), "the diagram stands across the reading line below its controls");
        var o = await UseAsync(Page, box, Page.Locator("#component-diagram .tl-seg-btn[data-dir='tb']"));
        // the page at neither end, so no scroll the keeper wrote was cut short (a Linux runner's fonts draw it 351 px
        // taller than the window, the text read kept 4 px down the page)
        Assert.True(o.ScrollY > 0 && o.ScrollY < o.MaxScroll,
            $"the page drawn top to bottom scrolls either way from where the text read stands (scroll {o.ScrollY:F0} of {o.MaxScroll:F0})");
        AssertKept(o, "direction");
    }

    /// <summary>
    /// A run report written by the whole pipeline whose component diagram is wider than the page and taller than the
    /// window: one scenario calls <paramref name="services"/> services from the shop, and each of them a chain of
    /// <paramref name="levels"/> less one more, each call with its own summary on the connection. Twenty chains of six by
    /// default; eight chains of ten are taller than the window drawn top to bottom too.
    /// </summary>
    private string WriteComponents(string fileName, int services = 20, int levels = 6)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var reportsDir = Path.Combine(TempDir, "components-place-" + tag);
        Directory.CreateDirectory(reportsDir);
        var id = "place-" + tag;
        var shop = "Shop" + tag;
        var names = new HashSet<string> { shop };
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            for (var s = 1; s <= services; s++)
            {
                var caller = shop;
                for (var d = 0; d < levels; d++)
                {
                    var callee = $"Service{s}Level{d}x{tag}";
                    names.Add(callee);
                    RequestResponseLogger.LogPair("Builds", id, HttpMethod.Post, new Uri($"http://service{s}level{d}/orders/build/{s}/{d}"), callee,
                        caller, statusCode: HttpStatusCode.OK);
                    caller = callee;
                }
            }
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature { DisplayName = "Shop", Scenarios = [new Scenario { Id = id, DisplayName = "Builds", Result = ExecutionResult.Passed }] }],
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
}
