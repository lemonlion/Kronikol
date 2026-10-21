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

    /// <summary>Opens the report with every feature and scenario open and every diagram drawn, and lets it settle.</summary>
    private async Task OpenAllDrawnAsync(string? report = null, int diagrams = Diagrams)
    {
        await Page.AddInitScriptAsync(BrowserRenderWorkerTests.BenchInitWithProbe);
        await Page.GotoAsync(report ?? Report.Value, new() { Timeout = PageOpenTimeout });
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
}
