using System.Text.Json;
using Kronikol.Reports;
using Microsoft.Playwright;
using Xunit;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What the audit of 5.7.0 to 5.7.2 found the zoom of every diagram, and the parts of a diagram lined up, did wrong
/// (<c>plans/ZOOM_EVERY_DIAGRAM_PLAN.md</c> section 9, 5.7.3): a diagram in parts after its box changes width, a zoom
/// back to the fit level from a pan, the pointer between two parts, a sideways wheel, keys typed in a field, an outline
/// the engine could not draw, an alias that is a participant keyword, a part that never draws, a diagram a hair wider
/// than its box, the first frame of a redraw, and the internal-flow popup's own scroll and tabs.
/// </summary>
[Collection(PlaywrightCollections.Zoom)]
public class ZoomRobustnessTests : PlaywrightTestBase
{
    public ZoomRobustnessTests(PlaywrightFixture fixture) : base(fixture) { }

    private static string WideParts(int calls) => ReportTestHelper.PartsSource(calls, new Dictionary<int, int> { [5] = 300 });

    private async Task OpenPartsAsync(string fileName, string source, int partHeight = 900)
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, fileName, source, partHeight));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await WaitForPartsAsync();
    }

    // every part drawn, lined up, at its level, and the far parts resized
    private async Task WaitForPartsAsync(int timeoutMs = 60000)
    {
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                if (!c || c._noteRendering || window._plantumlRendering || c._lineUpPending || c._zoomPending) return false;
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return parts.length > 1 && parts.every(p => p.querySelector(':scope > svg'))
                    && !!c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
            }
            """, null, new() { Timeout = timeoutMs, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    private sealed record Part(double L, double R, double W, double Level);

    private sealed record Layout(double Left, double Right, double ScrollLeft, double ScrollWidth, double ClientWidth, int Min,
        int Value, Part[] Parts)
    {
        public double Content => Right - Left;
        public double Spread => Parts.Max(p => p.Level) - Parts.Min(p => p.Level);
        public override string ToString() =>
            $"content {Left:F1}..{Right:F1}, scroll {ScrollLeft:F0}/{ScrollWidth:F0} of {ClientWidth:F0}, slider {Value} from {Min}; parts "
            + string.Join(" | ", Parts.Select(p => $"{p.L:F1}..{p.R:F1} at {p.Level * 100:F2}%"));
    }

    private async Task<Layout> LayoutAsync() => JsonSerializer.Deserialize<Layout>(await Page.EvaluateAsync<string>("""
        () => {
            const c = document.querySelector("[data-diagram-type='plantuml']");
            const cs = getComputedStyle(c), cr = c.getBoundingClientRect();
            const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
            const pics = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
            return JSON.stringify({
                Left: cr.left + c.clientLeft + parseFloat(cs.paddingLeft),
                Right: cr.left + c.clientLeft + c.clientWidth - parseFloat(cs.paddingRight),
                ScrollLeft: c.scrollLeft, ScrollWidth: c.scrollWidth, ClientWidth: c.clientWidth,
                Min: s ? parseInt(s.min) : -1, Value: s ? parseInt(s.value) : -1,
                Parts: pics.map(p => { const r = p.getBoundingClientRect(); return { L: r.left, R: r.right, W: r.width, Level: r.width / p.width.baseVal.value }; })
            });
        }
        """))!;

    // The parts at one level, the widest as wide as the box's content, none past it, and the slider at that level.
    private static bool FitsItsBox(Layout l) =>
        l.Spread <= 0.002 && Math.Abs(l.Parts.Max(p => p.W) - l.Content) <= 1.5
        && l.Parts.All(p => p.L >= l.Left - 1 && p.R <= l.Right + 1)
        && Math.Abs(l.Min - l.Parts[0].Level * 100) <= 1 && l.Value == l.Min;

    // B3: a part at the fit level was given a width in pixels, so it kept the width of the box it was sized in (5.6.1's
    // parts, each at its own fit, followed the box)
    [Fact]
    public async Task A_diagram_in_parts_at_its_fit_level_fits_its_box_again_after_the_window_narrows_and_widens()
    {
        await Page.SetViewportSizeAsync(1400, 900);
        await OpenPartsAsync("RobustResize.html", WideParts(30));
        var before = await LayoutAsync();
        Assert.True(before.Min < 100 && FitsItsBox(before), $"premise, a wide diagram at its fit level: {before}");

        foreach (var width in new[] { 1100, 1500 })
        {
            await Page.SetViewportSizeAsync(width, 900);
            var after = await LayoutAsync();
            for (var i = 0; i < 25 && !FitsItsBox(after); i++)
            {
                await Page.WaitForTimeoutAsync(200);
                after = await LayoutAsync();
            }
            Assert.True(FitsItsBox(after), $"at a window {width} px wide: {after}");
            Assert.NotEqual(before.Content, after.Content);
        }
    }

    // B3 far from the screen: a diagram more than a window's height away when the window narrows has its slider made
    // again as it comes near, so it fits its box once the reader is back at it
    [Fact]
    public async Task A_diagram_in_parts_far_from_the_screen_when_the_window_narrows_fits_its_box_once_the_reader_is_back()
    {
        await Page.SetViewportSizeAsync(1400, 900);
        await OpenPartsAsync("RobustResizeFar.html", WideParts(30));
        var before = await LayoutAsync();
        Assert.True(before.Min < 100 && FitsItsBox(before), $"premise, a wide diagram at its fit level: {before}");
        await Page.EvaluateAsync("""
            () => {
                const room = document.createElement('div');
                room.style.height = '8000px';
                document.body.appendChild(room);
                const c = document.querySelector("[data-diagram-type='plantuml']");
                window.scrollTo(0, scrollY + c.getBoundingClientRect().bottom + 2 * innerHeight);
            }
            """);
        await TwoAnimationFramesAsync(Page);
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector(\"[data-diagram-type='plantuml']\").getBoundingClientRect().bottom < -innerHeight"),
            "premise: the diagram is more than a window's height above the window");

        await Page.SetViewportSizeAsync(1100, 900);
        await Page.WaitForTimeoutAsync(600);
        await Page.EvaluateAsync("() => document.querySelector(\"[data-diagram-type='plantuml']\").scrollIntoView({ block: 'start' })");
        var after = await LayoutAsync();
        for (var i = 0; i < 25 && !FitsItsBox(after); i++)
        {
            await Page.WaitForTimeoutAsync(200);
            after = await LayoutAsync();
        }
        Assert.True(FitsItsBox(after), $"back at the diagram after the window narrowed: {after}");
        Assert.NotEqual(before.Content, after.Content);
    }

    // B1: back to the fit level, the parts near the screen were resized at once and the far ones a frame each, and the
    // box kept the scroll a pan had given it until the last far part was narrow again
    [Fact]
    public async Task Back_to_the_fit_level_from_a_pan_across_no_frame_shows_the_parts_scrolled_off()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await OpenPartsAsync("RobustBackToFit.html", WideParts(150));
        var fit = await LayoutAsync();
        Assert.True(fit.Parts.Length >= 10, $"premise, parts far from the screen: {fit.Parts.Length}");
        await ZoomFacts.ScrollToPartAsync(Page, 0);
        await ZoomFacts.SetLevelAsync(Page, 0, 100);
        await WaitForPartsAsync();
        await Page.EvaluateAsync("() => { const c = document.querySelector(\"[data-diagram-type='plantuml']\"); c.scrollLeft = c.scrollWidth; }");
        await TwoAnimationFramesAsync(Page);
        Assert.True((await LayoutAsync()).ScrollLeft > 100, "premise: panned across");

        await Page.EvaluateAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                window.__frames = [];
                const watch = () => {
                    window.__frames.push(c.scrollLeft);
                    if (window.__frames.length < 240 && (c._zoomPending || window.__frames.length < 3)) requestAnimationFrame(watch);
                };
                requestAnimationFrame(watch);
            }
            """);
        await ZoomFacts.SetLevelAsync(Page, 0, fit.Min);
        await WaitForPartsAsync();
        var frames = await Page.EvaluateAsync<double[]>("() => window.__frames");
        Assert.True(frames.Length >= 3, $"frames watched: {frames.Length}");
        Assert.All(frames, s => Assert.True(s <= 0.5, $"a frame showed the box scrolled {s:F0} px across: {string.Join(", ", frames.Select(f => f.ToString("F0")))}"));
    }

    // Z6 over a seam: with the pointer between two parts, no picture was under it and nothing kept the reader's place,
    // though the page's own scroll anchoring was held off
    [Fact]
    public async Task A_notch_with_the_pointer_between_two_parts_keeps_what_is_below_it_in_place()
    {
        await OpenPartsAsync("RobustSeam.html", WideParts(30));
        var at = await Page.EvaluateAsync<double[]>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const pics = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
                const a = pics[0].getBoundingClientRect(), b = pics[1].getBoundingClientRect();
                window.scrollBy(0, (a.bottom + b.top) / 2 - innerHeight / 2);
                return [];
            }
            """);
        await TwoAnimationFramesAsync(Page);
        var point = await Page.EvaluateAsync<double[]>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const pics = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
                const a = pics[0].getBoundingClientRect(), b = pics[1].getBoundingClientRect();
                const x = b.left + b.width / 2, y = (a.bottom + b.top) / 2;
                window.__seam = { fy: (y - b.top) / b.height, y };
                return [x, y, b.top - a.bottom];
            }
            """);
        Assert.True(point[2] > 2, $"premise: a gap between the parts, {point[2]:F1} px");
        await ZoomFacts.CtrlWheelAsync(Page, (float)point[0], (float)point[1], 1);
        await WaitForPartsAsync();
        var moved = await Page.EvaluateAsync<double>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const b = c.querySelectorAll(':scope > .puml-fragment > svg')[1].getBoundingClientRect();
                return b.top + window.__seam.fy * b.height - window.__seam.y;
            }
            """);
        Assert.True((await LayoutAsync()).Value > (await LayoutAsync()).Min, "premise: the notch zoomed in");
        Assert.True(Math.Abs(moved) <= 2, $"the point below the pointer moved {moved:F1} px down the page");
    }

    // A sideways wheel with Ctrl (a tilt wheel, a sideways swipe) has no deltaY: it zoomed out, as a wheel turned down
    [Fact]
    public async Task Ctrl_with_a_sideways_wheel_over_a_diagram_is_left_to_the_browser()
    {
        await OpenPartsAsync("RobustSideways.html", WideParts(30));
        await ZoomFacts.SetLevelAsync(Page, 0, 100);
        await WaitForPartsAsync();
        var r = JsonSerializer.Deserialize<JsonElement>(await Page.EvaluateAsync<string>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const svg = c.querySelector(':scope > .puml-fragment > svg');
                const q = svg.getBoundingClientRect();
                const e = new WheelEvent('wheel', { ctrlKey: true, deltaX: 120, deltaY: 0, clientX: q.left + q.width / 2,
                    clientY: Math.max(q.top + 20, 200), bubbles: true, cancelable: true });
                svg.dispatchEvent(e);
                return JSON.stringify({ prevented: e.defaultPrevented, value: c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider').value });
            }
            """));
        Assert.False(r.GetProperty("prevented").GetBoolean(), "a sideways wheel is not the diagram's");
        Assert.Equal("100", r.GetProperty("value").GetString());
    }

    // Ctrl and - typed in a field (the search box) zoomed the selected diagram, not what the reader typed in
    [Fact]
    public async Task Ctrl_and_minus_typed_in_the_search_box_while_a_diagram_is_selected_is_left_to_the_browser()
    {
        await OpenPartsAsync("RobustTyped.html", WideParts(30));
        await ZoomFacts.SetLevelAsync(Page, 0, 100);
        await WaitForPartsAsync();
        var r = JsonSerializer.Deserialize<JsonElement>(await Page.EvaluateAsync<string>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const svg = c.querySelector(':scope > .puml-fragment > svg');
                const q = svg.getBoundingClientRect();
                svg.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: q.left + 10, clientY: Math.max(q.top + 10, 200) }));
                const selected = c.classList.contains('diagram-selected');
                const box = document.querySelector('#searchbar');
                box.focus();
                const e = new KeyboardEvent('keydown', { key: '-', code: 'Minus', ctrlKey: true, bubbles: true, cancelable: true });
                box.dispatchEvent(e);
                return JSON.stringify({ selected, prevented: e.defaultPrevented, value: c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider').value });
            }
            """));
        Assert.True(r.GetProperty("selected").GetBoolean(), "premise: the diagram is selected");
        Assert.False(r.GetProperty("prevented").GetBoolean(), "keys typed in a field are the field's");
        Assert.Equal("100", r.GetProperty("value").GetString());
    }

    // The first frame of every part's first lifeline, in page pixels
    private const string FirstLifelines = """
        () => {
            const c = document.querySelector("[data-diagram-type='plantuml']");
            return Array.from(c.querySelectorAll(':scope > .puml-fragment > svg')).map(svg => {
                const xs = window._lifelinesOf(svg), r = svg.getBoundingClientRect(), vb = svg.viewBox.baseVal;
                return xs.length ? r.left + (xs[0] - vb.x) * r.width / vb.width : null;
            });
        }
        """;

    private static double Apart(double?[] xs) => xs.All(x => x.HasValue) ? xs.Max()!.Value - xs.Min()!.Value : double.PositiveInfinity;

    // C2: an outline the engine could not draw (a failure, or 30 s gone on a slow first load) was kept as "none" for the
    // header every diagram shares, so no diagram in parts lined up again until the page was loaded again
    [Fact]
    public async Task An_outline_the_engine_could_not_draw_leaves_the_next_drawing_free_to_line_up()
    {
        await Page.AddInitScriptAsync("""
            (() => {
                window.__failAside = true;
                let engine;
                Object.defineProperty(window, 'plantuml', {
                    configurable: true,
                    get() { return engine; },
                    set(v) {
                        if (v && typeof v.render === 'function' && !v.__failing) {
                            const render = v.render.bind(v);
                            v.render = function (lines, id, opts) {
                                if (window.__failAside && /^kronikol-aside-/.test(id)) {
                                    if (opts && opts.onError) { opts.onError('test: no outline'); return; }
                                    throw new Error('test: no outline');
                                }
                                return render(lines, id, opts);
                            };
                            v.__failing = true;
                        }
                        engine = v;
                    }
                });
            })();
            """);
        await OpenPartsAsync("RobustOutlineRetry.html", ReportTestHelper.PartsSource(30, new Dictionary<int, int> { [5] = 300 },
            replyNotes: true, extraNoteFields: 8));
        Assert.True(Apart(await Page.EvaluateAsync<double?[]>(FirstLifelines)) > 2, "premise: with no outline the parts are not lined up");
        const string partsKey = """
            () => Array.from(document.querySelectorAll("[data-diagram-type='plantuml'] > .puml-fragment"))
                .map(p => p.getAttribute('data-plantuml')).join('\u0000')
            """;
        var firstKey = await Page.EvaluateAsync<string>(partsKey);

        await Page.EvaluateAsync("""
            () => {
                window.__failAside = false;
                document.querySelectorAll("[data-diagram-type='plantuml'] svg").forEach(s => { s._before = true; });
                window._setReportDetails('expanded');
            }
            """);
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const pics = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
                return pics.length > 1 && pics.every(s => !s._before);
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await WaitForPartsAsync();
        var xs = await Page.EvaluateAsync<double?[]>(FirstLifelines);
        Assert.True(Apart(xs) <= 0.5, $"drawn again, the parts line up: {string.Join(", ", xs.Select(x => x?.ToString("F1") ?? "none"))}");

        // and the very parts whose outline failed, drawn again, line up too: the failure is not kept for them
        await Page.EvaluateAsync("""
            () => {
                document.querySelectorAll("[data-diagram-type='plantuml'] svg").forEach(s => { s._before = true; });
                window._setReportDetails('truncated');
            }
            """);
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const pics = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
                return pics.length > 1 && pics.every(s => !s._before);
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await WaitForPartsAsync();
        Assert.True(firstKey == await Page.EvaluateAsync<string>(partsKey), "premise: the parts are those first drawn with no outline");
        xs = await Page.EvaluateAsync<double?[]>(FirstLifelines);
        Assert.True(Apart(xs) <= 0.5, $"the parts first drawn with no outline, drawn again, line up: {string.Join(", ", xs.Select(x => x?.ToString("F1") ?? "none"))}");
    }

    // C1: a participant whose alias is a keyword (the generator camelizes a service called Queue to `queue`) sends the
    // first message of every part: the row was put after that message, and the part's numbering began again after it
    [Fact]
    public async Task Parts_whose_messages_are_sent_by_a_participant_named_like_a_keyword_keep_their_numbers_and_line_up()
    {
        var sb = new System.Text.StringBuilder("@startuml\n!pragma teoz true\nskinparam wrapWidth 800\nautonumber 1\n\n");
        sb.Append("queue \"Queue\" as queue\nparticipant \"Gateway\" as g\nparticipant \"Service\" as s\n\n");
        for (var k = 1; k <= 30; k++)
            sb.Append(k == 5 ? $"queue -> g : call{k} {new string('w', 300)}\n" : $"queue -> g : call{k}\n").Append($"g --> queue : ok{k}\n");
        sb.Append("@enduml");
        await OpenPartsAsync("RobustKeywordAlias.html", sb.ToString());

        var numbers = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment > svg'))
                .map(svg => Array.from(svg.querySelectorAll('text')).map(t => t.textContent.trim()).filter(t => /^\d+$/.test(t)).join(','))
            """);
        Assert.True(numbers.Length > 1, "premise: drawn in parts");
        var all = string.Join(",", numbers).Split(',').Select(int.Parse).ToArray();
        Assert.Equal(Enumerable.Range(1, 60).ToArray(), all);
        Assert.True(Apart(await Page.EvaluateAsync<double?[]>(FirstLifelines)) <= 0.5, "the parts line up");
    }

    // B5: a part that never draws (a render error, too large, nothing to draw) left the diagram's picture items out for
    // good; the part under the click is copied, as before 5.7.0, and its items say which
    [Fact]
    public async Task A_part_that_never_draws_leaves_the_picture_items_of_the_part_under_the_click()
    {
        await Page.AddInitScriptAsync("""
            (() => {
                let engine;
                Object.defineProperty(window, 'plantuml', {
                    configurable: true,
                    get() { return engine; },
                    set(v) {
                        if (v && typeof v.render === 'function' && !v.__failing) {
                            const render = v.render.bind(v);
                            v.render = function (lines, id, opts) {
                                if (/-frag-1$/.test(id)) {
                                    if (opts && opts.onError) { opts.onError('test: this part fails'); return; }
                                    throw new Error('test: this part fails');
                                }
                                return render(lines, id, opts);
                            };
                            v.__failing = true;
                        }
                        engine = v;
                    }
                });
            })();
            """);
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "RobustFailedPart.html", WideParts(30)));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const parts = c ? Array.from(c.querySelectorAll(':scope > .puml-fragment')) : [];
                return parts.length === 3 && parts.every(p => p.dataset.rendered) && !window._plantumlRendering && !c._lineUpPending;
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => document.querySelectorAll(\"[data-diagram-type='plantuml'] > .puml-fragment\")[1].querySelectorAll('svg').length"));

        await ZoomFacts.ScrollToPartAsync(Page, 0);
        await Page.EvaluateAsync("""
            () => {
                const svg = document.querySelector("[data-diagram-type='plantuml'] > .puml-fragment > svg");
                const r = svg.getBoundingClientRect();
                svg.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 8, clientY: Math.max(r.top + 40, 200) }));
            }
            """);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        var items = await Page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('.diagram-ctx-menu *')).filter(e => e.children.length === 0).map(e => e.textContent.trim()).filter(t => /PNG|SVG/.test(t))");
        Assert.Contains("Copy part 1 of 3 as SVG", items);
        Assert.Contains("Save part 1 of 3 as PNG", items);
    }

    // P6: a diagram a hair wider than its box's content rounds to a fit level of 100, so it fits; its parts were still
    // drawn at their natural width, past the box, with a scrollbar, and every redraw held the browser's scroll anchoring
    // off for a width that never changed (5.7.1's fault, for these diagrams)
    [Fact]
    public async Task A_diagram_in_parts_a_hair_wider_than_its_box_fits_it_with_no_scrollbar_and_a_redraw_leaves_the_scroll_alone()
    {
        // parts placed by their first lifeline, one of them shifted (a later part has a note left of the first participant):
        // the lined-up span is the widest part with its shift
        var source = LinedUpPartsTests.EdgeSource();
        await Page.SetViewportSizeAsync(2400, 900);
        await OpenPartsAsync("RobustHairProbe.html", source);
        var probe = await Page.EvaluateAsync<double[]>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const cs = getComputedStyle(c);
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                const content = c.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
                return [Math.max(...parts.map(p => p.querySelector(':scope > svg').width.baseVal.value + (p._kShift || 0))), innerWidth - content,
                    Math.max(...parts.map(p => p._kShift || 0))];
            }
            """);
        Assert.True(probe[2] > 2, $"premise: a part is shifted to line up, by {probe[2]:F1} px");
        // the window that leaves the content 0.3% narrower than the picture
        await Page.SetViewportSizeAsync((int)Math.Ceiling(probe[0] / 1.003 + probe[1]), 900);
        await OpenPartsAsync("RobustHair.html", source);
        var l = await LayoutAsync();
        Assert.True(l.Min == 50 && l.Value == 100, $"premise: the diagram fits, its fit level rounded to 100: {l}");
        Assert.True(probe[0] > l.Content, $"premise: the picture ({probe[0]:F1} px) is wider than the content ({l.Content:F1} px)");
        Assert.True(l.ScrollWidth <= l.ClientWidth, $"no scrollbar: {l}");
        Assert.True(l.Parts.All(p => p.R <= l.Right + 1), $"no part past the box's content: {l}");

        await Page.EvaluateAsync("""
            () => {
                window.__held = 0;
                new MutationObserver(() => { if (document.documentElement.style.overflowAnchor === 'none') window.__held++; })
                    .observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });
                document.querySelectorAll("[data-diagram-type='plantuml'] svg").forEach(s => { s._before = true; });
                window._setReportDetails('expanded');
            }
            """);
        await Page.WaitForFunctionAsync("""
            () => {
                const pics = Array.from(document.querySelectorAll("[data-diagram-type='plantuml'] > .puml-fragment > svg"));
                return pics.length > 1 && pics.every(s => !s._before);
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await WaitForPartsAsync();
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => window.__held"));
    }

    // B2: a diagram in parts drawn again by a toggle showed its new parts as the engine drew them, each at its own
    // width and not placed, for a frame or more, until its slider was made again after the paint
    [Fact]
    public async Task A_lined_up_diagram_in_parts_drawn_again_shows_its_parts_lined_up_in_the_first_frame()
    {
        var sb = new System.Text.StringBuilder(ReportTestHelper.PartsSource(30, new Dictionary<int, int> { [5] = 300 }, replyNotes: true, extraNoteFields: 8));
        // something left of the first participant in a later part only, so the parts stand at different places across
        sb.Replace("c -> g : call24\n", "c -> g : call24\nnote left of c\nwide note left of the first participant\nend note\n");
        await OpenPartsAsync("RobustFirstFrame.html", sb.ToString());
        Assert.True(Apart(await Page.EvaluateAsync<double?[]>(FirstLifelines)) <= 0.5, "premise: lined up");
        Assert.True(await Page.EvaluateAsync<bool>("() => Array.from(document.querySelectorAll(\"[data-diagram-type='plantuml'] > .puml-fragment\")).some(p => p._kShift > 1)"),
            "premise: a part stands shifted");

        await Page.EvaluateAsync("""
            (code) => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                c.querySelectorAll('svg').forEach(s => { s._before = true; });
                window.__first = null;
                const firsts = eval(code);
                const watch = () => {
                    const pics = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
                    if (pics.length > 1 && pics.every(s => !s._before && s.getBoundingClientRect().width > 0)) { window.__first = firsts(); return; }
                    requestAnimationFrame(watch);
                };
                requestAnimationFrame(watch);
                window._setReportDetails('expanded');
            }
            """, FirstLifelines);
        await WaitForPartsAsync();
        var first = await Page.EvaluateAsync<double?[]>("() => window.__first");
        Assert.NotNull(first);
        Assert.True(Apart(first) <= 0.5, $"the first frame showed the parts' first lifelines at {string.Join(", ", first.Select(x => x?.ToString("F1") ?? "none"))}");
    }

    // C10 (before 5.7.0, from 2026-06): a diagram numbered from past 1 (the second of a scenario's diagrams starts where
    // the first ended) and cut at a note too long for one part had its parts after the cut numbered from 1
    [Fact]
    public async Task A_diagram_numbered_from_past_one_and_cut_at_a_long_note_keeps_its_numbers_in_every_part()
    {
        var fields = string.Concat(Enumerable.Range(0, 900).Select(i => $"  \"field{i}\": \"{new string('v', 12)}\",\n"));
        var source = "@startuml\n!pragma teoz true\nautonumber 5\n\nactor \"Caller\" as caller\nparticipant \"Api\" as api\n\n"
            + "caller -> api : first\napi --> caller : ok\nnote right\n{\n" + fields + "  \"end\": 1\n}\nend note\n"
            + "caller -> api : second\napi --> caller : ok2\n@enduml";
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "RobustNumberedPastOne.html", source));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await ZoomFacts.WaitForDrawnAsync(Page, 0);
        // the note drawn in full, longer than a part holds: with the payload scroll box off (PayloadScrollLines = 0) a long
        // payload is cut across parts, where the box caps it under the length at which the page cuts a note
        await Page.EvaluateAsync("() => { window._payloadScrollLines = 0; window._setReportDetails('expanded'); }");
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return !c._noteRendering && !window._plantumlRendering && parts.length > 1 && parts.every(p => p.querySelector(':scope > svg'));
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        var withNote = await Page.EvaluateAsync<int>("""
            () => Array.from(document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment'))
                .filter(p => /\bfield[0-9]+\b/.test(p.getAttribute('data-plantuml') || '')).length
            """);
        Assert.True(withNote >= 2, $"premise: the note is cut across parts ({withNote} part(s) hold it)");
        var numbers = await Page.EvaluateAsync<string>("""
            () => Array.from(document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment > svg'))
                .flatMap(svg => Array.from(svg.querySelectorAll('text')).map(t => t.textContent.trim()).filter(t => /^\d+$/.test(t))).join(',')
            """);
        Assert.Equal("5,6,7,8", numbers);
    }

    // C11 (before 5.7.0): a part the engine never answered was given up after 60 s with nothing said and not settled, so
    // it showed "Rendering diagram..." for good (the wait is shortened by the page's test hook)
    [Fact]
    public async Task A_part_the_engine_never_answers_is_given_up_with_a_message_and_settled()
    {
        await Page.AddInitScriptAsync("""
            (() => {
                window.__kronikolRenderWaitMs = 1500;
                let engine;
                Object.defineProperty(window, 'plantuml', {
                    configurable: true,
                    get() { return engine; },
                    set(v) {
                        if (v && typeof v.render === 'function' && !v.__silent) {
                            const render = v.render.bind(v);
                            v.render = function (lines, id, opts) { if (/-frag-1$/.test(id)) return; return render(lines, id, opts); };
                            v.__silent = true;
                        }
                        engine = v;
                    }
                });
            })();
            """);
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "RobustGivenUp.html", WideParts(30)));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await Page.WaitForFunctionAsync("""
            () => {
                const parts = Array.from(document.querySelectorAll("[data-diagram-type='plantuml'] > .puml-fragment"));
                return parts.length === 3 && !!parts[0].querySelector('svg') && !!parts[2].querySelector('svg');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelectorAll(\"[data-diagram-type='plantuml'] > .puml-fragment\")[1].dataset.rendered",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        var text = await Page.EvaluateAsync<string>("() => document.querySelectorAll(\"[data-diagram-type='plantuml'] > .puml-fragment\")[1].textContent");
        Assert.Contains("Render error", text);
    }

    // C4: a part drawn again that the engine finds too large was shown with the shim's markup, which prints the engine
    // text it was given under "Raw PlantUML", the page's footbox line and lining-up row with it (Z12)
    [Fact]
    public async Task A_part_drawn_again_that_is_too_large_shows_its_own_source_with_no_line_the_page_added()
    {
        await Page.AddInitScriptAsync("""
            (() => {
                window.__tooLargeOnce = false;
                let engine;
                Object.defineProperty(window, 'plantuml', {
                    configurable: true,
                    get() { return engine; },
                    set(v) {
                        if (v && typeof v.render === 'function' && !v.__tooLarge) {
                            const render = v.render.bind(v);
                            v.render = function (lines, id, opts) {
                                if (window.__tooLargeOnce && /-frag-(n-)?1$/.test(id)) {
                                    window.__tooLargeOnce = false;
                                    const msg = 'java.lang.RuntimeException: Diagram too large for browser rendering: 1200x99999 (max 98304)';
                                    if (opts && typeof opts.onError === 'function' && opts.onError(msg) === true) return;
                                    // what the render shim writes when no one takes the failure: the text it was given
                                    const pre = document.createElement('pre');
                                    pre.textContent = lines.join('\n');
                                    const el = document.getElementById(id);
                                    el.innerHTML = '<strong>Diagram too large for client-side rendering.</strong>';
                                    el.appendChild(pre);
                                    return;
                                }
                                return render(lines, id, opts);
                            };
                            v.__tooLarge = true;
                        }
                        engine = v;
                    }
                });
            })();
            """);
        await OpenPartsAsync("RobustTooLarge.html", ReportTestHelper.PartsSource(30, new Dictionary<int, int> { [5] = 300 },
            replyNotes: true, extraNoteFields: 8));
        await Page.EvaluateAsync("""
            () => {
                document.querySelectorAll("[data-diagram-type='plantuml'] svg").forEach(s => { s._before = true; });
                window.__tooLargeOnce = true;
                window._setReportDetails('expanded');
            }
            """);
        await Page.WaitForFunctionAsync("""
            () => {
                const parts = Array.from(document.querySelectorAll("[data-diagram-type='plantuml'] > .puml-fragment"));
                return parts.length > 1 && !window.__tooLargeOnce && /too large/i.test(parts[1].textContent)
                    && !!parts[0].querySelector('svg') && !parts[0].querySelector('svg')._before;
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        var text = await Page.EvaluateAsync<string>("() => document.querySelectorAll(\"[data-diagram-type='plantuml'] > .puml-fragment\")[1].textContent");
        Assert.Contains("@startuml", text);
        Assert.DoesNotContain("hide footbox", text);
        Assert.DoesNotContain("<space:", text);
        Assert.DoesNotContain("-[hidden]>", text);
    }

    private async Task OpenPopupAsync(InternalFlowTab startTab)
    {
        await Page.GotoReportAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: startTab == InternalFlowTab.FlameChart,
            includeContextMenu: true, startTab: startTab)));
    }

    // B4: a zoom in the popup kept the reader's place by scrolling the window, the report behind the popup, not the popup
    [Fact]
    public async Task Zooming_the_popups_activity_diagram_leaves_the_report_behind_it_where_it_was()
    {
        await OpenPopupAsync(InternalFlowTab.Activity);
        // the report's own stylesheet for drawn pictures, as every report page has: a picture's height follows its width
        await Page.AddStyleTagAsync(new() { Content = DiagramContextMenu.GetInlineSvgStyles() });
        // a report long above and below the link, scrolled to it
        await Page.EvaluateAsync("""
            () => {
                const above = document.createElement('div'), below = document.createElement('div');
                above.style.height = below.style.height = '3000px';
                document.body.prepend(above);
                document.body.appendChild(below);
                document.querySelector('#trigger-seg-1').scrollIntoView({ block: 'center' });
            }
            """);
        await TwoAnimationFramesAsync(Page);
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('.iflow-popup .iflow-diagram > .diagram-zoom-controls .diagram-zoom-slider')",
            null, new() { Timeout = 30000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        // the popup scrolls in itself: room in it above the diagram, scrolled to the diagram, so it can scroll either way
        await Page.EvaluateAsync("""
            () => {
                const popup = document.querySelector('.iflow-popup');
                const room = document.createElement('div');
                room.style.height = '1500px';
                popup.prepend(room);
                popup.scrollTop = 1500;
            }
            """);
        await TwoAnimationFramesAsync(Page);
        var before = await Page.EvaluateAsync<double>("() => window.scrollY");
        Assert.True(before > 0, "premise: the report behind is scrolled");

        // Ctrl with the wheel down over the diagram, a few notches, as a reader zooming it out
        var at = await Page.EvaluateAsync<double[]>("""
            () => {
                const s = document.querySelector('.iflow-popup .iflow-diagram > .diagram-zoom-controls .diagram-zoom-slider');
                const svg = s.closest('.iflow-diagram').querySelector('svg');
                const r = svg.getBoundingClientRect(), p = document.querySelector('.iflow-popup').getBoundingClientRect();
                // low in the picture as the popup shows it: a point near the picture's top hardly moves
                const top = Math.max(r.top, p.top + 10), bottom = Math.min(r.bottom, p.bottom - 10);
                const x = r.left + r.width / 2, y = top + (bottom - top) * 0.9;
                window.__popupPoint = { svg, fy: (y - r.top) / r.height, y };
                return [x, y, parseInt(s.value), document.querySelector('.iflow-popup').scrollTop];
            }
            """);
        Assert.True(at[3] > 100, $"premise: the popup is scrolled, {at[3]:F0} px");
        await ZoomFacts.CtrlWheelAsync(Page, (float)at[0], (float)at[1], -5);
        var after = await Page.EvaluateAsync<double[]>("""
            () => {
                const p = window.__popupPoint, r = p.svg.getBoundingClientRect();
                return [window.scrollY, parseInt(document.querySelector('.iflow-popup .iflow-diagram > .diagram-zoom-controls .diagram-zoom-slider').value),
                    r.top + p.fy * r.height - p.y];
            }
            """);
        Assert.True(at[2] == 100, $"premise: the diagram fits its box, at 100 ({at[2]})");
        // each notch a step of 3 (5% of 50 to 100): the popup is as wide as what it holds, and until 5.7.3 it narrowed with
        // the picture zoomed out, so the next notch took the picture for a wide one at its fit level and drew it whole again
        Assert.True(after[1] == 85, $"five notches zoom out five steps, 100 to 85, not to {after[1]}");
        Assert.True(before == after[0], $"the report behind moved from {before} to {after[0]}");
        // the popup scrolled in its place: the point under the pointer stays where it was down the window, to the half pixel
        // a notch the popup's whole-pixel scroll leaves (a picture zoomed out stands in the middle of its box, so across it
        // moves)
        Assert.True(Math.Abs(after[2]) <= 2.5, $"the point under the pointer moved {after[2]:F1} px down the popup in five notches");
    }

    // C3: drawn while its tab was hidden (a popup opened on its flame chart), the activity diagram measured nothing and
    // was never asked again, so it had no slider and Ctrl with the wheel over it zoomed the page
    [Fact]
    public async Task The_activity_diagram_of_a_popup_opened_on_its_flame_chart_gets_its_slider_once_its_tab_is_shown()
    {
        await OpenPopupAsync(InternalFlowTab.FlameChart);
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Page.WaitForFunctionAsync("() => !!document.querySelector('.iflow-popup .iflow-view-main svg')", null,
            new() { Timeout = 30000, PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>("() => getComputedStyle(document.querySelector('.iflow-popup .iflow-view-main')).display === 'none'"),
            "premise: the activity diagram is drawn on a hidden tab");

        await Page.Locator(".iflow-popup [data-view='main']").First.ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('.iflow-popup .iflow-view-main .diagram-zoom-controls .diagram-zoom-slider')",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    // P8: a picture the server or the local Java engine drew says its width and height in pixels on its root, with
    // preserveAspectRatio none, so drawn narrower than its own width it kept its height and was squashed across: at the
    // fit level of a wide one (before 5.7.0 too), and zoomed out (any diagram that fits, since 5.7.0)
    [Fact]
    public async Task A_picture_the_Java_engine_drew_keeps_its_shape_at_its_fit_level_and_zoomed_out()
    {
        static string JavaSvg(int w, int h) =>
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" contentStyleType=\"text/css\" height=\"{h}px\" preserveAspectRatio=\"none\" "
            + $"style=\"width:{w}px;height:{h}px;background:#FFFFFF;\" version=\"1.1\" viewBox=\"0 0 {w} {h}\" width=\"{w}px\" zoomAndPan=\"magnify\">"
            + $"<defs/><g><rect x=\"5\" y=\"5\" width=\"{w - 10}\" height=\"{h - 10}\" fill=\"#E2E2F0\"/><text x=\"20\" y=\"40\" font-size=\"14\">Caller</text></g></svg>";
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Java pictures",
                Scenarios =
                [
                    new Scenario { Id = "java-wide", DisplayName = "Wide", Result = ExecutionResult.Passed },
                    new Scenario { Id = "java-fits", DisplayName = "Fits", Result = ExecutionResult.Passed }
                ]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("java-wide", JavaSvg(2400, 300), "@startuml\nA -> B\n@enduml"),
             new DefaultDiagramsFetcher.DiagramAsCode("java-fits", JavaSvg(400, 300), "@startuml\nA -> B\n@enduml")],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, "RobustJavaShape.html"), "Java shapes", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.Local, inlineSvgRendering: true);
        File.Copy(path, Path.Combine(OutputDir, "RobustJavaShape.html"), true);
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        await ZoomFacts.WaitForSliderAsync(Page, 0);
        await ZoomFacts.WaitForSliderAsync(Page, 1);
        // the report orders its scenarios its own way: each picture is found by its own width
        var wideAt = await Page.EvaluateAsync<int>("""
            () => Array.from(document.querySelectorAll("[data-diagram-type='plantuml']"))
                .findIndex(c => c.querySelector(':scope > svg').width.baseVal.value === 2400)
            """);
        Assert.True(wideAt >= 0, "premise: the wide picture is on the page");
        var fitsAt = 1 - wideAt;

        const string shape = """
            (i) => {
                const r = document.querySelectorAll("[data-diagram-type='plantuml']")[i].querySelector(':scope > svg').getBoundingClientRect();
                return [r.width, r.height];
            }
            """;
        var wide = await Page.EvaluateAsync<double[]>(shape, wideAt);
        Assert.True(wide[0] < 2000, $"premise: the wide picture is drawn at its fit level, {wide[0]:F0} px wide");
        Assert.True(Math.Abs(wide[1] / wide[0] - 0.125) <= 0.002, $"the wide picture at its fit level: {wide[0]:F1} x {wide[1]:F1}, not 8 to 1");

        await ZoomFacts.SetLevelAsync(Page, fitsAt, 50);
        var fits = await Page.EvaluateAsync<double[]>(shape, fitsAt);
        Assert.True(Math.Abs(fits[0] - 200) <= 1, $"premise: the picture that fits is zoomed out to 50%, {fits[0]:F1} px wide");
        Assert.True(Math.Abs(fits[1] / fits[0] - 0.75) <= 0.01, $"the picture zoomed out: {fits[0]:F1} x {fits[1]:F1}, not 4 to 3");
    }
}
