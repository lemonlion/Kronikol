using System.Text.Json;
using Xunit;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// No frame shows a sequence diagram in the engine's colours (5.9.1, plans/REPORT_UI_FIXES_PLAN.md I2). The engine draws
/// yellow square notes with folds and narrow black step bars; the page's role pass makes them the report's cards and
/// bars. A picture shown before the role pass reached it was painted the engine's way for a frame: a note's own redraw
/// of a diagram in parts showed its parts untagged (F1), a first drawing's part that landed before an earlier one was
/// shown untagged (F2), and a picture that got its box later was tagged only after the first frame showing it had been
/// painted, and only once it was in the window (F3). The instruments read what each frame paints (F5): for F1 and F2, a
/// frame watcher and a check at the end of every task; for F3, a ResizeObserver made after the page's, which reads a
/// frame after the page's own has tagged it. Only sequence diagrams are the role pass's: an activity diagram is never
/// tagged, and is never counted.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramColourFrameTests : PlaywrightTestBase
{
    private readonly ITestOutputHelper _output;

    public DiagramColourFrameTests(PlaywrightFixture fixture, ITestOutputHelper output) : base(fixture) => _output = output;

    // one scenario of forty calls: a sequence diagram of about eighty payload notes and step bars, drawn in parts
    private static readonly Lazy<string> Long = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-colours-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return DenseReportFixture.GenerateWithFlows(dir, OutputDir, "Colours_Long.html", features: 1, scenariosPerFeature: 1,
            callsPerScenario: 40, everyFilter: true, internalFlow: false);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    // sixty scenarios with their internal flows: views behind tabs, and scenarios far down the page
    private static readonly Lazy<string> Dense = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-colours-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return DenseReportFixture.GenerateWithFlows(dir, OutputDir, "Colours_Dense.html", features: 6, scenariosPerFeature: 10,
            everyFilter: true, internalFlow: true);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    // The pictures of the page's sequence diagrams, a diagram's own or its parts', which the role pass tags; a picture
    // drawn aside to line parts up is nobody's
    private const string PicturesJs = """
        const pictures = () => Array.from(document.querySelectorAll('.plantuml-browser > svg, .plantuml-browser > .puml-fragment > svg'))
            .filter(svg => !!svg.closest('[data-k-sequence]') && !svg.closest('[id^="kronikol-aside-"]'));
        const name = svg => (svg.parentElement && (svg.parentElement.id || svg.parentElement.className)) || '?';
        """;

    // F1, F2: every frame watched (the next asked for inside the callback, so none passes unwatched) and the end of every
    // task that changes what shows; a picture in the window without its roles is a picture painted in the engine's colours
    private const string WatchFramesJs = "() => {" + PicturesJs + """
            if (window.__frames) window.__frames.stop();
            const bad = [];
            let on = true, frames = 0;
            const shown = svg => {
                if (!svg.isConnected) return false;
                const r = svg.getBoundingClientRect();
                return r.width > 0 && r.height > 0 && r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth;
            };
            const check = where => pictures().forEach(svg => { if (!svg.getAttribute('data-k-roles') && shown(svg)) bad.push(where + ' ' + name(svg)); });
            requestAnimationFrame(function frame() { if (!on) return; frames++; check('frame'); requestAnimationFrame(frame); });
            const mo = new MutationObserver(() => { if (on) check('task'); });
            mo.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['style', 'class', 'data-rendered', 'open'] });
            window.__frames = { stop() { on = false; mo.disconnect(); return JSON.stringify({ count: bad.length, bad: bad.slice(0, 10), frames }); } };
        }
        """;

    // F3: a ResizeObserver made after the page's, so in a frame it reads a picture after the page's own has tagged it,
    // before the frame is painted; every picture on the page, and every one added, observed
    private const string WatchResizesJs = "() => {" + PicturesJs + """
            if (window.__resizes) window.__resizes.stop();
            const bad = [];
            let reports = 0;
            const ro = new ResizeObserver(entries => entries.forEach(e => {
                if (e.contentRect.width <= 0 || !e.target.isConnected || pictures().indexOf(e.target) < 0) return;
                reports++;
                if (!e.target.getAttribute('data-k-roles')) bad.push('resize ' + name(e.target));
            }));
            Array.from(document.querySelectorAll('.plantuml-browser svg')).forEach(svg => ro.observe(svg));
            const mo = new MutationObserver(records => records.forEach(r => Array.prototype.forEach.call(r.addedNodes, n => {
                if (n.nodeType !== 1) return;
                (n.tagName.toLowerCase() === 'svg' ? [n] : Array.from(n.querySelectorAll('svg'))).forEach(svg => ro.observe(svg));
            })));
            mo.observe(document.body, { subtree: true, childList: true });
            window.__resizes = { stop() { ro.disconnect(); mo.disconnect(); return JSON.stringify({ count: bad.length, bad: bad.slice(0, 10), reports }); } };
        }
        """;

    private sealed record Watch(int Count, string[] Bad, int Frames, int Reports);

    private async Task<Watch> StopAsync(string watcher)
    {
        var json = await Page.EvaluateAsync<string>($"() => window.{watcher}.stop()");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var w = new Watch(r.GetProperty("count").GetInt32(), r.GetProperty("bad").EnumerateArray().Select(b => b.GetString()!).ToArray(),
            r.TryGetProperty("frames", out var f) ? f.GetInt32() : 0, r.TryGetProperty("reports", out var rp) ? rp.GetInt32() : 0);
        _output.WriteLine($"{watcher}: {w.Count} raw, {w.Frames} frames, {w.Reports} reports; {string.Join("; ", w.Bad)}");
        return w;
    }

    /// <summary>Opens the long diagram's scenario at the top of its diagram, drawn in parts.</summary>
    private async Task OpenLongAsync(string theme = "light")
    {
        await Page.GotoReportAsync(Long.Value, new() { Timeout = PageOpenTimeout });
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await ShowLongDiagramAsync();
        await WaitForDrawnPartsAsync();
        // the first part's note buttons, which a note's own control is
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('.plantuml-browser[data-diagram-type=plantuml] > .puml-fragment .note-hover-rect')",
            null, new() { Timeout = 60_000, PollingInterval = 200 });
    }

    private Task ShowLongDiagramAsync() => Page.EvaluateAsync("""
        () => {
            document.querySelectorAll('details.feature, details.scenario').forEach(function (d) { d.open = true; });
            document.querySelector('.plantuml-browser[data-diagram-type=plantuml]').scrollIntoView({ block: 'start' });
        }
        """);

    // the diagram drawn in parts, every part with its picture, and nothing drawing: what a drawing leaves, whatever it
    // coloured
    private Task WaitForDrawnPartsAsync() => Page.WaitForFunctionAsync("""
        () => {
            var c = document.querySelector('.plantuml-browser[data-diagram-type=plantuml]');
            var parts = c ? c.querySelectorAll(':scope > .puml-fragment') : [];
            return !window._plantumlRendering && !c._noteRendering && parts.length > 1 && (!window.__before || !document.contains(window.__before))
                && Array.prototype.every.call(parts, function (p) { return !!p.querySelector('svg'); });
        }
        """, null, new() { Timeout = 120_000, PollingInterval = 200 });

    private Task<int> PartsAsync() => Page.EvaluateAsync<int>(
        "() => document.querySelector('.plantuml-browser[data-diagram-type=plantuml]').querySelectorAll(':scope > .puml-fragment').length");

    // F1: a note's own control redraws a diagram in parts, and shows the parts coloured
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_notes_own_redraw_of_a_diagram_in_parts_never_shows_the_engines_colours(string theme)
    {
        await OpenLongAsync(theme);
        Assert.True(await PartsAsync() >= 2, "the diagram is drawn in parts");

        await Page.EvaluateAsync(WatchFramesJs);
        // JS dispatch: an SVG <text> would take a pointer's double-click
        await Page.EvaluateAsync("""
            () => {
                var part = document.querySelector('.plantuml-browser[data-diagram-type=plantuml]').querySelectorAll(':scope > .puml-fragment')[0];
                window.__before = part.querySelector('svg');
                part.querySelector('.note-hover-rect').dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
            }
            """);
        await WaitForDrawnPartsAsync();
        var watch = await StopAsync("__frames");

        Assert.True(watch.Frames > 0, "frames were watched");
        Assert.Equal(0, watch.Count);
    }

    // F2: the first drawing of a diagram in parts, part 0's answer held back so part 1 lands first
    [Fact]
    public async Task A_first_drawing_whose_later_part_lands_first_never_shows_the_engines_colours()
    {
        await Page.GotoReportAsync(Long.Value, new() { Timeout = PageOpenTimeout });
        await Page.WaitForFunctionAsync("() => !!(window.plantuml && window.plantuml.render)", null, new() { Timeout = 60_000, PollingInterval = 200 });
        var parallel = await Page.EvaluateAsync<int>("() => window.plantuml.maxParallel || 1");
        Assert.True(parallel >= 2, $"the engine draws {parallel} at a time, so a later part can land first");
        // the suite's own pattern for holding the engine's answer back: the render call wrapped, not the network
        await Page.EvaluateAsync("""
            () => {
                var render = window.plantuml.render;
                window.__heldBack = 0;
                window.__landed = [];
                window.plantuml.render = function (lines, id, opts) {
                    var self = this, args = arguments;
                    if (/-frag-0$/.test(id || '')) { window.__heldBack++; setTimeout(function () { render.apply(self, args); }, 2500); return; }
                    return render.apply(this, args);
                };
                new MutationObserver(function (records) {
                    records.forEach(function (r) {
                        if (r.attributeName === 'data-rendered' && r.target.classList.contains('puml-fragment')) window.__landed.push(r.target.dataset.fragment);
                    });
                }).observe(document.body, { subtree: true, attributes: true, attributeFilter: ['data-rendered'] });
            }
            """);
        await Page.EvaluateAsync(WatchFramesJs);
        await ShowLongDiagramAsync();
        await WaitForDrawnPartsAsync();
        var watch = await StopAsync("__frames");

        Assert.True(await Page.EvaluateAsync<int>("() => window.__heldBack") >= 1, "part 0's answer was held back");
        var landed = await Page.EvaluateAsync<string[]>("() => window.__landed");
        _output.WriteLine("parts landed in the order " + string.Join(", ", landed));
        Assert.True(landed.Length >= 2 && landed[0] != "0", "a later part landed before part 0");
        Assert.Equal(0, watch.Count);
    }

    // F1 through the Wide payload width's refine pass, which redraws by the note's own path after the queue's redraw
    [Fact]
    public async Task The_wide_width_never_shows_the_engines_colours()
    {
        await OpenLongAsync();
        await Page.EvaluateAsync(WatchFramesJs);
        // every swap of the diagram's pictures counted: the queue's, then the refine pass's
        await Page.EvaluateAsync("""
            () => {
                var c = document.querySelector('.plantuml-browser[data-diagram-type=plantuml]');
                window.__swaps = 0;
                new MutationObserver(function (records) {
                    if (records.some(function (r) { return Array.prototype.some.call(r.removedNodes, function (n) { return n.nodeType === 1 && (n.matches('svg') || n.matches('.puml-fragment')); }); })) window.__swaps++;
                }).observe(c, { childList: true });
                window.__before = c.querySelector('svg');
                var s = document.querySelector('.toolbar-right .note-width-select');
                s.value = 'full';
                window._setNoteWidth(s);
            }
            """);
        await Page.WaitForFunctionAsync("() => window.__swaps >= 2", null, new() { Timeout = 60_000, PollingInterval = 200 });
        await WaitForDrawnPartsAsync();
        var watch = await StopAsync("__frames");

        Assert.Equal(0, watch.Count);
    }

    // F3: a diagram redrawn behind another view has no box, and is coloured in the frame its view shows again
    [Fact]
    public async Task A_diagram_redrawn_behind_another_view_is_coloured_in_the_frame_it_shows()
    {
        await Page.GotoReportAsync(Dense.Value, new() { Timeout = PageOpenTimeout });
        await Page.EvaluateAsync("""
            () => {
                var s = document.querySelector('details.scenario');
                s.closest('details.feature').open = true;
                s.open = true;
                s.scrollIntoView({ block: 'start' });
            }
            """);
        await Page.WaitForFunctionAsync(
            "() => !window._plantumlRendering && !!document.querySelector('details.scenario .diagram-view-seq .plantuml-browser svg[data-k-roles]')",
            null, new() { Timeout = 120_000, PollingInterval = 200 });
        var other = Page.Locator("details.scenario").First.Locator(".diagram-toggle-btn:not([data-dtype='seq'])").First;
        await other.ClickAsync();
        await Page.EvaluateAsync("() => { window.__before = document.querySelector('details.scenario .diagram-view-seq .plantuml-browser svg'); }");

        await Page.EvaluateAsync("() => document.querySelector('.toolbar-row .toggle-btn[data-toggle=\"databases\"]').click()");
        await Page.WaitForFunctionAsync("""
            () => !document.querySelector('.toggle-btn[data-toggle="databases"][data-pending]') && !window._plantumlRendering
                && !document.contains(window.__before) && !!document.querySelector('details.scenario .diagram-view-seq .plantuml-browser svg')
            """, null, new() { Timeout = 120_000, PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('details.scenario .diagram-view-seq .plantuml-browser svg').getBoundingClientRect().width === 0"),
            "the diagram was redrawn while its view was hidden");

        await Page.EvaluateAsync(WatchResizesJs);
        await Page.Locator("details.scenario").First.Locator(".diagram-toggle-btn[data-dtype='seq']").ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('details.scenario .diagram-view-seq .plantuml-browser svg[data-k-roles]')",
            null, new() { Timeout = 30_000, PollingInterval = 200 });
        var watch = await StopAsync("__resizes");

        Assert.True(watch.Reports > 0, "the picture's box was reported");
        Assert.Equal(0, watch.Count);
    }

    // F3: a report control redraws every diagram while the reader is on the Timeline tab, so the Features tab's pictures
    // are drawn with no box; a Timeline name's link then shows one, and a reader's scroll reaches another further down,
    // and neither shows its picture in the engine's colours (until 5.9.1 each was tagged only after the first frame showing
    // it had been painted)
    [Fact]
    public async Task A_far_scenario_reached_by_a_link_or_by_scrolling_never_shows_the_engines_colours()
    {
        await Page.GotoReportAsync(Dense.Value, new() { Timeout = PageOpenTimeout });
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature, details.scenario').forEach(function (d) { d.open = true; })");
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        // every picture drawn; not every one coloured, since one the page has not laid out yet (a far feature under
        // content-visibility) gets its role pass only when it gets a box, which is this fact's case (a CI run timed out
        // here waiting for the colours, 5.10.1)
        await Page.WaitForFunctionAsync("""
            () => !window._plantumlRendering && Array.prototype.every.call(document.querySelectorAll('.diagram-view-seq .plantuml-browser'),
                function (c) { return !!c.querySelector('svg'); })
            """, null, new() { Timeout = 300_000, PollingInterval = 200 });
        await Page.Locator("#tab-timeline").ClickAsync();
        await Page.EvaluateAsync("() => document.querySelector('.toolbar-row .toggle-btn[data-toggle=\"databases\"]').click()");
        await Page.WaitForFunctionAsync("() => !document.querySelector('.toggle-btn[data-toggle=\"databases\"][data-pending]') && !window._plantumlRendering",
            null, new() { Timeout = 300_000, PollingInterval = 200 });
        var waiting = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('details.scenario'))
                .filter(function (s) { var svg = s.querySelector('.diagram-view-seq .plantuml-browser svg'); return svg && !svg.getAttribute('data-k-roles'); })
                .map(function (s) { return s.getAttribute('data-stable-id'); })
            """);
        Assert.True(waiting.Length >= 2, $"the pictures redrawn behind the Timeline tab wait for a box: {waiting.Length}");

        await Page.EvaluateAsync(WatchResizesJs);
        // the first by its Timeline name, a link (#sid-…) the page's own handler follows to the Features tab
        await Page.Locator($".timeline-label[href='#sid-{waiting[0]}']").First.ClickAsync();
        await Page.WaitForFunctionAsync("id => !!document.querySelector('details.scenario[data-stable-id=\"' + id + '\"] .diagram-view-seq .plantuml-browser svg[data-k-roles]')",
            waiting[0], new() { Timeout = 30_000, PollingInterval = 200 });
        // then a reader's scroll down to the last
        await Page.EvaluateAsync("""
            async id => {
                var target = document.querySelector('details.scenario[data-stable-id="' + id + '"]');
                for (var k = 0; k < 600 && target.getBoundingClientRect().top > innerHeight / 2; k++) {
                    window.scrollBy(0, 240);
                    await new Promise(function (res) { requestAnimationFrame(function () { requestAnimationFrame(res); }); });
                }
            }
            """, waiting[^1]);
        await Page.WaitForFunctionAsync("id => !!document.querySelector('details.scenario[data-stable-id=\"' + id + '\"] .diagram-view-seq .plantuml-browser svg[data-k-roles]')",
            waiting[^1], new() { Timeout = 30_000, PollingInterval = 200 });
        var watch = await StopAsync("__resizes");

        Assert.True(watch.Reports >= 2, "the pictures' boxes were reported");
        Assert.Equal(0, watch.Count);
    }

    // the queue's path (a report control), single pictures and parts arriving in order: coloured before they show since
    // 5.1.1, and kept so
    [Fact]
    public async Task The_report_controls_never_show_the_engines_colours()
    {
        await OpenLongAsync();
        await Page.EvaluateAsync(WatchFramesJs);
        await Page.EvaluateAsync("() => { window.__before = document.querySelector('.plantuml-browser[data-diagram-type=plantuml] svg'); document.querySelector('.toolbar-row .toggle-btn[data-toggle=\"databases\"]').click(); }");
        await Page.WaitForFunctionAsync("() => !document.querySelector('.toggle-btn[data-toggle=\"databases\"][data-pending]') && !document.contains(window.__before)",
            null, new() { Timeout = 120_000, PollingInterval = 200 });
        await WaitForDrawnPartsAsync();
        var watch = await StopAsync("__frames");

        Assert.Equal(0, watch.Count);
    }

    // E15: the outline and the calibration a diagram in parts is lined up from are drawn aside, where nobody sees them,
    // and get no role pass (main-thread time on every drawing for pictures nobody sees)
    [Fact]
    public async Task The_aside_drawings_get_no_role_pass()
    {
        await Page.GotoReportAsync(Long.Value, new() { Timeout = PageOpenTimeout });
        // made after the page's own observers, so it reads each aside picture after the role pass would have tagged it
        await Page.EvaluateAsync("""
            () => {
                window.__aside = [];
                new MutationObserver(function (records) {
                    records.forEach(function (r) {
                        Array.prototype.forEach.call(r.addedNodes, function (n) {
                            if (n.nodeType !== 1) return;
                            var svgs = n.tagName.toLowerCase() === 'svg' ? [n] : Array.from(n.querySelectorAll('svg'));
                            svgs.forEach(function (svg) {
                                var aside = svg.closest('[id^="kronikol-aside-"]');
                                if (aside) window.__aside.push({ tagged: !!svg.getAttribute('data-k-roles'), host: aside.hasAttribute('data-k-sequence') });
                            });
                        });
                    });
                }).observe(document.body, { subtree: true, childList: true });
            }
            """);
        await ShowLongDiagramAsync();
        await WaitForDrawnPartsAsync();
        var aside = await Page.EvaluateAsync<JsonElement>("() => window.__aside");

        Assert.True(aside.GetArrayLength() >= 1, "the diagram's parts were lined up from a drawing made aside");
        Assert.All(aside.EnumerateArray(), a =>
        {
            Assert.False(a.GetProperty("tagged").GetBoolean(), "an aside picture got the role pass");
            Assert.False(a.GetProperty("host").GetBoolean(), "an aside drawing was marked a sequence host");
        });
    }
}
