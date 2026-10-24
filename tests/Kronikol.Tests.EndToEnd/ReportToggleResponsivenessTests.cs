using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A report-level toggle redraws every diagram the page has drawn, and with every scenario of a large report open and
/// drawn it answers at once: no main-thread task it starts runs long, turning it off or back on. In 5.1.0 a toggle
/// turned back on put every diagram back from the page's cache in one task, seconds long on a consumer's report, and
/// each diagram far from the screen was laid out to place note buttons nobody could see. Every diagram is still drawn
/// again at once (5.1.1), the one on screen first and one scrolled to next, so a diagram far down the page shows the new
/// state the moment it is reached; its buttons are placed as it comes near. A diagram the toggle leaves as it was is not
/// drawn again, and the next drawings start on the workers ahead of their turn. The budgets scale by the run's measured
/// contention (<see cref="ContentionScale"/>).
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ReportToggleResponsivenessTests : PlaywrightTestBase
{
    private readonly ITestOutputHelper _output;

    public ReportToggleResponsivenessTests(PlaywrightFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    /// <summary>The longest main-thread task a toggle may cost, before the contention scale.</summary>
    private const double TaskBudgetMs = 200;

    private const int Diagrams = 60;

    // written once for the class, outside any test's folder
    private static readonly Lazy<string> Report = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-toggles-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        // sequence diagrams alone: a CI runner draws a few a second, and the facts below wait for every one
        return DenseReportFixture.GenerateWithFlows(dir, OutputDir, "Dense_Toggles.html", features: 6, scenariosPerFeature: 10,
            everyFilter: true, internalFlow: false);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    // two scenarios: a toggle's queue with nothing to wait for but the engine
    private static readonly Lazy<string> Small = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-toggles-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return DenseReportFixture.GenerateWithFlows(dir, OutputDir, "Dense_Toggles_Small.html", features: 1, scenariosPerFeature: 2,
            everyFilter: true);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    // one scenario of forty calls: a sequence diagram of about eighty payload notes, drawn in parts, the shape of a
    // consumer's diagram whose toggles took 1.5 to 2 s in 5.4.4
    private static readonly Lazy<string> Long = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-toggles-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return DenseReportFixture.GenerateWithFlows(dir, OutputDir, "Dense_Toggles_Long.html", features: 1, scenariosPerFeature: 1,
            callsPerScenario: 40, everyFilter: true, internalFlow: false);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Opens the report with every feature and scenario open and every diagram drawn, and lets it settle.</summary>
    private async Task OpenAllDrawnAsync(string? report = null, int diagrams = Diagrams)
    {
        await Page.AddInitScriptAsync(BrowserRenderWorkerTests.BenchInitWithProbe);
        await Page.GotoReportAsync(report ?? Report.Value, new() { Timeout = PageOpenTimeout });
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature, details.scenario').forEach(function (d) { d.open = true; })");
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        // every diagram drawn, the views behind each scenario's tab bar as well: a toggle measured while the page still
        // draws them waits its turn at the engine between them, and its redraws never run back to back
        await Page.WaitForFunctionAsync($$"""
            () => {
                var all = document.querySelectorAll('.plantuml-browser');
                return !window._plantumlRendering && Array.prototype.every.call(all, function (c) { return !!c.querySelector('svg'); })
                    && document.querySelectorAll('.plantuml-browser[data-k-sequence] svg[data-k-roles]').length >= {{diagrams}};
            }
            """, null, new() { Timeout = 300_000, PollingInterval = 200 });
        await SettleAsync();
    }

    /// <summary>Waits until the page has gone a second without a long task, so what is measured next is the action's.</summary>
    private Task SettleAsync() => Page.WaitForFunctionAsync("""
        () => {
            var tasks = window.__bench.longTasks, now = performance.now();
            var last = tasks.length ? tasks[tasks.length - 1].start + tasks[tasks.length - 1].duration : 0;
            return now - last > 1000;
        }
        """, null, new() { Timeout = 120_000, PollingInterval = 200 });

    /// <summary>Clicks the report's toggle and waits until its last diagram is drawn again; returns when it was clicked.</summary>
    private async Task<double> ToggleAsync(string toggle)
    {
        var from = await Page.EvaluateAsync<double>($$"""
            () => {
                var t = performance.now();
                document.querySelector('.toolbar-row .toggle-btn[data-toggle="{{toggle}}"]').click();
                return t;
            }
            """);
        await Page.WaitForFunctionAsync(
            $"() => !document.querySelector('.toggle-btn[data-toggle=\"{toggle}\"][data-pending]')",
            null, new() { Timeout = 300_000, PollingInterval = 200 });
        return from;
    }

    /// <summary>The longest long task that ended after <paramref name="from"/>, and the run's contention stretch.</summary>
    private async Task<(double Worst, double Stretch)> WorstTaskSinceAsync(double from)
    {
        await SettleAsync();
        var r = await Page.EvaluateAsync<JsonElement>("""
            (from) => {
                var worst = 0;
                window.__bench.longTasks.forEach(function (t) { if (t.start + t.duration >= from && t.duration > worst) worst = t.duration; });
                return { worst: worst, probe: window.__bench.probe };
            }
            """, from);
        var probe = r.GetProperty("probe").EnumerateArray().Select(p => p.GetDouble()).ToList();
        return (r.GetProperty("worst").GetDouble(), ContentionScale.Stretch(probe));
    }

    private void AssertWithinBudget(string action, double worst, double stretch)
    {
        _output.WriteLine($"{action}: longest task {worst:F0} ms, stretch {stretch:F2}");
        Assert.True(worst <= TaskBudgetMs * stretch,
            $"{action} ran a {worst:F0} ms task (budget {TaskBudgetMs * stretch:F0} ms at stretch {stretch:F2})");
    }

    [Fact]
    public async Task Report_toggles_turned_off_and_on_with_every_diagram_drawn_run_no_long_task()
    {
        await OpenAllDrawnAsync();

        // one page for every toggle (drawing every diagram is most of the test's time); each is measured on its own
        var results = new List<(string Action, double Worst, double Stretch)>();
        foreach (var toggle in new[] { "databases", "setup", "events" })
        {
            var off = await ToggleAsync(toggle);
            var (worstOff, stretchOff) = await WorstTaskSinceAsync(off);
            results.Add(($"{toggle} off", worstOff, stretchOff));
            var on = await ToggleAsync(toggle);
            var (worstOn, stretchOn) = await WorstTaskSinceAsync(on);
            results.Add(($"{toggle} back on", worstOn, stretchOn));
        }

        foreach (var (action, worst, stretch) in results) AssertWithinBudget(action, worst, stretch);
    }

    [Fact]
    public async Task Sorting_the_features_with_every_diagram_drawn_runs_no_long_task_and_the_page_order_follows()
    {
        await OpenAllDrawnAsync();

        // a card moved in the page has the layout of its drawn diagrams built again: the sort places the cards with CSS
        // order and moves them in the page after, in slices (5.2.1; 213 ms on 200 scenarios with 400 drawn before it)
        var results = new List<(string Action, double Worst, double Stretch)>();
        foreach (var order in new[] { "duration", "name", "order" })
        {
            var from = await Page.EvaluateAsync<double>($$"""
                () => {
                    var t = performance.now(), sort = document.getElementById('features-sort');
                    sort.value = '{{order}}';
                    sort.dispatchEvent(new Event('change', { bubbles: true }));
                    return t;
                }
                """);
            await Page.WaitForFunctionAsync("""
                () => {
                    var o = Array.prototype.map.call(document.querySelectorAll('#report-content > details.feature'),
                        function (f) { return parseInt(getComputedStyle(f).order, 10) || 0; });
                    return o.every(function (v, i) { return i === 0 || o[i - 1] <= v; });
                }
                """, null, new() { Timeout = 60_000, PollingInterval = 200 });
            var (worst, stretch) = await WorstTaskSinceAsync(from);
            results.Add(($"sorting by {order}", worst, stretch));
        }

        foreach (var (action, worst, stretch) in results) AssertWithinBudget(action, worst, stretch);
    }

    [Fact]
    public async Task A_toggle_draws_the_diagram_on_screen_first_and_one_scrolled_to_next_and_a_far_one_gets_its_buttons_as_it_comes_near()
    {
        await OpenAllDrawnAsync();
        // the reader is halfway down the report
        await Page.EvaluateAsync("() => document.querySelectorAll('.plantuml-browser[data-k-sequence]')[30].scrollIntoView({ block: 'center' })");
        await SettleAsync();
        // the order the sequence diagrams get their new pictures in
        await Page.EvaluateAsync("""
            () => {
                var seq = Array.prototype.slice.call(document.querySelectorAll('.plantuml-browser[data-k-sequence]'));
                seq.forEach(function (c) { c.querySelector('svg')._before = true; });
                window.__order = [];
                var seen = new Set();
                new MutationObserver(function (records) {
                    records.forEach(function (r) {
                        var c = r.target.closest ? r.target.closest('.plantuml-browser[data-k-sequence]') : null;
                        var svg = c && c.querySelector('svg');
                        if (!svg || svg._before || seen.has(c)) return;
                        seen.add(c);
                        window.__order.push(seq.indexOf(c));
                    });
                }).observe(document.body, { childList: true, subtree: true });
                document.querySelector('.toolbar-row .toggle-btn[data-toggle="databases"]').click();
            }
            """);
        // a few diagrams in, the reader jumps to the last one
        await Page.WaitForFunctionAsync("() => window.__order.length >= 5", null, new() { Timeout = 120_000, PollingInterval = 200 });
        var scrolledAt = await Page.EvaluateAsync<int>("""
            () => {
                var seq = document.querySelectorAll('.plantuml-browser[data-k-sequence]');
                seq[seq.length - 1].scrollIntoView({ block: 'center' });
                return window.__order.length;
            }
            """);
        await Page.WaitForFunctionAsync("() => !document.querySelector('.toggle-btn[data-toggle=\"databases\"][data-pending]')",
            null, new() { Timeout = 300_000, PollingInterval = 200 });
        var order = (await Page.EvaluateAsync<int[]>("() => window.__order")).ToList();

        // the diagram on screen was drawn first, though thirty come before it on the page (5.1.0 drew them in page
        // order), and the one jumped to went next instead of waiting for every diagram nearer the middle
        Assert.Equal(Diagrams, order.Count);
        Assert.InRange(order[0], 29, 31);
        Assert.InRange(order.IndexOf(Diagrams - 1), 0, scrolledAt + 10);

        // the first diagram, far from where the reader was: reached in one task, it holds its new picture with its notes
        // as cards, and has no note buttons yet; they arrive as it comes near, and show on hover
        var first = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var c = document.querySelector('.plantuml-browser[data-k-sequence]');
                var far = c.getBoundingClientRect().bottom < -window.innerHeight * 3;
                c.scrollIntoView({ block: 'center' });
                var svg = c.querySelector('svg');
                return { far: far, old: !!svg._before, roles: svg.getAttribute('data-k-roles') || '', buttons: !!svg.querySelector('.note-hover-rect') };
            }
            """);
        Assert.True(first.GetProperty("far").GetBoolean(), "the first diagram is far above the screen");
        Assert.False(first.GetProperty("old").GetBoolean(), "the first diagram was drawn again");
        Assert.Contains("\"cards\":", first.GetProperty("roles").GetString());
        Assert.False(first.GetProperty("buttons").GetBoolean(), "a diagram far from the screen gets its buttons as it comes near");
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('.plantuml-browser[data-k-sequence]').querySelector('svg .note-hover-rect')",
            null, new() { Timeout = 30_000, PollingInterval = 200 });
        var shown = await Page.EvaluateAsync<bool>("""
            () => {
                var c = document.querySelector('.plantuml-browser[data-k-sequence]');
                c.querySelector('svg .note-hover-rect').dispatchEvent(new MouseEvent('mouseenter', { bubbles: false }));
                return Array.prototype.some.call(c.querySelectorAll('svg .note-toggle-icon'), function (b) { return b.style.opacity === '1'; });
            }
            """);
        Assert.True(shown, "hovering the note shows its buttons");
    }

    [Fact]
    public async Task A_filter_toggle_draws_again_only_what_it_changes_and_starts_the_next_drawings_ahead()
    {
        await OpenAllDrawnAsync(Small.Value, 2);
        await Page.EvaluateAsync("""
            () => {
                document.querySelectorAll('.plantuml-browser svg').forEach(function (s) { s._before = true; });
                var prefetch = window.plantuml.prefetch;
                window.__prefetched = [];
                window.plantuml.prefetch = function (sources) {
                    Array.prototype.push.apply(window.__prefetched, sources);
                    return prefetch.apply(this, arguments);
                };
            }
            """);

        // the fixture's payloads carry no headers, so Headers changes no diagram: every picture stays, the sequence
        // diagrams' first drawings as well (drawn from their source with some line ends written \n for \r\n)
        await ToggleAsync("headers");
        Assert.Equal(4, await Page.EvaluateAsync<int>(
            "() => Array.prototype.filter.call(document.querySelectorAll('.plantuml-browser svg'), function (s) { return s._before; }).length"));

        await ToggleAsync("databases");

        // the sequence diagrams lost their database calls and were drawn again, the second one started on the workers
        // during the first one's turn; each activity diagram, which the filter leaves as it was, kept its picture
        var r = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var all = Array.prototype.slice.call(document.querySelectorAll('.plantuml-browser'));
                var seq = all.filter(function (c) { return c.hasAttribute('data-k-sequence'); });
                var other = all.filter(function (c) { return !c.hasAttribute('data-k-sequence'); });
                return {
                    seq: seq.length, other: other.length,
                    seqRedrawn: seq.filter(function (c) { return !c.querySelector('svg')._before; }).length,
                    otherKept: other.filter(function (c) { return c.querySelector('svg')._before; }).length,
                    seqStartedAhead: seq.filter(function (c) { return window.__prefetched.indexOf(c.getAttribute('data-plantuml')) >= 0; }).length
                };
            }
            """);
        Assert.Equal(2, r.GetProperty("seq").GetInt32());
        Assert.Equal(2, r.GetProperty("other").GetInt32());
        Assert.Equal(2, r.GetProperty("seqRedrawn").GetInt32());
        Assert.Equal(2, r.GetProperty("otherKept").GetInt32());
        Assert.True(r.GetProperty("seqStartedAhead").GetInt32() >= 1, "the next sequence diagram was started ahead of its turn");
    }

    [Fact]
    public async Task A_diagram_whose_picture_was_replaced_since_is_drawn_again_though_the_toggle_leaves_its_source_as_it_was()
    {
        await OpenAllDrawnAsync(Small.Value, 2);
        // an activity diagram's picture gives way to something else (as to an engine failure's message)
        await Page.EvaluateAsync("""
            () => {
                var other = Array.prototype.filter.call(document.querySelectorAll('.plantuml-browser'), function (c) { return !c.hasAttribute('data-k-sequence'); })[0];
                other.innerHTML = '<div class="replaced">replaced</div>';
            }
            """);

        await ToggleAsync("databases");

        // the filter leaves its source as it was, and it is drawn from it again all the same
        var drawn = await Page.EvaluateAsync<bool>("""
            () => {
                var other = Array.prototype.filter.call(document.querySelectorAll('.plantuml-browser'), function (c) { return !c.hasAttribute('data-k-sequence'); })[0];
                return !!other.querySelector('svg') && !other.querySelector('.replaced');
            }
            """);
        Assert.True(drawn, "the diagram was drawn again");
    }

    [Fact]
    public async Task A_zoomed_diagram_drawn_again_far_from_the_screen_keeps_its_zoom_before_its_buttons_arrive()
    {
        // a window narrow enough that the diagrams are wider than it, so each can be zoomed
        await Page.SetViewportSizeAsync(640, 480);
        await OpenAllDrawnAsync(Small.Value, 2);
        // the reader zooms the second diagram to its natural size, then goes back to the top
        await Page.EvaluateAsync("() => document.querySelectorAll('.plantuml-browser[data-k-sequence]')[1].scrollIntoView({ block: 'center' })");
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelectorAll('.plantuml-browser[data-k-sequence]')[1].querySelector('.diagram-zoom-slider')",
            null, new() { Timeout = 30_000, PollingInterval = 200 });
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelectorAll('.plantuml-browser[data-k-sequence]')[1].querySelector('.diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
                window.scrollTo(0, 0);
            }
            """);
        await SettleAsync();

        await ToggleAsync("databases");

        // drawn again far from the screen, it is as wide as the reader left it, though its zoom slider and note buttons
        // wait until it comes near
        var r = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var c = document.querySelectorAll('.plantuml-browser[data-k-sequence]')[1], svg = c.querySelector('svg');
                return { far: c.getBoundingClientRect().top > window.innerHeight * 1.5, zoomed: c.classList.contains('diagram-natural-size'),
                         maxWidth: svg.style.maxWidth, buttons: !!svg.querySelector('.note-hover-rect') };
            }
            """);
        Assert.True(r.GetProperty("far").GetBoolean(), "the second diagram is far below the screen");
        Assert.True(r.GetProperty("zoomed").GetBoolean(), "the diagram is still zoomed");
        Assert.Equal("none", r.GetProperty("maxWidth").GetString());
        Assert.False(r.GetProperty("buttons").GetBoolean(), "its note buttons wait until it comes near");
    }

    [Fact]
    public async Task A_redraw_the_engine_refuses_at_once_leaves_the_queue_finished_once_with_no_poll_running()
    {
        // every interval the page starts, and whether it was cleared
        await Page.AddInitScriptAsync("""
            (() => {
                var set = window.setInterval, clear = window.clearInterval;
                window.__intervals = new Map();
                window.setInterval = function (fn, ms) { var id = set.apply(this, arguments); window.__intervals.set(id, { ms: ms, at: performance.now() }); return id; };
                window.clearInterval = function (id) { window.__intervals.delete(id); return clear.apply(this, arguments); };
            })();
            """);
        await OpenAllDrawnAsync(Small.Value, 2);

        // the engine refuses the queue's first drawing at once, as it does a source it cannot take
        var from = await Page.EvaluateAsync<double>("""
            () => {
                var render = window.plantuml.render;
                window.plantuml.render = function () { window.plantuml.render = render; throw new Error('refused'); };
                window.__completed = window._renderCompleteCount || 0;
                return performance.now();
            }
            """);
        await ToggleAsync("databases");

        // the queue went on to the other diagram and finished once, and the refused drawing's poll stopped with it: it ran
        // on for 30 s and then started the finished queue again (5.1.0)
        await Page.WaitForFunctionAsync(
            "(from) => !Array.from(window.__intervals.values()).some(function (v) { return v.ms === 250 && v.at >= from; })",
            from, new() { Timeout = 5_000, PollingInterval = 200 });
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window._renderCompleteCount - window.__completed"));
    }

    /// <summary>
    /// Opens the long diagram's scenario and leaves the reader at the top of the diagram, under the report's bar, which
    /// floats there with its toggles; returns once the diagram is drawn with its note buttons and the page has settled.
    /// </summary>
    private async Task OpenLongUnderTheBarAsync()
    {
        await Page.AddInitScriptAsync(BrowserRenderWorkerTests.BenchInitWithProbe);
        await Page.GotoReportAsync(Long.Value, new() { Timeout = PageOpenTimeout });
        await Page.EvaluateAsync("""
            () => {
                document.querySelectorAll('details.feature, details.scenario').forEach(function (d) { d.open = true; });
                document.querySelector('.plantuml-browser[data-diagram-type=plantuml]').scrollIntoView({ block: 'start' });
            }
            """);
        await Page.WaitForFunctionAsync("""
            () => {
                var parts = document.querySelectorAll('.plantuml-browser[data-diagram-type=plantuml] > .puml-fragment');
                return !window._plantumlRendering && parts.length > 1
                    && Array.prototype.every.call(parts, function (p) { return !!p.querySelector('svg .note-hover-rect'); });
            }
            """, null, new() { Timeout = 300_000, PollingInterval = 200 });
        var shape = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var c = document.querySelector('.plantuml-browser[data-diagram-type=plantuml]');
                var bar = document.querySelector('.toolbar-row.report-nav');
                window.scrollTo(0, window.scrollY + c.getBoundingClientRect().top - bar.getBoundingClientRect().height - 20);
                return { parts: c.querySelectorAll(':scope > .puml-fragment').length, notes: c.querySelectorAll('svg .note-hover-rect').length,
                         words: c.querySelectorAll('svg text').length };
            }
            """);
        _output.WriteLine($"diagram: {shape}");
        Assert.True(shape.GetProperty("notes").GetInt32() >= 60, "the diagram holds the notes of forty calls");
        await Page.WaitForFunctionAsync("() => document.querySelector('.toolbar-row.report-nav').classList.contains('k-floating')",
            null, new() { Timeout = 10_000, PollingInterval = 200 });
        await SettleAsync();
    }

    /// <summary>
    /// How long after the engine has drawn the last part of the long diagram's new picture the page may take to paint it,
    /// before the contention scale. The drawing itself runs on the engine's workers, and how long it takes is the
    /// machine's: timed from the click, a toggle that needs new drawings took 569 to 843 ms on CI's Linux runner (5.5.3).
    /// </summary>
    private const double PaintBudgetMs = 300;

    [Fact]
    public async Task A_toggle_with_a_long_diagram_of_many_notes_on_screen_runs_no_long_task_and_paints_the_new_picture_first()
    {
        await OpenLongUnderTheBarAsync();

        var results = new List<(string Action, double Worst, double Stretch, double DrawnToPaint, bool ButtonsFirst)>();
        // the four filters the fixture has (it records no assertions): each draws the diagram again, and back
        foreach (var toggle in new[] { "databases", "steps", "setup", "events" })
            foreach (var phase in new[] { "switched", "switched back" })
            {
                // the first frame that shows the diagram's new picture: whether its note buttons were placed before it,
                // and when it had been painted (a task queued from the frame's callback runs after its paint), from the
                // click and from the moment the last of its new parts was drawn (put in its hidden part by the engine, or
                // from the page's cache), which is when the page's own work begins
                var from = await Page.EvaluateAsync<double>($$"""
                    () => {
                        var c = document.querySelector('.plantuml-browser[data-diagram-type=plantuml]');
                        c.querySelectorAll('svg').forEach(function (s) { s._before = true; });
                        var t = performance.now(), drawn = null;
                        window.__painted = null;
                        window.__drawnToPaint = null;
                        window.__buttonsFirst = null;
                        var parts = new MutationObserver(function (records) {
                            records.forEach(function (r) {
                                if (!r.target.closest || !r.target.closest('.puml-fragment-new')) return;
                                if (Array.prototype.some.call(r.addedNodes, function (n) {
                                    return n.nodeName.toLowerCase() === 'svg' || (n.querySelector && !!n.querySelector('svg'));
                                })) drawn = performance.now();
                            });
                        });
                        parts.observe(c, { childList: true, subtree: true });
                        // every frame watched, the next one asked for from inside the callback: asked for from a task, a
                        // frame could pass unwatched (the Linux runner's did)
                        requestAnimationFrame(function frame() {
                            var s = c.querySelector('svg');
                            if (s && !s._before) {
                                parts.disconnect();
                                window.__buttonsFirst = !!c.querySelector('svg .note-hover-rect');
                                setTimeout(function () {
                                    var now = performance.now();
                                    window.__painted = now - t;
                                    window.__drawnToPaint = now - (drawn === null ? t : drawn);
                                }, 0);
                            } else if (performance.now() - t < 60000) requestAnimationFrame(frame);
                        });
                        document.querySelector('.toolbar-row .toggle-btn[data-toggle="{{toggle}}"]').click();
                        return t;
                    }
                    """);
                await Page.WaitForFunctionAsync(
                    $"() => !document.querySelector('.toggle-btn[data-toggle=\"{toggle}\"][data-pending]')",
                    null, new() { Timeout = 300_000, PollingInterval = 200 });
                var (worst, stretch) = await WorstTaskSinceAsync(from);
                var r = await Page.EvaluateAsync<JsonElement>("""
                    () => ({ painted: window.__painted, drawnToPaint: window.__drawnToPaint, buttonsFirst: window.__buttonsFirst,
                             buttons: Array.prototype.every.call(document.querySelectorAll('.plantuml-browser[data-diagram-type=plantuml] > .puml-fragment'),
                                 function (p) { return !!p.querySelector('svg .note-hover-rect'); }) })
                    """);
                var painted = r.GetProperty("painted").ValueKind == JsonValueKind.Number ? r.GetProperty("painted").GetDouble() : double.NaN;
                var drawnToPaint = r.GetProperty("drawnToPaint").ValueKind == JsonValueKind.Number ? r.GetProperty("drawnToPaint").GetDouble() : double.NaN;
                var buttonsFirst = r.GetProperty("buttonsFirst").ValueKind != JsonValueKind.False;
                _output.WriteLine($"{toggle} {phase}: longest task {worst:F0} ms, new picture painted after {painted:F0} ms, " +
                                  $"{drawnToPaint:F0} ms after its last part was drawn" + (buttonsFirst ? ", its note buttons placed before it" : ""));
                // once the toggle is done, the diagram on screen has its buttons
                Assert.True(r.GetProperty("buttons").GetBoolean(), $"{toggle} {phase}: every part has its note buttons once the toggle is done");
                results.Add(($"{toggle} {phase}", worst, stretch, drawnToPaint, buttonsFirst));
            }

        foreach (var (action, worst, stretch, drawnToPaint, buttonsFirst) in results)
        {
            // the picture is painted first, and its note buttons follow: placing them took longer than drawing it
            Assert.False(buttonsFirst, $"{action}: the note buttons were placed before the new picture was painted");
            Assert.True(drawnToPaint <= PaintBudgetMs * stretch,
                $"{action}: the new picture was painted {drawnToPaint:F0} ms after its last part was drawn (budget {PaintBudgetMs * stretch:F0} ms at stretch {stretch:F2})");
            AssertWithinBudget(action, worst, stretch);
        }
    }

    [Fact]
    public async Task Placing_the_note_buttons_of_a_long_diagram_reads_each_word_s_box_once_and_takes_one_layout()
    {
        await OpenLongUnderTheBarAsync();

        // each part's buttons placed again, the boxes the page reads counted: the engine draws each word of a payload as an
        // element of its own, so a part holds about two thousand
        var r = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var proto = SVGGraphicsElement.prototype, getBBox = proto.getBBox, reads = 0;
                proto.getBBox = function () { reads++; return getBBox.apply(this, arguments); };
                try {
                    return Array.prototype.map.call(document.querySelectorAll('.plantuml-browser[data-diagram-type=plantuml] > .puml-fragment'), function (part) {
                        var svg = part.querySelector('svg');
                        reads = 0;
                        var t = performance.now();
                        window._makeNotesCollapsible(part);
                        return { reads: reads, ms: performance.now() - t, drawn: svg.querySelectorAll('text, path, rect, line, polygon, ellipse').length,
                                 notes: svg.querySelectorAll('.note-hover-rect').length };
                    });
                } finally {
                    proto.getBBox = getBBox;
                }
            }
            """);
        var probe = await Page.EvaluateAsync<double[]>("() => window.__bench.probe");
        var stretch = ContentionScale.Stretch(probe.ToList());
        foreach (var part in r.EnumerateArray())
        {
            var reads = part.GetProperty("reads").GetInt32();
            var drawn = part.GetProperty("drawn").GetInt32();
            var ms = part.GetProperty("ms").GetDouble();
            _output.WriteLine($"part of {part.GetProperty("notes").GetInt32()} notes and {drawn} drawn elements: {reads} boxes read in {ms:F0} ms");
            if (part.GetProperty("notes").GetInt32() < 10) continue;
            // until 5.5.1 each note read every word of the picture again (a note's box against every other word), and
            // read its own boxes after the buttons of the note before it were written, so the browser laid the whole
            // picture out again for each note
            Assert.True(reads <= 3 * drawn, $"{reads} boxes read for a part of {drawn} drawn elements");
            Assert.True(ms <= 100 * stretch, $"placing a part's note buttons took {ms:F0} ms (budget {100 * stretch:F0} ms at stretch {stretch:F2})");
        }
    }

    // The diagram's one slider (since 5.7.0 a diagram in parts has one, on its container: plans/ZOOM_EVERY_DIAGRAM_PLAN.md
    // Z8), made after a toggle from the widths the engine drew its parts at.
    [Fact]
    public async Task A_toggle_makes_the_diagrams_zoom_slider_without_drawing_a_part_at_its_natural_size()
    {
        await OpenLongUnderTheBarAsync();
        await Page.EvaluateAsync("""
            () => {
                // a box narrower than the diagram's parts (about 600 px each), so each part is scaled down to fit it and
                // the diagram gets a zoom slider when it is drawn again
                document.querySelector('.plantuml-browser[data-diagram-type=plantuml]').style.width = '420px';
                window.__natural = 0;
                // a picture drawn at its natural size and back shows as a change of its style from max-width: none
                new MutationObserver(function (records) {
                    records.forEach(function (r) { if (r.target.tagName === 'svg' && /max-width:\s*none/.test(r.oldValue || '')) window.__natural++; });
                }).observe(document.querySelector('.plantuml-browser[data-diagram-type=plantuml]'),
                    { attributes: true, attributeFilter: ['style'], attributeOldValue: true, subtree: true });
            }
            """);

        await ToggleAsync("databases");
        // every part drawn, and the diagram's slider made again
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('.plantuml-browser[data-diagram-type=plantuml]');
                return Array.prototype.every.call(c.querySelectorAll(':scope > .puml-fragment'), function (p) { return !!p.querySelector('svg'); })
                    && !!c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
            }
            """, null, new() { Timeout = 30_000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        // every part is drawn at the level that fits the diagram's widest part, and the slider starts there, worked out
        // from the widths the engine drew the parts at; measuring a width drew the part at its natural size and back,
        // every word in it laid out twice
        var r = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var c = document.querySelector('.plantuml-browser[data-diagram-type=plantuml]');
                var slider = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                return {
                    natural: window.__natural, slider: Number(slider.min), value: Number(slider.value),
                    parts: Array.prototype.map.call(c.querySelectorAll(':scope > .puml-fragment'), function (p) {
                        var svg = p.querySelector('svg');
                        return { level: svg.getBoundingClientRect().width / svg.width.baseVal.value * 100, notes: svg.querySelectorAll('.note-hover-rect').length };
                    })
                };
            }
            """);
        _output.WriteLine($"{r}");
        Assert.Equal(0, r.GetProperty("natural").GetInt32());
        var min = r.GetProperty("slider").GetInt32();
        Assert.True(min < 100, $"the diagram is wider than its box: its slider starts at {min}");
        Assert.Equal(min, r.GetProperty("value").GetInt32());
        foreach (var part in r.GetProperty("parts").EnumerateArray())
            Assert.InRange(part.GetProperty("level").GetDouble(), min - 1, min + 1);
    }

    [Fact]
    public async Task A_toggle_gives_the_diagram_on_screen_its_note_buttons_by_the_time_it_is_done()
    {
        await OpenAllDrawnAsync(Small.Value, 2);
        await Page.EvaluateAsync("() => document.querySelector('.plantuml-browser[data-k-sequence]').scrollIntoView({ block: 'center' })");
        await SettleAsync();
        // the first frame that shows its new picture, and whether its note buttons were placed before it
        await Page.EvaluateAsync("""
            () => {
                var c = document.querySelector('.plantuml-browser[data-k-sequence]'), t = performance.now();
                c.querySelector('svg')._before = true;
                window.__buttonsFirst = null;
                window.__drawingAtPaint = null;
                (function frame() {
                    requestAnimationFrame(function () {
                        var s = c.querySelector('svg');
                        if (s && !s._before) {
                            window.__buttonsFirst = !!s.querySelector('.note-hover-rect');
                            window.__drawingAtPaint = c._noteRendering === true;
                        } else if (performance.now() - t < 60000) frame();
                    });
                })();
            }
            """);

        await ToggleAsync("databases");

        // its new picture was painted before its buttons were placed (5.5.1), and they are in place once the toggle
        // stops showing it is busy, with no pointer over it
        var r = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var svg = document.querySelector('.plantuml-browser[data-k-sequence] svg');
                return { fresh: !svg._before, buttons: !!svg.querySelector('.note-hover-rect'), buttonsFirst: window.__buttonsFirst,
                         drawingAtPaint: window.__drawingAtPaint, drawing: !!document.querySelector('.plantuml-browser[data-k-sequence]')._noteRendering };
            }
            """);
        Assert.True(r.GetProperty("fresh").GetBoolean(), "the diagram on screen was drawn again");
        Assert.True(r.GetProperty("buttons").GetBoolean(), "the diagram on screen has its note buttons once the toggle is done");
        Assert.Equal(JsonValueKind.False, r.GetProperty("buttonsFirst").ValueKind);
        // until its buttons are placed it still counts as being drawn, so whatever waits for a drawing to end (a note's
        // button, a later toggle, a test) finds them in place: 5.5.1 ended the drawing with the picture
        Assert.Equal(JsonValueKind.True, r.GetProperty("drawingAtPaint").ValueKind);
        Assert.False(r.GetProperty("drawing").GetBoolean(), "the drawing ends once the buttons are placed");
    }

    // A diagram whose buttons fail to be placed after the paint is reported, and its drawing still ends and the toggle
    // still finishes, as after a first drawing's hooks: 5.5.2 left it counted as being drawn for good, so its note buttons
    // did nothing and every later toggle waited 15 s for it, and the toggle never finished (5.5.3).
    [Fact]
    public async Task A_toggle_finishes_and_ends_the_drawing_when_placing_the_note_buttons_fails()
    {
        await OpenAllDrawnAsync(Small.Value, 2);
        await Page.EvaluateAsync("() => document.querySelector('.plantuml-browser[data-k-sequence]').scrollIntoView({ block: 'center' })");
        await SettleAsync();
        // once the diagram on screen waits for the paint, placing its buttons reads its notes' states, and that read fails
        await Page.EvaluateAsync("""
            () => {
                var c = document.querySelector('.plantuml-browser[data-k-sequence]'), t = performance.now();
                window.__failed = false;
                (function frame() {
                    requestAnimationFrame(function () {
                        if (c._kPaintWaits > 0) {
                            var steps = c._noteSteps;
                            Object.defineProperty(c, '_noteSteps', { configurable: true, get: function () {
                                window.__failed = true;
                                Object.defineProperty(c, '_noteSteps', { configurable: true, writable: true, value: steps });
                                throw new Error('placing the buttons failed');
                            } });
                        } else if (performance.now() - t < 60000) frame();
                    });
                })();
                document.querySelector('.toolbar-row .toggle-btn[data-toggle="databases"]').click();
            }
            """);

        await Page.WaitForFunctionAsync(
            "() => !document.querySelector('.toggle-btn[data-toggle=\"databases\"][data-pending]')",
            null, new() { Timeout = 30_000, PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__failed"), "placing the buttons failed once");
        Assert.False(await Page.EvaluateAsync<bool>("() => !!document.querySelector('.plantuml-browser[data-k-sequence]')._noteRendering"),
            "the drawing ends though its buttons failed");
    }

    [Fact]
    public async Task A_toggle_finishes_with_the_note_buttons_placed_on_a_page_that_paints_no_frame()
    {
        await OpenAllDrawnAsync(Small.Value, 2);
        await Page.EvaluateAsync("() => document.querySelector('.plantuml-browser[data-k-sequence]').scrollIntoView({ block: 'center' })");
        await SettleAsync();
        // a renderer that produces no frame (a stalled headless one): no requestAnimationFrame callback ever runs, so the
        // paint the diagram on screen waits for before its buttons never comes
        await Page.EvaluateAsync("""
            () => {
                document.querySelector('.plantuml-browser[data-k-sequence] svg')._before = true;
                window.requestAnimationFrame = function () { return 0; };
                document.querySelector('.toolbar-row .toggle-btn[data-toggle="databases"]').click();
            }
            """);

        await Page.WaitForFunctionAsync("() => !document.querySelector('.toggle-btn[data-toggle=\"databases\"][data-pending]')",
            null, new() { Timeout = 15_000, PollingInterval = 200 });
        var r = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var svg = document.querySelector('.plantuml-browser[data-k-sequence] svg');
                return { fresh: !svg._before, buttons: !!svg.querySelector('.note-hover-rect') };
            }
            """);
        Assert.True(r.GetProperty("fresh").GetBoolean(), "the diagram on screen was drawn again");
        Assert.True(r.GetProperty("buttons").GetBoolean(), "the diagram on screen has its note buttons");
    }
}
