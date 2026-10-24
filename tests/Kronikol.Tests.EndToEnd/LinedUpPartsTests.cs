using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The parts of a diagram drawn in parts are lined up (<c>plans/ZOOM_EVERY_DIAGRAM_PLAN.md</c> R2, Q7 and Q12, facts of
/// section 6.3): the engine lays each part out on its own, so until 5.7.0 a participant stood at a different place in
/// each part. The page draws an outline of the whole diagram first, holds every part to the outline's gaps with a row of
/// hidden messages labelled with spaces of exact widths (the row goes only to the engine, Z12), and places each part by
/// its first lifeline.
/// </summary>
[Collection(PlaywrightCollections.Zoom)]
public class LinedUpPartsTests : PlaywrightTestBase
{
    private readonly ITestOutputHelper _output;

    public LinedUpPartsTests(PlaywrightFixture fixture, ITestOutputHelper output) : base(fixture) => _output = output;

    /// <summary>
    /// Three parts whose widest gaps fall in different parts: a wide label between the caller and the gateway in the
    /// first, between the service and the store in the second, and between the gateway and the service in the third. A
    /// note on every reply.
    /// </summary>
    internal static string LinedUpSource() =>
        ReportTestHelper.PartsSource(30, new Dictionary<int, int> { [3] = 100, [14] = 100, [28] = 100 }, replyNotes: true);

    private const int LinedUpPartHeight = 3200;

    private async Task OpenAsync(string fileName, string? source = null, int partHeight = LinedUpPartHeight)
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, fileName,
            source ?? LinedUpSource(), partHeight));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await ZoomFacts.WaitForSliderAsync(Page, 0);
    }

    /// <summary>Each part's lifelines where the reader sees them: screen x, left to right.</summary>
    private Task<double[][]> LifelinesOnScreenAsync() => Page.EvaluateAsync<double[][]>("""
        () => {
            const c = document.querySelector("[data-diagram-type='plantuml']");
            return Array.from(c.querySelectorAll(':scope > .puml-fragment > svg')).map(svg => {
                const r = svg.getBoundingClientRect(), k = r.width / svg.width.baseVal.value;
                return window._lifelinesOf(svg).map(x => r.left + x * k);
            });
        }
        """);

    /// <summary>The widest any lifeline of the first <paramref name="count"/> stands from where it stands in another part.</summary>
    private static double WorstOffset(double[][] parts, int count)
    {
        var worst = 0.0;
        for (var i = 0; i < count; i++)
        {
            var xs = parts.Where(p => p.Length > i).Select(p => p[i]).ToArray();
            worst = Math.Max(worst, xs.Max() - xs.Min());
        }
        return worst;
    }

    private static string Show(double[][] parts) =>
        string.Join(" | ", parts.Select(p => string.Join(", ", p.Select(x => x.ToString("F1")))));

    // 1
    [Fact]
    public async Task Every_parts_lifelines_stand_at_the_same_places_the_first_included()
    {
        await OpenAsync("LinedUpPlaces.html");

        var parts = await LifelinesOnScreenAsync();
        Assert.Equal(3, parts.Length);
        Assert.All(parts, p => Assert.Equal(4, p.Length));
        Assert.True(WorstOffset(parts, 4) <= 0.5, $"every lifeline at one place in every part: {Show(parts)}");
    }

    // The control for 1: drawn without lining up, the same parts' lifelines stand apart, so fact 1 measures the page's work.
    [Fact]
    public async Task Without_lining_up_the_same_parts_lifelines_stand_apart()
    {
        await Page.AddInitScriptAsync("window.__kronikolLineUp = false;");
        await OpenAsync("LinedUpControl.html");

        var parts = await LifelinesOnScreenAsync();
        Assert.Equal(3, parts.Length);
        Assert.True(WorstOffset(parts, 4) > 5, $"the parts' lifelines differ: {Show(parts)}");
    }

    /// <summary>
    /// The diagram's parts drawn aside by the page's engine from the engine texts the page drew them from, with the row and
    /// without it: what each picture holds.
    /// </summary>
    private async Task<JsonElement[][]> DrawnBothWaysAsync() => JsonSerializer.Deserialize<JsonElement[][]>(await Page.EvaluateAsync<string>("""
        async () => {
            const c = document.querySelector("[data-diagram-type='plantuml']");
            const lineUp = c._lineUp;
            const draw = src => new Promise(resolve => {
                const div = document.createElement('div');
                div.id = 'lined-up-aside-' + Math.random().toString(36).slice(2);
                div.style.cssText = 'position:absolute;left:-100000px;top:0;visibility:hidden';
                document.body.appendChild(div);
                const done = () => {
                    const svg = div.querySelector('svg');
                    if (!svg && !div.textContent.trim()) return;
                    mo.disconnect();
                    div.remove();
                    const texts = Array.from(svg ? svg.querySelectorAll('text') : []).map(t => t.textContent);
                    resolve(svg ? {
                        polygon: svg.querySelectorAll('polygon').length, path: svg.querySelectorAll('path').length,
                        line: svg.querySelectorAll('line').length, rect: svg.querySelectorAll('rect').length,
                        text: texts.length, numbers: texts.filter(t => /^\d+$/.test(t.trim())).join(','),
                        underscores: texts.filter(t => /_{3,}/.test(t)).length, height: svg.height.baseVal.value
                    } : { error: div.textContent.trim().slice(0, 200) });
                };
                const mo = new MutationObserver(done);
                mo.observe(div, { childList: true, subtree: true, characterData: true });
                window.plantuml.render(src.split('\n'), div.id);
                done();
            });
            const out = [];
            for (let k = 0; k < lineUp.engine.length; k++) out.push([await draw(lineUp.engine[k]), await draw(lineUp.plain[k])]);
            return JSON.stringify(out);
        }
        """))!;

    // 2
    [Fact]
    public async Task The_row_draws_nothing_and_makes_each_part_only_its_height_taller()
    {
        await OpenAsync("LinedUpRowDrawsNothing.html");
        var parts = await DrawnBothWaysAsync();
        Assert.Equal(3, parts.Length);

        for (var k = 0; k < parts.Length; k++)
        {
            var (with, without) = (parts[k][0], parts[k][1]);
            foreach (var kind in new[] { "polygon", "path", "line", "rect", "text" })
                Assert.True(with.GetProperty(kind).GetInt32() == without.GetProperty(kind).GetInt32(),
                    $"part {k + 1}: as many {kind}s with the row as without it ({with.GetProperty(kind)} against {without.GetProperty(kind)})");
            Assert.Equal(0, with.GetProperty("underscores").GetInt32());
            var taller = with.GetProperty("height").GetDouble() - without.GetProperty("height").GetDouble();
            Assert.True(taller is >= 15 and <= 40, $"part {k + 1} is the row's height taller: {taller:F1} px");
        }
    }

    // 3
    [Fact]
    public async Task Each_message_keeps_the_number_it_has_without_the_row()
    {
        await OpenAsync("LinedUpNumbers.html");
        var parts = await DrawnBothWaysAsync();

        for (var k = 0; k < parts.Length; k++)
        {
            var with = parts[k][0].GetProperty("numbers").GetString();
            Assert.False(string.IsNullOrEmpty(with), $"part {k + 1} has numbered messages");
            Assert.Equal(parts[k][1].GetProperty("numbers").GetString(), with);
        }
    }

    /// <summary>The text of the tab an item opens.</summary>
    private async Task<string> OpenedTextAsync(int part, string submenu, string item)
    {
        var tab = Page.Context.WaitForPageAsync();
        await ZoomFacts.MenuAsync(Page, part, submenu, item);
        var page = await tab;
        await page.WaitForFunctionAsync("() => !!document.querySelector('pre')", null, new() { Timeout = 10000, PollingInterval = 200 });
        var text = await page.EvaluateAsync<string>("() => document.querySelector('pre').textContent");
        await page.CloseAsync();
        return text;
    }

    private static void AssertNoAddedLine(string what, string text)
    {
        foreach (var added in new[] { "-[hidden]>", "autonumber stop", "<space:", "<color:transparent>", "hide footbox" })
            Assert.False(text.Contains(added, StringComparison.Ordinal), $"{what} holds no \"{added}\"");
    }

    // 4
    [Fact]
    public async Task The_source_items_give_the_diagram_as_written_with_none_of_the_lines_the_page_added()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await OpenAsync("LinedUpSources.html");
        // the report's notes start truncated; one of the second part's is expanded, so the full and current sources differ
        await ZoomFacts.ScrollToPartAsync(Page, 1);
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector(\"[data-diagram-type='plantuml']\").querySelectorAll(':scope > .puml-fragment')[1].querySelector('.note-hover-rect')",
            null, new() { Timeout = 30000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => { window.__before = document.querySelector(\"[data-diagram-type='plantuml']\").querySelectorAll(':scope > .puml-fragment > svg')[1]; }");
        // JS dispatch: an SVG <text> would take a pointer's double-click
        await Page.EvaluateAsync("""
            () => document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment')[1]
                .querySelector('.note-hover-rect').dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }))
            """);
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before)", null, new() { Timeout = 60000, PollingInterval = 200 });
        await ZoomFacts.WaitForSliderAsync(Page, 0);

        var expected = await Page.EvaluateAsync<string[]>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                return [c._noteOriginalSource || c.getAttribute('data-plantuml'), c.getAttribute('data-plantuml')];
            }
            """);

        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");
        await ZoomFacts.MenuAsync(Page, 1, "Copy PlantUML source", "Copy full PlantUML source");
        var full = await ZoomFacts.ClipboardTextAsync(Page, "@startuml");
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");
        await ZoomFacts.MenuAsync(Page, 1, "Copy PlantUML source", "Copy current PlantUML source");
        var current = await ZoomFacts.ClipboardTextAsync(Page, "@startuml");
        var openedFull = await OpenedTextAsync(1, "Open PlantUML source in new tab", "Open full PlantUML in new tab");
        var openedCurrent = await OpenedTextAsync(1, "Open PlantUML source in new tab", "Open current PlantUML in new tab");

        foreach (var (what, text) in new[] { ("the full source copied", full), ("the current source copied", current),
                     ("the full source opened", openedFull), ("the current source opened", openedCurrent) })
        {
            Assert.StartsWith("@startuml", text);
            AssertNoAddedLine(what, text);
        }
        Assert.Equal(expected[0].Replace("\r", ""), full.Replace("\r", ""));
        Assert.Equal(expected[1].Replace("\r", ""), current.Replace("\r", ""));
        Assert.Equal(full.Replace("\r", ""), openedFull.Replace("\r", ""));
        Assert.Equal(current.Replace("\r", ""), openedCurrent.Replace("\r", ""));
    }

    // 5
    [Fact]
    public async Task Copy_as_SVG_of_a_lined_up_diagram_carries_its_own_source_with_no_row()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await OpenAsync("LinedUpCopySvg.html");
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");

        await ZoomFacts.MenuAsync(Page, 1, "Copy image", "Copy as SVG");
        var svg = await ZoomFacts.ClipboardTextAsync(Page, "<svg");

        var sources = await ZoomFacts.PlantUmlSourcesAsync(Page, svg);
        Assert.Single(sources);
        AssertNoAddedLine("the copy's source", sources[0]);
        var source = await Page.EvaluateAsync<string>("() => document.querySelector(\"[data-diagram-type='plantuml']\").getAttribute('data-plantuml')");
        Assert.Equal(source.Replace("\r", ""), sources[0].Replace("\r", ""));
    }

    /// <summary>The source of <see cref="LinedUpSource"/> with notes longer than the report's ten lines, which it truncates.</summary>
    internal static string LongNotesSource() =>
        ReportTestHelper.PartsSource(30, new Dictionary<int, int> { [3] = 100, [14] = 100, [28] = 100 }, replyNotes: true, extraNoteFields: 16);

    /// <summary>The source of the toggle fact: twelve calls, notes longer than ten lines, a wide label in each gap.</summary>
    internal static string ToggleSource() =>
        ReportTestHelper.PartsSource(12, new Dictionary<int, int> { [2] = 100, [6] = 100, [10] = 100 }, replyNotes: true, extraNoteFields: 16);

    /// <summary>The text "Copy current box text" gives for a note of the second part, the report's notes truncated as they start.</summary>
    private async Task<string> SecondPartsNoteTextAsync()
    {
        await ZoomFacts.ScrollToPartAsync(Page, 1);
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");
        await Page.EvaluateAsync("""
            () => {
                const part = document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment')[1];
                const groups = window._findNoteGroups(part.querySelector(':scope > svg'));
                const path = (groups[1] || groups[0]).paths[0], r = path.getBoundingClientRect();
                path.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + r.width / 2, clientY: r.top + 6 }));
            }
            """);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });
        var parent = menu.Locator(".submenu-parent", new() { HasTextString = "Copy box text" }).First;
        await parent.HoverAsync();
        await parent.Locator(".submenu").GetByText("Copy current box text", new() { Exact = true }).First.ClickAsync();
        return await Page.EvaluateAsync<string>("""
            async () => {
                for (let k = 0; k < 50; k++) {
                    const text = await navigator.clipboard.readText();
                    if (text !== 'nothing copied yet') return text;
                    await new Promise(res => setTimeout(res, 200));
                }
                return '';
            }
            """);
    }

    // 6
    [Fact]
    public async Task No_part_holds_the_row_and_a_second_part_notes_current_text_is_as_without_lining_up()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await OpenAsync("LinedUpNoteText.html", LongNotesSource());

        var stored = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment'))
                .map(p => p.getAttribute('data-plantuml'))
            """);
        Assert.True(stored.Length >= 2, $"drawn in {stored.Length} parts");
        for (var k = 0; k < stored.Length; k++) AssertNoAddedLine($"part {k + 1}'s source", stored[k]);
        Assert.True(await Page.EvaluateAsync<bool>("() => !!document.querySelector(\"[data-diagram-type='plantuml']\")._lineUp"),
            "the diagram was lined up");
        var lined = await SecondPartsNoteTextAsync();

        await Page.AddInitScriptAsync("window.__kronikolLineUp = false;");
        await OpenAsync("LinedUpNoteTextPlain.html", LongNotesSource());
        var plain = await SecondPartsNoteTextAsync();

        Assert.False(string.IsNullOrWhiteSpace(lined), "the note's current text is copied");
        Assert.Equal(plain, lined);
    }

    // 7
    [Fact]
    public async Task A_toggle_that_draws_the_diagram_again_in_parts_lines_its_new_parts_up_and_one_that_leaves_one_picture_adds_no_row()
    {
        // notes longer than the report's ten lines: truncated (as the report starts) in two parts, expanded in more, and
        // collapsed in one
        await OpenAsync("LinedUpToggle.html", ToggleSource(), partHeight: 2000);
        Assert.True((await ZoomFacts.ReadAsync(Page, 0)).Levels.Length >= 2, "truncated, the diagram is drawn in parts");
        await Page.EvaluateAsync("() => { window.__before = document.querySelector(\"[data-diagram-type='plantuml']\").querySelectorAll(':scope > .puml-fragment > svg')[1]; }");
        await Page.EvaluateAsync("s => window._setReportDetails(s)", "expanded");
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before)", null, new() { Timeout = 60000, PollingInterval = 200 });
        await ZoomFacts.WaitForSliderAsync(Page, 0);

        var parts = await LifelinesOnScreenAsync();
        Assert.True(parts.Length >= 2, $"drawn again in {parts.Length} parts");
        Assert.True(WorstOffset(parts, 4) <= 0.5, $"the new parts line up: {Show(parts)}");

        await Page.EvaluateAsync("s => window._setReportDetails(s)", "collapsed");
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                // one picture: the box's own, or one part's (a diagram drawn in parts before is drawn again in parts)
                return !c._noteRendering && c.querySelectorAll(':scope > svg, :scope > .puml-fragment > svg').length === 1
                    && c.querySelectorAll(':scope > .puml-fragment').length <= 1;
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        var whole = await Page.EvaluateAsync<string>("""
            () => {
                const svg = document.querySelector("[data-diagram-type='plantuml']").querySelector(':scope > svg, :scope > .puml-fragment > svg');
                const walker = document.createTreeWalker(svg, NodeFilter.SHOW_COMMENT);
                let c, out = '';
                while ((c = walker.nextNode())) if (/^\?plantuml-src /.test(c.data)) out = '<' + c.data + '>';
                return out;
            }
            """);
        var drawnFrom = await ZoomFacts.PlantUmlSourcesAsync(Page, whole);
        Assert.Single(drawnFrom);
        AssertNoAddedLine("the one picture's engine text", drawnFrom[0]);
    }

    // 8
    [Fact]
    public async Task Parts_that_disagree_once_drawn_are_drawn_again_from_their_own_gaps_and_line_up()
    {
        // the outline's gaps halved: the row is too short, so the parts disagree once drawn
        await Page.AddInitScriptAsync("window.__kronikolOutlineGapScale = 0.5;");
        await OpenAsync("LinedUpSecondDrawing.html");
        await Page.WaitForFunctionAsync("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                return c._lineUp && c._lineUp.pass === 2 && !(c._lineUpPending > 0);
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await ZoomFacts.WaitForSliderAsync(Page, 0);

        var parts = await LifelinesOnScreenAsync();
        Assert.True(WorstOffset(parts, 4) <= 0.5, $"drawn again, the parts line up: {Show(parts)}");
    }

    // 9
    [Fact]
    public async Task A_lined_up_diagram_starts_at_its_fit_level_all_of_it_in_view()
    {
        await OpenAsync("LinedUpFit.html");

        var d = await ZoomFacts.ReadAsync(Page, 0);
        Assert.True(d.Min < 100, $"the lined-up diagram is wider than its box: lowest level {d.Min}");
        Assert.Equal(d.Min, d.Slider);
        Assert.True(d.ScrollWidth <= d.ClientWidth, $"no scroll across: {d.ScrollWidth} against {d.ClientWidth}");
        var inView = await Page.EvaluateAsync<bool>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']"), cr = c.getBoundingClientRect();
                return Array.from(c.querySelectorAll(':scope > .puml-fragment > svg')).every(s => {
                    const r = s.getBoundingClientRect();
                    return r.left >= cr.left - 0.5 && r.right <= cr.right + 0.5;
                });
            }
            """);
        Assert.True(inView, "every part is inside the box across");
    }

    /// <summary>
    /// How long the report's notes control takes to draw the diagram again, in the page's own clock: from the call to the
    /// frame after its new parts are in.
    /// </summary>
    private static Task<double> RedrawTimeAsync(IPage page, string details) => page.EvaluateAsync<double>("""
        (details) => new Promise(done => {
            const c = document.querySelector("[data-diagram-type='plantuml']");
            const before = c.querySelector(':scope > .puml-fragment > svg');
            const t = performance.now();
            const check = () => {
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                if (!c._noteRendering && !document.contains(before) && parts.length && parts.every(p => p.querySelector(':scope > svg')))
                    requestAnimationFrame(() => done(performance.now() - t));
                else setTimeout(check, 5);
            };
            window._setReportDetails(details);
            check();
        })
        """, details);

    // 10. One page, lining up switched off and on between toggles (window.__kronikolLineUp) once every picture is in the
    // page's cache: two pages timed against each other passed one run and failed the next, the same toggle taking 800 to
    // 1,190 ms. What a toggle waits for besides its own work (a collection, a frame between two of its tasks) only adds
    // time, so each way is held to the quicker half of its toggles of each kind.
    [Fact]
    public async Task Lining_up_a_fifteen_part_diagram_costs_a_toggle_no_more_than_five_percent()
    {
        await Page.AddInitScriptAsync(BrowserRenderWorkerTests.BenchInitWithProbe);
        var source = ReportTestHelper.PartsSource(735, new Dictionary<int, int> { [2] = 180 }, replyNotes: true);
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, "LinedUpBench.html", source, 12000));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => { document.querySelector('details.feature').open = true; document.querySelector('details.scenario').open = true; window._renderDiagramsInContainer(document.body); }");
        await ZoomFacts.WaitForSliderAsync(Page, 0, timeoutMs: 180000);
        Assert.True(await Page.EvaluateAsync<bool>("() => !!document.querySelector(\"[data-diagram-type='plantuml']\")._lineUp"), "the diagram is lined up");

        // what the page does after a toggle (note buttons placed slice by slice, the far parts' hover work) ends before the
        // next one starts, so that no toggle pays for the one before it
        Task SettleAsync() => Page.WaitForFunctionAsync("""
            () => {
                var tasks = window.__bench.longTasks, now = performance.now();
                var last = tasks.length ? tasks[tasks.length - 1].start + tasks[tasks.length - 1].duration : 0;
                return now - last > 500;
            }
            """, null, new() { Timeout = 60_000, PollingInterval = 200 });
        var states = new[] { "collapsed", "truncated" };
        var times = (from lineUp in new[] { true, false } from state in states select (lineUp, state))
            .ToDictionary(k => k, _ => new List<double>());
        // the first round draws every picture both ways, for the page's cache, and is not counted; the order alternates,
        // so that neither way always follows the other
        for (var round = 0; round < 8; round++)
            foreach (var lineUp in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
            {
                await Page.EvaluateAsync("on => { window.__kronikolLineUp = on ? undefined : false; }", lineUp);
                foreach (var state in states)
                {
                    await SettleAsync();
                    var ms = await RedrawTimeAsync(Page, state);
                    if (round > 0) times[(lineUp, state)].Add(ms);
                }
            }
        Assert.True(await Page.EvaluateAsync<bool>("() => !!document.querySelector(\"[data-diagram-type='plantuml']\")._lineUp"), "the last toggle lined the parts up");
        var probe = await Page.EvaluateAsync<double[]>("() => window.__bench.probe");
        var stretch = ContentionScale.Stretch(probe.ToList());
        double Quicker(bool lineUp) => states.Sum(s =>
        {
            var v = times[(lineUp, s)].OrderBy(x => x).ToList();
            return v.Take((v.Count + 1) / 2).Average();
        });
        var (withRow, without) = (Quicker(true), Quicker(false));
        foreach (var (k, v) in times)
            _output.WriteLine($"{(k.lineUp ? "lined up" : "not")}, {k.state}: {string.Join(", ", v.Select(x => x.ToString("F0")))} ms");
        _output.WriteLine($"quicker half of each kind, summed: {withRow:F0} ms lined up, {without:F0} ms not; stretch {stretch:F2}");
        // 5%, and a frame's worth for each toggle's clock
        var budget = without * 1.05 + 2 * 17 * stretch;
        Assert.True(withRow <= budget,
            $"toggles lined up took {withRow:F0} ms against {without:F0} ms without, the quicker half of each kind (budget {budget:F0} ms)");
    }

    // 11
    [Fact]
    public async Task The_row_is_exact_a_lined_up_diagram_is_no_wider_than_drawn_whole_by_more_than_a_pixel_a_gap()
    {
        await OpenAsync("LinedUpExact.html");

        var r = await Page.EvaluateAsync<double[]>("""
            async () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const pics = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
                const lined = Math.max(...pics.map(s => s.width.baseVal.value + (s.parentElement._kShift || 0)));
                const src = c.getAttribute('data-plantuml');
                const whole = await new Promise(resolve => {
                    const div = document.createElement('div');
                    div.id = 'lined-up-whole';
                    div.style.cssText = 'position:absolute;left:-100000px;top:0;visibility:hidden';
                    document.body.appendChild(div);
                    const mo = new MutationObserver(() => {
                        const svg = div.querySelector('svg');
                        if (!svg) return;
                        mo.disconnect();
                        resolve(svg.width.baseVal.value);
                        div.remove();
                    });
                    mo.observe(div, { childList: true, subtree: true });
                    window.plantuml.render(src.split('\n'), div.id);
                });
                return [lined, whole, c._lineUp ? c._lineUp.names.length - 1 : -1];
            }
            """);
        var (lined, whole, gaps) = (r[0], r[1], (int)r[2]);
        _output.WriteLine($"lined up {lined:F1} px, drawn whole {whole:F1} px, {gaps} gaps");
        Assert.Equal(3, gaps);
        Assert.True(lined <= whole + gaps + 0.5, $"lined up {lined:F1} px against {whole:F1} px drawn whole");
    }

    /// <summary>
    /// Participants declared otherwise than the generator does: one with no <c>as</c> and one in a <c>box</c>, and one
    /// created partway with <c>create</c>; the first gap held open by a label far wider than any one row of underscores
    /// could hold (2,000 underscores drew the engine's error picture), in the first part only; and a note left of the
    /// first participant in a later part only, which moves that part's lifelines right of the others'.
    /// </summary>
    internal static string EdgeSource()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("@startuml\n!pragma teoz true\nautonumber 1\n\nbox \"Edge\"\nactor Client\nparticipant \"Web Gateway\" as g\nend box\nparticipant Service\ndatabase \"Store\" as d\n\n");
        sb.Append("Client -> g : call1 " + new string('w', 260) + "\ng --> Client : ok1\n");
        for (var k = 2; k <= 30; k++)
        {
            if (k == 15) sb.Append("create participant \"Late\" as l\ng -> l : make\n");
            // room left of the first lifeline in a later part only: the page places each part by its first lifeline
            if (k == 24) sb.Append("note left of Client : a note left of the first participant " + new string('n', 40) + "\n");
            var (from, to) = (k % 3) switch { 0 => ("Client", "g"), 1 => ("g", "Service"), _ => ("Service", "d") };
            sb.Append($"{from} -> {to} : call{k}\n{to} --> {from} : ok{k}\n");
        }
        sb.Append("@enduml");
        return sb.ToString();
    }

    // Q7's first step: a gap wider than one label of underscores, and participants declared with no `as`, in a `box` or by
    // `create`
    [Fact]
    public async Task A_gap_wider_than_any_label_of_underscores_and_participants_declared_otherwise_line_up()
    {
        await OpenAsync("LinedUpEdge.html", EdgeSource(), partHeight: 900);

        var errors = await Page.EvaluateAsync<int>("""
            () => Array.from(document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment'))
                .filter(p => !p.querySelector(':scope > svg')).length
            """);
        Assert.Equal(0, errors);
        // every part declares the participants, the box with them: until 5.7.0 a box ended the part's header, so the parts
        // after the first declared none and drew only those their own messages named
        var stored = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment'))
                .map(p => p.getAttribute('data-plantuml'))
            """);
        Assert.All(stored, s =>
        {
            foreach (var line in new[] { "box \"Edge\"", "actor Client", "participant \"Web Gateway\" as g", "end box", "participant Service", "database \"Store\" as d" })
                Assert.Contains(line, s);
        });
        var parts = await LifelinesOnScreenAsync();
        Assert.True(parts.Length >= 3, $"drawn in {parts.Length} parts");
        var gaps = await Page.EvaluateAsync<double[]>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                return Array.from(c.querySelectorAll(':scope > .puml-fragment > svg')).map(svg => { const xs = window._lifelinesOf(svg); return xs[1] - xs[0]; });
            }
            """);
        Assert.All(gaps, g => Assert.True(g > 2000, $"the first gap is held open in every part: {string.Join(", ", gaps.Select(x => x.ToString("F0")))}"));
        Assert.True(WorstOffset(parts, 4) <= 0.5, $"the declared participants line up: {Show(parts)}");
    }
}
