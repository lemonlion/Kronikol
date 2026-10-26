using System.Text.Json;
using Xunit;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// One scale for a diagram drawn in parts, while it is drawn and while it zooms (<c>plans/ZOOM_EVERY_DIAGRAM_PLAN.md</c>
/// Z8, facts 16 and 17 of section 6.2): a part wider than those above it lowers the diagram's level once, keeping the
/// reader's place, and a notch resizes the parts near the screen at once and the rest in the frames after.
/// </summary>
[Collection(PlaywrightCollections.Zoom)]
public class PartsZoomDrawingTests : PlaywrightTestBase
{
    private readonly ITestOutputHelper _output;

    public PartsZoomDrawingTests(PlaywrightFixture fixture, ITestOutputHelper output) : base(fixture) { _output = output; }

    // 16. The page lines a diagram's parts up from an outline drawn first, so its parts are as wide as each other from
    // their first drawing (R2); the test turns that off to stand for a diagram whose outline fell short. The second part
    // is held back until the test lets it go, once the reader is on the first and its width watched: held for a fixed
    // 1.5 s, it came before the watching began whenever the first part took longer than that to draw, on a busy machine.
    [Fact]
    public async Task A_first_drawing_whose_second_part_is_wider_lowers_the_first_parts_level_once_and_keeps_the_readers_place()
    {
        await Page.AddInitScriptAsync("""
            (() => {
                window.__kronikolLineUp = false;
                let engine;
                Object.defineProperty(window, 'plantuml', {
                    configurable: true,
                    get() { return engine; },
                    set(v) {
                        if (v && typeof v.render === 'function' && !v.__held) {
                            const render = v.render.bind(v);
                            v.render = function (lines, id, opts) {
                                if (/-frag-1$/.test(id)) { window.__releasePart = () => render(lines, id, opts); return; }
                                return render(lines, id, opts);
                            };
                            v.__held = true;
                        }
                        engine = v;
                    }
                });
            })();
            """);
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "PartsLevelDrop.html",
            ReportTestHelper.PartsSource(20, new Dictionary<int, int> { [15] = 260 })));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");

        // the first part drawn, and the reader on it
        await Page.WaitForFunctionAsync("""
            () => {
                const p = document.querySelector("[data-diagram-type='plantuml'] > .puml-fragment > svg");
                return !!p && p.getBoundingClientRect().width > 0;
            }
            """, null, new() { Timeout = 30000, PollingInterval = 200 });
        await Page.EvaluateAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const first = c.querySelector(':scope > .puml-fragment > svg');
                const r = first.getBoundingClientRect();
                window.scrollBy(0, r.top + r.height / 2 - innerHeight / 2);
                window.__widths = [];
                new ResizeObserver(es => es.forEach(e => window.__widths.push(Math.round(e.contentRect.width * 10) / 10))).observe(first);
            }
            """);
        await TwoAnimationFramesAsync(Page);
        var at = await Page.EvaluateAsync<float[]>("""
            () => {
                const r = document.querySelector("[data-diagram-type='plantuml'] > .puml-fragment > svg").getBoundingClientRect();
                return [r.left + r.width / 2, innerHeight / 2];
            }
            """);
        await ZoomFacts.MarkPointAsync(Page, at[0], at[1]);

        // the second part comes now, its width watched and the reader on the first (the diagram's slider waits for it)
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => document.querySelectorAll(\"[data-diagram-type='plantuml'] > .puml-fragment > svg\").length"));
        await Page.WaitForFunctionAsync("() => typeof window.__releasePart === 'function'", null, new() { Timeout = 30000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => window.__releasePart()");
        await ZoomFacts.WaitForSliderAsync(Page, 0);
        await Page.WaitForFunctionAsync("() => document.querySelectorAll(\"[data-diagram-type='plantuml'] > .puml-fragment > svg\").length === 2",
            null, new() { Timeout = 30000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        await TwoAnimationFramesAsync(Page);

        var d = await ZoomFacts.ReadAsync(Page, 0);
        Assert.True(d.Min < 100, $"the second part is wide: the diagram's slider runs from {d.Min}");
        Assert.True(d.Levels.Max() - d.Levels.Min() <= 0.5, $"both parts at one level: {string.Join(", ", d.Levels.Select(l => l.ToString("F1")))}");
        var widths = await Page.EvaluateAsync<double[]>("() => window.__widths");
        var drops = widths.Zip(widths.Skip(1), (a, b) => b < a - 0.5).Count(x => x);
        Assert.True(drops == 1, $"the first part shrinks once: its widths were {string.Join(", ", widths)}");
        var (_, down) = await ZoomFacts.PointMovedAsync(Page);
        Assert.True(Math.Abs(down) <= 2, $"the window keeps the reader's place: the point moved {down:F1} px");
    }

    // 17, the reader's place: a notch far down a diagram in parts resizes the parts far above the window one a frame
    // after, and the point under the pointer stays where it was through every one of them
    [Fact]
    public async Task A_notch_far_down_a_diagram_in_parts_keeps_the_point_under_the_pointer_while_the_parts_above_grow()
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "PartsFarAbove.html",
            ReportTestHelper.PartsSource(160, new Dictionary<int, int> { [2] = 180 })));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await ZoomFacts.WaitForSliderAsync(Page, 0, timeoutMs: 120000);
        // every part drawn and lined up
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return !window._plantumlRendering && !c._lineUpPending && parts.length > 1 && parts.every(p => p.querySelector(':scope > svg'));
            }
            """, null, new() { Timeout = 120000, PollingInterval = 200 });
        var parts = (await ZoomFacts.ReadAsync(Page, 0)).Levels.Length;
        Assert.True(parts >= 6, $"the diagram is drawn in {parts} parts");
        await ZoomFacts.ScrollToPartAsync(Page, parts - 2);
        var farAbove = await Page.EvaluateAsync<int>("""
            () => Array.from(document.querySelectorAll("[data-diagram-type='plantuml'] > .puml-fragment"))
                .filter(p => p.getBoundingClientRect().bottom < -window.innerHeight).length
            """);
        Assert.True(farAbove >= 2, $"premise: {farAbove} parts are more than a window's height above the window");
        var (x, y) = await ZoomFacts.PointInAsync(Page, 0, part: parts - 2);
        await ZoomFacts.MarkPointAsync(Page, x, y);

        await ZoomFacts.CtrlWheelAsync(Page, x, y, 1);
        var (nearAcross, nearDown) = await ZoomFacts.PointMovedAsync(Page);
        var d = await ZoomFacts.ReadAsync(Page, 0);
        // one level past the fit, the picture is a few pixels wider than the box's content: the box scrolls over its
        // padding all the same, or the point moved 6.1 px across with nothing to scroll
        Assert.True(d.Slider == d.Min + 1, $"premise: the notch zooms one level past the fit level: {d.Min} -> {d.Slider}");
        await Page.WaitForFunctionAsync("() => !document.querySelector(\"[data-diagram-type='plantuml']\")._zoomPending",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        var (across, down) = await ZoomFacts.PointMovedAsync(Page);
        _output.WriteLine($"moved after the near parts: {nearAcross:F1} across, {nearDown:F1} down; after every part: {across:F1}, {down:F1}");
        Assert.True(Math.Abs(down) <= 2, $"once every part was at the new level, the point under the pointer had moved {down:F1} px down the page");
        Assert.True(Math.Abs(across) <= 2, $"once every part was at the new level, the point under the pointer had moved {across:F1} px across");
    }

    // 17, at once: the parts on screen are resized in the notch's own task, before anything is painted, and only the far
    // ones wait a frame each; resized one a frame, a part beside the one under the pointer showed the old level a frame
    // or more after it
    [Fact]
    public async Task A_notch_resizes_every_part_on_screen_in_its_own_task_not_only_the_part_under_the_pointer()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "PartsOnScreenAtOnce.html",
            ReportTestHelper.PartsSource(40, new Dictionary<int, int> { [2] = 180 }), partHeight: 500));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await ZoomFacts.WaitForSliderAsync(Page, 0, timeoutMs: 120000);
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return !window._plantumlRendering && !c._lineUpPending && !c._zoomPending && parts.length > 1 && parts.every(p => p.querySelector(':scope > svg'));
            }
            """, null, new() { Timeout = 120000, PollingInterval = 200 });
        var parts = (await ZoomFacts.ReadAsync(Page, 0)).Levels.Length;
        Assert.True(parts >= 4, $"the diagram is drawn in {parts} parts");
        var (x, y) = await ZoomFacts.PointInAsync(Page, 0, part: 1);

        var r = await Page.EvaluateAsync<JsonElement>("""
            ([x, y]) => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                const before = parseInt(s.value);
                const target = document.elementFromPoint(x, y);
                const under = target.closest('.puml-fragment').querySelector(':scope > svg');
                const onScreen = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'))
                    .filter(p => { const b = p.getBoundingClientRect(); return b.bottom > 0 && b.top < innerHeight; });
                target.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, ctrlKey: true, deltaY: -100, clientX: x, clientY: y }));
                return { before, level: parseInt(s.value), under: onScreen.indexOf(under),
                    levels: onScreen.map(p => p.getBoundingClientRect().width / p.width.baseVal.value * 100) };
            }
            """, new object[] { (double)x, (double)y });
        var before = r.GetProperty("before").GetInt32();
        var level = r.GetProperty("level").GetInt32();
        var levels = r.GetProperty("levels").EnumerateArray().Select(l => l.GetDouble()).ToArray();
        var shown = string.Join(", ", levels.Select(l => l.ToString("F2")));
        Assert.True(level > before, $"premise: the notch zooms in, {before} -> {level}");
        Assert.True(levels.Length >= 2 && r.GetProperty("under").GetInt32() >= 0,
            $"premise: the part under the pointer and another are on screen ({levels.Length} on screen)");
        Assert.True(levels.All(l => Math.Abs(l - level) <= 0.6), $"in the notch's own task the parts on screen were at {shown}, not {level}");
    }

    // A redraw makes the slider of a long diagram in parts again at the level its parts are drawn at: the parts far from
    // the window keep their size, so none is resized, none holds the browser's scroll
    // anchoring off or scrolls the page (SplitDiagramZoomTests has the near parts)
    [Fact]
    public async Task A_slider_made_again_on_a_long_diagram_in_parts_reads_no_far_part_and_leaves_the_scroll_alone()
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "PartsFarAnchoring.html",
            ReportTestHelper.PartsSource(160, new Dictionary<int, int> { [2] = 180 })));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await ZoomFacts.WaitForSliderAsync(Page, 0, timeoutMs: 120000);
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return !window._plantumlRendering && !c._lineUpPending && !c._zoomPending && parts.length > 1 && parts.every(p => p.querySelector(':scope > svg'));
            }
            """, null, new() { Timeout = 120000, PollingInterval = 200 });
        var parts = (await ZoomFacts.ReadAsync(Page, 0)).Levels.Length;
        await ZoomFacts.ScrollToPartAsync(Page, parts - 2);

        // and as none of them changes, none of them is laid out to find that out: read one a frame, the far parts cost
        // 181 ms of layout on every toggle of a long report (5.7.3)
        var r = await Page.EvaluateAsync<string>("""
            () => new Promise(done => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const y = scrollY, h = innerHeight;
                const far = new Set(Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'))
                    .filter(p => { const b = p.parentElement.getBoundingClientRect(); return b.bottom < -h || b.top > 2 * h; }));
                const read = Element.prototype.getBoundingClientRect;
                let farReads = 0;
                Element.prototype.getBoundingClientRect = function() { if (far.has(this)) farReads++; return read.call(this); };
                let held = 0, frames = 0;
                window._addZoomButton(c);
                const watch = () => {
                    if (document.documentElement.style.overflowAnchor === 'none') held++;
                    if (++frames < Math.max(30, far.size + 10)) requestAnimationFrame(watch);
                    else { Element.prototype.getBoundingClientRect = read; done((far.size > 2) + '|' + farReads + '|' + held + '|' + (scrollY - y)); }
                };
                watch();
            })
            """);
        Assert.Equal("true|0|0|0", r);
    }

    // 17. Fifteen parts as wide and long as the consumer report's longest diagram (M6: 1,750 x 112,940 px): resizing every
    // part at once cost 191 ms there, so a notch resizes the parts near the screen at once and the rest after. The notch
    // is held to the plan's 50 ms, or to what the browser itself takes to draw the part under the pointer one level
    // larger, past its box as the notch draws it, measured here, with a quarter more and 10 ms for the page's own work.
    // On that diagram the browser took 33 to 42 ms for a part on its own (about 4,400 elements: its layout, pre-paint,
    // paint and, past its box, a new scroll layer), the same whether resized by its width or scaled by a transform
    // (5.7.0, plans/ZOOM_EVERY_DIAGRAM_PLAN.md log).
    [Fact]
    public async Task A_notch_on_a_diagram_of_fifteen_parts_or_more_paints_quickly_and_every_part_reaches_the_new_level_within_its_budget()
    {
        await Page.AddInitScriptAsync(BrowserRenderWorkerTests.BenchInitWithProbe);
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "PartsNotchBudget.html",
            ReportTestHelper.PartsSource(735, new Dictionary<int, int> { [2] = 180 }, replyNotes: true), partHeight: 12000));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await ZoomFacts.WaitForSliderAsync(Page, 0, timeoutMs: 180000);
        var parts = (await ZoomFacts.ReadAsync(Page, 0)).Levels.Length;
        Assert.True(parts >= 15, $"the diagram is drawn in {parts} parts");
        var (x, y) = await ZoomFacts.PointInAsync(Page, 0, part: 7);
        await Page.WaitForTimeoutAsync(1000);

        // the browser's own cost: the part under the pointer drawn one level larger, past its box, to the frame after, and back
        var alone = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            alone.Add(await Page.EvaluateAsync<double>("""
                ([x, y]) => new Promise(done => {
                    const svg = document.elementFromPoint(x, y).closest('.puml-fragment').querySelector(':scope > svg');
                    const width = svg.style.width, maxWidth = svg.style.maxWidth, w = svg.getBoundingClientRect().width;
                    const t0 = performance.now();
                    svg.style.maxWidth = 'none';
                    svg.style.width = (w + svg.width.baseVal.value / 100) + 'px';
                    requestAnimationFrame(() => setTimeout(() => {
                        const ms = performance.now() - t0;
                        svg.style.width = width;
                        svg.style.maxWidth = maxWidth;
                        requestAnimationFrame(() => setTimeout(() => done(ms), 300));
                    }, 0));
                })
                """, new object[] { (double)x, (double)y }));
        }
        var browserAlone = alone.OrderBy(t => t).ElementAt(1);
        // the far parts are resized one a frame, each at about the browser's own cost of a part: a second in all on this
        // machine, and that cost for every part on a slow one (CI's Linux runner took 700 to 880 ms for a part alone)
        var everyBudget = Math.Max(1000, parts * browserAlone * 1.5);
        await Page.WaitForTimeoutAsync(500);

        var r = await Page.EvaluateAsync<JsonElement>("""
            ([x, y, cap]) => new Promise(done => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const pics = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
                const target = document.elementFromPoint(x, y);
                const t0 = performance.now();
                target.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, ctrlKey: true, deltaY: -100, clientX: x, clientY: y }));
                const handler = performance.now() - t0;
                const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                const level = parseInt(s.value);
                let painted = null;
                requestAnimationFrame(() => setTimeout(() => { painted = performance.now() - t0; }, 0));
                const check = () => {
                    const all = pics.every(p => Math.abs(p.getBoundingClientRect().width / p.width.baseVal.value * 100 - level) <= 0.6);
                    if (all && painted !== null) done({ handler, painted, everyPart: performance.now() - t0, level, probe: window.__bench.probe });
                    else if (performance.now() - t0 > cap) done({ handler, painted, everyPart: -1, level, probe: window.__bench.probe });
                    else requestAnimationFrame(check);
                };
                requestAnimationFrame(check);
            })
            """, new object[] { (double)x, (double)y, Math.Max(10000, everyBudget * 5) });

        var stretch = ContentionScale.Stretch(r.GetProperty("probe").EnumerateArray().Select(p => p.GetDouble()).ToList());
        var painted = r.GetProperty("painted").GetDouble();
        var everyPart = r.GetProperty("everyPart").GetDouble();
        var budget = Math.Max(50, browserAlone * 1.25 + 10) * stretch;
        _output.WriteLine($"{parts} parts: handler {r.GetProperty("handler").GetDouble():F0} ms, painted {painted:F0} ms, every part {everyPart:F0} ms, " +
                          $"the part alone {string.Join("/", alone.Select(t => t.ToString("F0")))} ms, stretch {stretch:F2}");
        Assert.True(everyPart >= 0, "every part reaches the new level");
        Assert.True(painted <= budget, $"the notch was painted after {painted:F0} ms (budget {budget:F0} ms: the part alone took the browser {browserAlone:F0} ms, stretch {stretch:F2})");
        Assert.True(everyPart <= everyBudget * stretch,
            $"every part reached the new level after {everyPart:F0} ms (budget {everyBudget * stretch:F0} ms: {parts} parts, the part alone {browserAlone:F0} ms, stretch {stretch:F2})");
    }
}
