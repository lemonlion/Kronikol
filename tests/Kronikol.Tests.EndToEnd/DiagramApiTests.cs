using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The page's diagram API, <c>window.kronikol_diagrams</c> (plans/SEARCH_PLAN.md 4.6.5): <c>whenDrawn</c>, a promise kept
/// once a diagram brought near is drawn for the view it is in; <c>show</c>, which sets a scenario's own toggle states and
/// queues one redraw of its drawn diagrams, its diagrams drawn later taking them on their first draw; <c>openNote</c>,
/// which puts a note in a state through the render queue, waiting for draws in flight, or seeds it for a diagram not drawn
/// yet; and <c>findNote</c> and <c>findMessage</c>, which find a note by its index in the original source and the n-th
/// message whose label reads as a statement's, through the filters, the note states and the parts. The way to a search's
/// match is built on these, as the scenario's gear, the note buttons and the toggles are.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramApiTests : PlaywrightTestBase
{
    public DiagramApiTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>The report's start states for these facts: databases hidden, headers hidden, payloads truncated at 10.</summary>
    private static readonly ResolvedToggleDefaults Start = ResolvedToggleDefaults.BuiltIn with { DatabasesShown = false };

    /// <summary>A scenario of three calls: a GET of the orders, a database read and a GET of the menu.</summary>
    private static DiagramApiReports.Scenario Orders(string id, string name, int at = 0) => new(id, name,
    [
        .. DiagramApiReports.Call(id, at, "GET", "/api/orders", DiagramApiReports.Body(id + "orders", 3)),
        .. DiagramApiReports.Call(id, at + 1, "GET", "/orders-db/rows", DiagramApiReports.Body(id + "rows", 2), "Orders DB", database: true),
        .. DiagramApiReports.Call(id, at + 2, "GET", "/api/menu", DiagramApiReports.Body(id + "menu", 3))
    ]);

    /// <summary>A scenario whose diagram the page draws in parts: <paramref name="calls"/> GETs of a page each.</summary>
    private static DiagramApiReports.Scenario Pages(string id, string name, int calls) => new(id, name,
        [.. Enumerable.Range(1, calls).SelectMany(k =>
            DiagramApiReports.Call(id, k, "GET", $"/api/items?page={k}", DiagramApiReports.Body($"item{k:D2}", 3)))]);

    /// <summary>The page's helpers for these facts: a scenario by its name, its diagram, a note's original index by a
    /// key its source holds, the note drawn for an index, how many rows it paints and its painted text.</summary>
    private const string Helpers = """
        () => {
            var h = window.__api = {};
            h.scenario = (name) => Array.from(document.querySelectorAll('details.scenario'))
                .find(s => s.querySelector(':scope > summary').textContent.indexOf(name) >= 0);
            h.host = (name) => h.scenario(name).querySelector('.plantuml-browser');
            h.source = (host) => host._noteOriginalSource || host.getAttribute('data-plantuml');
            h.noteIndex = (host, key) => window._parseNoteBlocks(h.source(host))
                .findIndex(b => b.contentLines.some(l => l.indexOf(key) >= 0));
            h.svgs = (host) => {
                var parts = Array.from(host.querySelectorAll(':scope > .puml-fragment'));
                return (parts.length ? parts : [host]).map(p => p.querySelector(':scope > svg')).filter(s => s);
            };
            h.drawnNote = (host, idx) => {
                var found = null;
                h.svgs(host).forEach(svg => window._findNoteGroups(svg).forEach(g => {
                    var ix = window._noteIndicesOf(g);
                    if (!found && ix && ix.original === idx) found = g;
                }));
                return found;
            };
            h.rows = (g) => window._paintedNoteRowCount(g);
            h.text = (el) => (el.textContent || '').replace(/[\s​]+/g, '');
            h.groupText = (g) => g.texts.map(t => t.textContent).join('').replace(/[\s​]+/g, '');
            h.settled = (host) => host && host.dataset.rendered === '1' && !host._noteRendering && !window._plantumlRendering
                && !host.querySelector('.puml-fragment-new') && h.svgs(host).length > 0
                && Array.from(host.querySelectorAll(':scope > .puml-fragment')).every(p => p.dataset.rendered === '1');
            // counts the pictures put into a diagram (its own svg, or a part's)
            h.countSwaps = (host) => {
                var n = { count: 0 };
                new MutationObserver(ms => ms.forEach(m => m.addedNodes.forEach(a => {
                    if (a.nodeType === 1 && (a.localName === 'svg' || (a.classList && a.classList.contains('puml-fragment')))) n.count++;
                }))).observe(host, { childList: true });
                return n;
            };
            return true;
        }
        """;

    private async Task OpenAsync(string url)
    {
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync(Helpers);
    }

    private Task SettledAsync(string scenario) => Page.WaitForFunctionAsync(
        "name => window.__api.settled(window.__api.host(name))", scenario, new() { Timeout = 60000, PollingInterval = 200 });

    /// <summary>Opens the feature and the scenarios named, which the page then draws as they come near.</summary>
    private Task OpenScenariosAsync(params string[] names) => Page.EvaluateAsync("""
        names => {
            document.querySelectorAll('details.feature').forEach(f => f.open = true);
            names.forEach(n => { window.__api.scenario(n).open = true; });
        }
        """, names);

    /// <summary>Ten scenarios of three calls before <paramref name="last"/>, so that, all of them open, it is far below the window
    /// (the page orders scenarios by name, and draws the first one's diagrams as it opens).</summary>
    private static DiagramApiReports.Scenario[] TallPage(params DiagramApiReports.Scenario[] last)
    {
        var first = Enumerable.Range(1, 10).Select(k => Orders($"o{k}", $"Order {k}", k * 3)).ToArray();
        return [.. first, .. last];
    }

    [Fact]
    public async Task WhenDrawn_waits_for_a_diagram_brought_near_and_is_kept_once_every_part_is_drawn()
    {
        var url = DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_WhenDrawn.html",
            TallPage(Pages("far", "Zeta far pages", 14)), Start, partHeight: 900);
        await OpenAsync(url);
        await OpenScenariosAsync(Enumerable.Range(1, 10).Select(k => $"Order {k}").Append("Zeta far pages").ToArray());
        await SettledAsync("Order 1");
        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await TwoAnimationFramesAsync(Page);
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__api.host('Zeta far pages').getBoundingClientRect().top > innerHeight + 400"),
            "premise: the diagram is far below the window");

        await Page.EvaluateAsync("""
            () => {
                var host = window.__api.host('Zeta far pages');
                window.__drawn = null;
                window.kronikol_diagrams.whenDrawn(host).then(v => {
                    var parts = Array.from(host.querySelectorAll(':scope > .puml-fragment'));
                    window.__drawn = { v: v, parts: parts.length, every: parts.every(p => p.dataset.rendered === '1' && !!p.querySelector(':scope > svg')),
                        rendered: host.dataset.rendered || null, busy: !!host._noteRendering };
                });
            }
            """);
        await Page.WaitForTimeoutAsync(1200);
        Assert.Null(await Page.EvaluateAsync<string?>("() => window.__drawn && JSON.stringify(window.__drawn)"));
        Assert.Null(await Page.EvaluateAsync<string?>("() => window.__api.host('Zeta far pages').dataset.rendered || null"));

        await Page.EvaluateAsync("() => window.__api.host('Zeta far pages').scrollIntoView({ block: 'start' })");
        await Page.WaitForFunctionAsync("() => !!window.__drawn", null, new() { Timeout = 60000, PollingInterval = 200 });

        Assert.True(await Page.EvaluateAsync<bool>("() => window.__drawn.v"));
        Assert.True(await Page.EvaluateAsync<int>("() => window.__drawn.parts") >= 2, "premise: the diagram is drawn in parts");
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__drawn.every"), "a part was not drawn when the promise was kept");
        Assert.Equal("1", await Page.EvaluateAsync<string?>("() => window.__drawn.rendered"));
        Assert.False(await Page.EvaluateAsync<bool>("() => window.__drawn.busy"));
    }

    [Fact]
    public async Task WhenDrawn_gives_up_after_its_timeout_for_a_diagram_never_brought_near()
    {
        var url = DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_WhenDrawnTimeout.html", TallPage(Orders("far", "Zeta far order", 30)), Start);
        await OpenAsync(url);
        await OpenScenariosAsync(Enumerable.Range(1, 10).Select(k => $"Order {k}").Append("Zeta far order").ToArray());
        await SettledAsync("Order 1");
        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");

        var result = await Page.EvaluateAsync<string>("""
            async () => {
                var t0 = performance.now();
                var v = await window.kronikol_diagrams.whenDrawn(window.__api.host('Zeta far order'), { timeout: 400 });
                return v + ' after ' + (performance.now() - t0 >= 350 ? 'the timeout' : 'less');
            }
            """);

        Assert.Equal("false after the timeout", result);
    }

    [Fact]
    public async Task WhenDrawn_after_show_waits_for_the_redraw_show_queued()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_WhenDrawnShow.html", [Orders("a", "Order A")], Start));
        await OpenScenariosAsync("Order A");
        await SettledAsync("Order A");

        var seen = await Page.EvaluateAsync<string>("""
            async () => {
                var scenario = window.__api.scenario('Order A'), host = window.__api.host('Order A');
                var before = host.querySelector(':scope > svg');
                window.kronikol_diagrams.show(scenario, { databases: true });
                await window.kronikol_diagrams.whenDrawn(host);
                var now = host.querySelector(':scope > svg');
                return (now === before ? 'the old picture' : 'a new picture') + ', '
                    + (window.__api.text(now).indexOf('OrdersDB') >= 0 ? 'the database drawn' : 'no database');
            }
            """);

        Assert.Equal("a new picture, the database drawn", seen);
    }

    [Fact]
    public async Task WhenDrawn_is_kept_with_false_once_its_signal_aborts()
    {
        var url = DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_WhenDrawnAbort.html", TallPage(Orders("far", "Zeta far order", 30)), Start);
        await OpenAsync(url);
        await OpenScenariosAsync(Enumerable.Range(1, 10).Select(k => $"Order {k}").Append("Zeta far order").ToArray());
        await SettledAsync("Order 1");
        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");

        var result = await Page.EvaluateAsync<string>("""
            async () => {
                var stop = new AbortController(), t0 = performance.now();
                var drawn = window.kronikol_diagrams.whenDrawn(window.__api.host('Zeta far order'), { signal: stop.signal });
                setTimeout(() => stop.abort(), 300);
                var v = await drawn;
                return v + ' after ' + (performance.now() - t0 < 5000 ? 'the abort' : 'its timeout');
            }
            """);

        Assert.Equal("false after the abort", result);
    }

    [Fact]
    public async Task WhenDrawn_is_kept_for_a_flame_view_once_its_bars_are_drawn()
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, "DiagramApi_Flame.html", _ => { }));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        var bars = await Page.EvaluateAsync<int>("""
            async () => {
                var flame = document.querySelector('.diagram-view-flame .iflow-flame');
                var v = await window.kronikol_diagrams.whenDrawn(flame, { timeout: 30000 });
                return v ? flame.querySelectorAll('.iflow-flame-bar').length : -1;
            }
            """);

        Assert.True(bars > 0, $"the flame view's bars when the promise was kept: {bars}");
    }

    [Fact]
    public async Task Show_turns_on_a_scenario_s_states_with_one_redraw_and_leaves_the_report_and_other_scenarios_alone()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_Show.html", [Orders("a", "Order A"), Orders("b", "Order B", 10)], Start));
        await OpenScenariosAsync("Order A", "Order B");
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await SettledAsync("Order A");
        await SettledAsync("Order B");

        var changed = await Page.EvaluateAsync<string>("""
            async () => {
                var a = window.__api.host('Order A'), b = window.__api.host('Order B');
                window.__swapsA = window.__api.countSwaps(a);
                window.__swapsB = window.__api.countSwaps(b);
                window.__beforeB = b.querySelector(':scope > svg');
                var keys = await window.kronikol_diagrams.show(window.__api.scenario('Order A'), { databases: true, headers: true });
                await window.kronikol_diagrams.whenDrawn(a);
                return keys.slice().sort().join(',');
            }
            """);
        await Page.WaitForTimeoutAsync(1000);
        await SettledAsync("Order A");

        Assert.Equal("databases,headers", changed);
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__swapsA.count"));
        var a = await Page.EvaluateAsync<string>("() => window.__api.text(window.__api.host('Order A').querySelector(':scope > svg'))");
        Assert.Contains("OrdersDB", a);
        Assert.Contains("Content-Type=application/json", a);
        // the other scenario's diagram and the report's own toggles are as they were
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => window.__swapsB.count"));
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__api.host('Order B').querySelector(':scope > svg') === window.__beforeB"));
        Assert.DoesNotContain("OrdersDB", await Page.EvaluateAsync<string>("() => window.__api.text(window.__beforeB)"));
        Assert.False(await Page.EvaluateAsync<bool>("() => !!window._databasesVisible"));
        Assert.True(await Page.EvaluateAsync<bool>("() => !!window._headersHidden"));
        Assert.Equal("false", await Page.Locator(".toolbar-right .toggle-btn[data-toggle='databases']").First.GetAttributeAsync("aria-pressed"));
        Assert.Equal("false", await Page.Locator(".toolbar-right .toggle-btn[data-toggle='headers']").First.GetAttributeAsync("aria-pressed"));
        // the scenario's gear shows what it was set to; the other scenario's does not
        Assert.Equal("true,true,false,false", await Page.EvaluateAsync<string>("""
            () => ['Order A', 'Order B'].flatMap(n => ['databases', 'headers'].map(t =>
                window.__api.scenario(n).querySelector('.toggle-btn[data-toggle="' + t + '"]').getAttribute('aria-pressed'))).join(',')
            """));
    }

    [Fact]
    public async Task Show_reaches_a_diagram_the_scenario_draws_later()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_ShowLater.html", [Orders("a", "Order A"), Orders("b", "Order B", 10)], Start));
        await OpenScenariosAsync("Order A");
        await SettledAsync("Order A");
        Assert.Null(await Page.EvaluateAsync<string?>("() => window.__api.host('Order B').dataset.rendered || null"));

        await Page.EvaluateAsync("() => window.kronikol_diagrams.show(window.__api.scenario('Order B'), { databases: true, headers: true })");
        var drawn = await Page.EvaluateAsync<string>("""
            async () => {
                window.__api.scenario('Order B').open = true;
                var host = window.__api.host('Order B');
                host.scrollIntoView({ block: 'center' });
                await window.kronikol_diagrams.whenDrawn(host, { timeout: 60000 });
                return window.__api.text(host.querySelector(':scope > svg'));
            }
            """);

        Assert.Contains("OrdersDB", drawn);
        Assert.Contains("Content-Type=application/json", drawn);
    }

    /// <summary>The response note of the scenario's first call, by the key its body starts with.</summary>
    private Task<int> FirstResponseNoteAsync(string scenario, string key) =>
        Page.EvaluateAsync<int>("([n, k]) => window.__api.noteIndex(window.__api.host(n), k)", new object[] { scenario, key });

    [Fact]
    public async Task OpenNote_while_another_diagram_draws_waits_and_then_draws_the_note_in_that_state()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_OpenNoteWaits.html", [Orders("a", "Order A")], Start));
        await OpenScenariosAsync("Order A");
        await SettledAsync("Order A");
        var idx = await FirstResponseNoteAsync("Order A", "aorders_01");
        Assert.True(idx >= 0, "premise: a note holds the first response");
        Assert.True(await Page.EvaluateAsync<int>($"() => window.__api.rows(window.__api.drawnNote(window.__api.host('Order A'), {idx}))") > 1,
            "premise: the note is drawn open");

        // a drawing in flight, as the page's own draws hold it
        await Page.EvaluateAsync($$"""
            () => {
                window._plantumlRendering = true;
                window.__opened = null;
                window.__picture = window.__api.host('Order A').querySelector(':scope > svg');
                window.kronikol_diagrams.openNote(window.__api.host('Order A'), {{idx}}, { state: 'collapsed' }).then(v => { window.__opened = v; });
            }
            """);
        await Page.WaitForTimeoutAsync(600);
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__api.host('Order A').querySelector(':scope > svg') === window.__picture"),
            "the diagram was drawn again while another drawing was in flight");
        await Page.EvaluateAsync("() => { window._plantumlRendering = false; }");

        await Page.WaitForFunctionAsync("() => window.__opened !== null", null, new() { Timeout = 30000, PollingInterval = 200 });
        await SettledAsync("Order A");
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__opened"));
        Assert.Equal(1, await Page.EvaluateAsync<int>($"() => window.__api.rows(window.__api.drawnNote(window.__api.host('Order A'), {idx}))"));
    }

    [Fact]
    public async Task A_note_button_pressed_while_another_diagram_draws_is_not_lost()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_ButtonWaits.html", [Orders("a", "Order A")], Start));
        await OpenScenariosAsync("Order A");
        await SettledAsync("Order A");
        var idx = await FirstResponseNoteAsync("Order A", "aorders_01");

        await Page.EvaluateAsync($$"""
            () => {
                var g = window.__api.drawnNote(window.__api.host('Order A'), {{idx}});
                g.paths[0].dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
                var svg = g.paths[0].ownerSVGElement, b = g.paths[0].getBBox();
                var minus = Array.from(svg.querySelectorAll('.note-toggle-icon[data-note-btn="minus"]')).find(e => {
                    var r = e.getBBox(); return r.x >= b.x - 1 && r.x + r.width <= b.x + b.width + 1 && r.y >= b.y - 1 && r.y <= b.y + 40;
                });
                window._plantumlRendering = true;
                minus.querySelector('rect').dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
            }
            """);
        await Page.WaitForTimeoutAsync(600);
        await Page.EvaluateAsync("() => { window._plantumlRendering = false; }");

        await Page.WaitForFunctionAsync($"() => window.__api.settled(window.__api.host('Order A')) && window.__api.rows(window.__api.drawnNote(window.__api.host('Order A'), {idx})) === 1",
            null, new() { Timeout = 30000, PollingInterval = 200 });
    }

    [Fact]
    public async Task OpenNote_before_a_first_draw_seeds_the_note_and_the_diagram_is_drawn_once()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_OpenNoteSeed.html", [Orders("a", "Order A"), Orders("b", "Order B", 10)], Start));
        await OpenScenariosAsync("Order A");
        await SettledAsync("Order A");
        Assert.Null(await Page.EvaluateAsync<string?>("() => window.__api.host('Order B').dataset.rendered || null"));

        // the notes' indices, read from the diagram's source before the page has drawn it
        var result = await Page.EvaluateAsync<string>("""
            async () => {
                var host = window.__api.host('Order B');
                var blocks = window._parseNoteBlocks(await window.decompressGzipBase64(window._getPumlZ(host)));
                var at = (key) => blocks.findIndex(b => b.contentLines.some(l => l.indexOf(key) >= 0));
                window.__notes = { orders: at('borders_01'), menu: at('bmenu_01') };
                window.__swaps = window.__api.countSwaps(host);
                var opened = window.kronikol_diagrams.openNote(host, window.__notes.orders, { state: 'collapsed', format: 'json' });
                window.__api.scenario('Order B').open = true;
                host.scrollIntoView({ block: 'center' });
                var v = await opened;
                await window.kronikol_diagrams.whenDrawn(host, { timeout: 60000 });
                return String(v);
            }
            """);
        await Page.WaitForTimeoutAsync(1500);
        await SettledAsync("Order B");

        Assert.Equal("true", result);
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__swaps.count"));
        // the note seeded is folded and in JSON; the diagram's other payload is in the report's YAML
        Assert.Equal("0 json yaml", await Page.EvaluateAsync<string>("""
            () => {
                var host = window.__api.host('Order B'), n = window.__notes, f = host._noteFormats || {};
                return host._noteSteps[n.orders] + ' ' + (f[n.orders] || 'json') + ' ' + (f[n.menu] || 'json');
            }
            """));
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__api.rows(window.__api.drawnNote(window.__api.host('Order B'), window.__notes.orders))"));
    }

    /// <summary>
    /// openNote's width ('full' or 'default'), which the plan's openNote did not take; the note's own width button goes
    /// through the same redraw as its step and format. A note opened wide, after its diagram's first drawing and before
    /// it, is drawn with the wide note's class on its header alone. A wide note's diagram is drawn again once it is
    /// painted, by the pass that fits it to its column, and whenDrawn is kept after that drawing: no picture comes after it.
    /// </summary>
    [Fact]
    public async Task OpenNote_draws_a_note_wide_before_and_after_a_first_draw_and_whenDrawn_waits_for_its_last_drawing()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_OpenNoteWide.html", [Orders("a", "Order A"), Orders("b", "Order B", 10)], Start));
        await OpenScenariosAsync("Order A");
        await SettledAsync("Order A");
        Assert.Null(await Page.EvaluateAsync<string?>("() => window.__api.host('Order B').dataset.rendered || null"));

        var opened = await Page.EvaluateAsync<string>("""
            async () => {
                var api = window.kronikol_diagrams, h = window.__api;
                var wide = (host) => (host.getAttribute('data-plantuml').match(/note<<kronNoteWide>>/g) || []).length;
                // after the first drawing
                var a = h.host('Order A'), ia = h.noteIndex(a, 'aorders_01');
                var swapsA = h.countSwaps(a);
                var va = await api.openNote(a, ia, { width: 'full' });
                await api.whenDrawn(a);
                window.__late = { a: swapsA, aKept: swapsA.count };
                // before it: the note's index read from the source the page has not drawn yet
                var b = h.host('Order B');
                var blocks = window._parseNoteBlocks(await window.decompressGzipBase64(window._getPumlZ(b)));
                var ib = blocks.findIndex(x => x.contentLines.some(l => l.indexOf('borders_01') >= 0));
                var swapsB = h.countSwaps(b);
                var vb = await api.openNote(b, ib, { width: 'full' });
                h.scenario('Order B').open = true;
                b.scrollIntoView({ block: 'center' });
                await api.whenDrawn(b, { timeout: 60000 });
                window.__late.b = swapsB;
                window.__late.bKept = swapsB.count;
                return [va, (a._noteWidths || {})[ia], wide(a), vb, (b._noteWidths || {})[ib], wide(b)].join(',');
            }
            """);
        await Page.WaitForTimeoutAsync(2000);

        Assert.Equal("true,full,1,true,full,1", opened);
        Assert.Equal("0 drawn after, 0 drawn after", await Page.EvaluateAsync<string>("""
            () => (window.__late.a.count - window.__late.aKept) + ' drawn after, ' + (window.__late.b.count - window.__late.bKept) + ' drawn after'
            """));
    }

    /// <summary>
    /// What one task changes is drawn in one redraw (plans/SEARCH_PLAN.md 4.6.5, step 7: every change of the toggles and of
    /// the note for a diagram already drawn in one redraw, waited for): show and openNote called together draw the diagram
    /// once, with both changes.
    /// </summary>
    [Fact]
    public async Task Show_and_openNote_in_one_task_draw_a_diagram_once()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_OneRedraw.html", [Orders("a", "Order A")], Start));
        await OpenScenariosAsync("Order A");
        await SettledAsync("Order A");
        var idx = await FirstResponseNoteAsync("Order A", "aorders_01");
        Assert.True(await Page.EvaluateAsync<int>($"() => window.__api.rows(window.__api.drawnNote(window.__api.host('Order A'), {idx}))") > 1,
            "premise: the note is drawn open");

        var opened = await Page.EvaluateAsync<string>($$"""
            async () => {
                var host = window.__api.host('Order A'), api = window.kronikol_diagrams;
                window.__swaps = window.__api.countSwaps(host);
                var shown = api.show(window.__api.scenario('Order A'), { databases: true });
                var opened = api.openNote(host, {{idx}}, { state: 'collapsed' });
                await api.whenDrawn(host);
                return (await shown).join(',') + ' ' + (await opened);
            }
            """);
        await Page.WaitForTimeoutAsync(1000);
        await SettledAsync("Order A");

        Assert.Equal("databases true", opened);
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__swaps.count"));
        Assert.Contains("OrdersDB", await Page.EvaluateAsync<string>("() => window.__api.text(window.__api.host('Order A').querySelector(':scope > svg'))"));
        Assert.Equal(1, await Page.EvaluateAsync<int>($"() => window.__api.rows(window.__api.drawnNote(window.__api.host('Order A'), {idx}))"));
    }

    [Fact]
    public async Task OpenNote_draws_a_note_of_a_later_part_in_that_state()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_OpenNoteInPart.html", [Pages("p", "Pages", 14)], Start, partHeight: 900));
        await OpenScenariosAsync("Pages");
        await SettledAsync("Pages");
        var idx = await Page.EvaluateAsync<int>("() => window.__api.noteIndex(window.__api.host('Pages'), 'item13_01')");
        Assert.True(await Page.EvaluateAsync<int>($"() => window.__api.rows(window.kronikol_diagrams.findNote(window.__api.host('Pages'), {idx}))") > 1,
            "premise: the note is drawn open");

        var drawn = await Page.EvaluateAsync<string>($$"""
            async () => {
                var host = window.__api.host('Pages'), api = window.kronikol_diagrams;
                var v = await api.openNote(host, {{idx}}, { state: 'collapsed' });
                await api.whenDrawn(host);
                var g = api.findNote(host, {{idx}});
                return [v, g ? window.__api.rows(g) : 'no note', g && g.part.classList.contains('puml-fragment') ? +g.part.dataset.fragment >= 1 : 'whole'].join(',');
            }
            """);

        Assert.Equal("true,1,true", drawn);
    }

    [Fact]
    public async Task OpenNote_is_kept_with_false_for_a_note_the_diagram_does_not_have_and_a_view_its_payload_has_not()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_OpenNoteFalse.html", [Orders("a", "Order A")], Start));
        await OpenScenariosAsync("Order A");
        await SettledAsync("Order A");

        var kept = await Page.EvaluateAsync<string>("""
            async () => {
                var host = window.__api.host('Order A'), api = window.kronikol_diagrams;
                // the request's note holds its headers alone: no JSON, so no YAML view
                var request = window.__api.noteIndex(host, 'Content-Type');
                var before = host.querySelector(':scope > svg'), formats = JSON.stringify(host._noteFormats || {});
                var past = await api.openNote(host, 999, { state: 'collapsed' });
                var below = await api.openNote(host, -1, { state: 'collapsed' });
                var yaml = await api.openNote(host, request, { format: 'yaml' });
                return [past, below, yaml, host.querySelector(':scope > svg') === before, JSON.stringify(host._noteFormats || {}) === formats].join(',');
            }
            """);

        Assert.Equal("false,false,false,true,true", kept);
    }

    /// <summary>
    /// The engine refusing a note's drawing (it threw): the promise is kept with false, and the note, the picture and the
    /// source the picture was drawn from stay as they were, as a note's own redraw left them before it went through the
    /// queue. The engine is stubbed in the page for one call.
    /// </summary>
    [Fact]
    public async Task OpenNote_whose_drawing_the_engine_refuses_leaves_the_note_and_its_picture_as_they_were()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_OpenNoteRefused.html", [Orders("a", "Order A")], Start));
        await OpenScenariosAsync("Order A");
        await SettledAsync("Order A");
        var idx = await FirstResponseNoteAsync("Order A", "aorders_01");

        var kept = await Page.EvaluateAsync<string>($$"""
            async () => {
                var host = window.__api.host('Order A'), real = window.plantuml.render;
                var picture = host.querySelector(':scope > svg'), source = host.getAttribute('data-plantuml'), step = host._noteSteps[{{idx}}];
                window.plantuml.render = function() { window.plantuml.render = real; throw new Error('refused'); };
                var v;
                try { v = await window.kronikol_diagrams.openNote(host, {{idx}}, { state: 'collapsed' }); }
                finally { window.plantuml.render = real; }
                return [v, host._noteSteps[{{idx}}] === step, host.getAttribute('data-plantuml') === source,
                    host.querySelector(':scope > svg') === picture].join(',');
            }
            """);

        Assert.Equal("false,true,true,true", kept);
    }

    [Fact]
    public async Task FindNote_finds_a_note_by_its_original_index_in_a_later_part()
    {
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_FindNote.html", [Pages("p", "Pages", 14)], Start, partHeight: 900));
        await OpenScenariosAsync("Pages");
        await SettledAsync("Pages");
        Assert.True(await Page.EvaluateAsync<int>("() => window.__api.host('Pages').querySelectorAll(':scope > .puml-fragment').length") >= 3,
            "premise: the diagram is drawn in three parts or more");

        var found = await Page.EvaluateAsync<string>("""
            () => {
                var host = window.__api.host('Pages'), idx = window.__api.noteIndex(host, 'item13_01');
                var g = window.kronikol_diagrams.findNote(host, idx);
                if (!g) return 'nothing found for note ' + idx;
                var ix = window._noteIndicesOf(g);
                return [ix && ix.original === idx, g.part.classList.contains('puml-fragment') && +g.part.dataset.fragment >= 1,
                    g.svg === g.paths[0].ownerSVGElement, window.__api.groupText(g).indexOf('item13_01') >= 0].join(',');
            }
            """);

        Assert.Equal("True,True,True,True", found, ignoreCase: true);
    }

    [Fact]
    public async Task FindMessage_finds_the_nth_message_whose_label_reads_as_the_statement_s()
    {
        DiagramApiReports.Scenario menus = new("m", "Menus",
        [
            .. DiagramApiReports.Call("m", 1, "GET", "/api/menu", DiagramApiReports.Body("menu1", 2)),
            .. DiagramApiReports.Call("m", 2, "GET", "/api/orders", DiagramApiReports.Body("orders", 2)),
            .. DiagramApiReports.Call("m", 3, "GET", "/api/menu", DiagramApiReports.Body("menu2", 2)),
            .. DiagramApiReports.Call("m", 4, "GET", "/api/menu", DiagramApiReports.Body("menu3", 2))
        ]);
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_FindMessage.html", [menus], Start));
        await OpenScenariosAsync("Menus");
        await SettledAsync("Menus");

        var found = await Page.EvaluateAsync<string>("""
            () => {
                var host = window.__api.host('Menus'), api = window.kronikol_diagrams;
                var svg = host.querySelector(':scope > svg');
                var menus = Array.from(svg.querySelectorAll('g.message')).filter(g => g.childElementCount > 0
                    && window.__api.text(g).indexOf('GET:/api/menu') >= 0);
                var statement = window.__api.source(host).split('\n').filter(l => l.indexOf('GET: /api/menu') >= 0)[2];
                var orders = api.findMessage(host, 'GET: /api/orders', 0);
                return [menus.length, api.findMessage(host, 'GET: /api/menu', 0) === menus[0], api.findMessage(host, 'GET: /api/menu', 2) === menus[2],
                    api.findMessage(host, statement, 2) === menus[2], api.findMessage(host, 'GET: /api/menu', 3) === null,
                    !!orders && window.__api.text(orders).indexOf('GET:/api/orders') >= 0].join(',');
            }
            """);

        Assert.Equal("3,true,true,true,true,true", found);
    }

    /// <summary>
    /// plans/SEARCH_PLAN.md H28, as the design took it: no map of lines from the original source to the drawn one is kept.
    /// A note is found by its index in the original source, which the note script gives every note it draws, and a
    /// message by its place among the original source's statements of the same label, the diagram's filters run over
    /// the original to see which of them are drawn; so each is found through the filters, the note states and the parts.
    /// </summary>
    [Fact]
    public async Task FindNote_and_findMessage_find_the_element_meant_through_filters_note_states_and_parts()
    {
        // a database read labelled as the API's read is, then the API's reads, one far down: drawn in parts, the payloads
        // folded, the database's call left out
        DiagramApiReports.Scenario items = new("i", "Items",
        [
            .. DiagramApiReports.Call("i", 1, "GET", "/items", DiagramApiReports.Body("dbitems", 2), "Orders DB", database: true),
            .. DiagramApiReports.Call("i", 2, "GET", "/items", DiagramApiReports.Body("first", 2)),
            .. Enumerable.Range(3, 11).SelectMany(k => DiagramApiReports.Call("i", k, "GET", $"/api/other?page={k}", DiagramApiReports.Body($"other{k:D2}", 3))),
            .. DiagramApiReports.Call("i", 20, "GET", "/items", DiagramApiReports.Body("last", 2))
        ]);
        var start = Start with { Details = ReportDetailsState.Collapsed };
        await OpenAsync(DiagramApiReports.Write(TempDir, OutputDir, "DiagramApi_Through.html", [items], start, partHeight: 700));
        await OpenScenariosAsync("Items");
        await SettledAsync("Items");
        Assert.True(await Page.EvaluateAsync<int>("() => window.__api.host('Items').querySelectorAll(':scope > .puml-fragment').length") >= 2,
            "premise: the diagram is drawn in parts");

        const string read = """
            () => {
                var host = window.__api.host('Items'), api = window.kronikol_diagrams, h = window.__api;
                function where(el) { var p = el && el.closest('.puml-fragment'); return el ? 'part ' + (p ? p.dataset.fragment : 'none') : 'none'; }
                function to(el) { return el ? (el.closest('g.message') ? 'message' : 'other') : 'none'; }
                var db = api.findMessage(host, 'GET: /items', 0), first = api.findMessage(host, 'GET: /items', 1), last = api.findMessage(host, 'GET: /items', 2);
                var lastNote = h.noteIndex(host, 'last_01'), dbNote = h.noteIndex(host, 'dbitems_01');
                var g = api.findNote(host, lastNote), d = api.findNote(host, dbNote);
                var parts = host.querySelectorAll(':scope > .puml-fragment').length;
                return [to(db), to(first), where(first), to(last), where(last) === 'part ' + (parts - 1),
                    g ? h.rows(g) + ' row ' + (h.groupText(g).indexOf('last_01') >= 0) : 'no note', d ? 'db note drawn' : 'no db note'].join(',');
            }
            """;
        Assert.Equal("none,message,part 0,message,True,1 row True,no db note", await Page.EvaluateAsync<string>(read), ignoreCase: true);

        // the database's calls drawn: the first statement of that label is drawn now, the others are still found
        await Page.EvaluateAsync("""
            async () => {
                await window.kronikol_diagrams.show(window.__api.scenario('Items'), { databases: true });
                await window.kronikol_diagrams.whenDrawn(window.__api.host('Items'));
            }
            """);
        await SettledAsync("Items");
        var drawn = await Page.EvaluateAsync<string>("""
            () => {
                var host = window.__api.host('Items'), api = window.kronikol_diagrams, h = window.__api;
                var db = api.findMessage(host, 'GET: /items', 0), first = api.findMessage(host, 'GET: /items', 1);
                var all = h.svgs(host).flatMap(s => Array.from(s.querySelectorAll('g.message')).filter(g => g.childElementCount > 0
                    && h.text(g).indexOf('GET:/items') >= 0));
                var d = api.findNote(host, h.noteIndex(host, 'dbitems_01'));
                return [db === all[0], first === all[1], d ? h.rows(d) + ' row ' + (h.groupText(d).indexOf('dbitems_01') >= 0) : 'no db note'].join(',');
            }
            """);
        Assert.Equal("true,true,1 row true", drawn);
    }
}
