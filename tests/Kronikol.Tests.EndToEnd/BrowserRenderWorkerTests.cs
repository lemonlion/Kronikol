using System.Text.Json;
using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Browser rendering off the main thread (BROWSER_RENDER_WORKER_PLAN.md): the engine runs in Web
/// Workers, the page stays interactive, fragments render in parallel and identical fragments come from
/// a cache. These tests measure the large-report fixture (see <see cref="LargeReportFixture"/>) and
/// pin the behaviour the design guarantees. Structural/relative assertions must hold on every run;
/// the absolute time budgets are capability checks that CPU contention (a saturated CI runner, a
/// parallel full-suite run) can breach on any single attempt, so the perf test retries the whole
/// measurement and passes on one clean attempt — see the comment inside it. All numbers are written
/// to the test output for the record.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class BrowserRenderWorkerTests : PlaywrightTestBase
{
    private readonly ITestOutputHelper _output;

    public BrowserRenderWorkerTests(PlaywrightFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    /// <summary>
    /// Installed before any page script: stamps when <c>body.plantuml-ready</c> appears and records every
    /// main-thread long task (PerformanceObserver 'longtask', ≥ 50 ms by definition).
    /// </summary>
    internal const string BenchInitScript = """
        (function () {
            window.__bench = { readyAt: null, longTasks: [] };
            function markReady() {
                if (window.__bench.readyAt === null && document.body && document.body.classList.contains('plantuml-ready'))
                    window.__bench.readyAt = performance.now();
            }
            // The page adds plantuml-ready in a DOMContentLoaded listener on document; a window listener
            // runs in the bubble phase after every document listener, i.e. right after the class is added.
            window.addEventListener('DOMContentLoaded', markReady);
            var poll = setInterval(function () { markReady(); if (window.__bench.readyAt !== null) clearInterval(poll); }, 4);
            try {
                new PerformanceObserver(function (list) {
                    list.getEntries().forEach(function (e) { window.__bench.longTasks.push({ start: e.startTime, duration: e.duration }); });
                }).observe({ type: 'longtask', buffered: true });
            } catch (e) { }
        })();
        """;

    /// <summary>Every diagram container and every fragment has finished (an svg, or the engine's failure text).</summary>
    internal const string AllRenderedJs = """
        () => {
            var els = document.querySelectorAll('.plantuml-browser');
            if (!els.length) return false;
            for (var i = 0; i < els.length; i++) {
                var el = els[i];
                if (el.dataset.rendered !== '1') return false;
                var frags = el.querySelectorAll('.puml-fragment');
                if (frags.length) {
                    for (var f = 0; f < frags.length; f++) {
                        if (frags[f].dataset.rendered !== '1') return false;
                        if (!frags[f].querySelector('svg') && !(frags[f].textContent || '').trim()) return false;
                    }
                } else if (!el.querySelector('svg') && !(el.textContent || '').trim()) return false;
            }
            return true;
        }
        """;

    private sealed record Metrics(double ReadyMs, double EngineReadyMs, double AllMs, double BlockedMs, double WorstTaskMs, int LongTasks,
        double ToggleMs, double ToggleBlockedMs, double ToggleWorstTaskMs, int ToggleRenders, int Fragments,
        string Mode, int Workers, int ExpectedWorkers, int MaxInFlight, int Renders, int CacheHits, double WorkerMs, double InjectMs);

    private bool _benchInitInstalled;

    private async Task<Metrics> MeasureLargeReport(string fileName, int workers = Constants.TrackingDefaults.BrowserRenderWorkers)
    {
        // Register the bench init script once per page — a second registration
        // would run twice on each navigation and double-count long tasks.
        if (!_benchInitInstalled)
        {
            await Page.AddInitScriptAsync(BenchInitScript);
            _benchInitInstalled = true;
        }
        await Page.GotoAsync(LargeReportFixture.Generate(TempDir, OutputDir, fileName, browserRenderWorkers: workers));
        await Page.WaitForFunctionAsync("() => window.__bench && window.__bench.readyAt !== null", null,
            new() { Timeout = 120000, PollingInterval = 200 });
        var readyAt = await Page.EvaluateAsync<double>("() => window.__bench.readyAt");

        await ExpandFirstScenarioWithDiagram();

        // Force every diagram (the IntersectionObserver only covers what is on screen) and time it.
        var t0 = await Page.EvaluateAsync<double>(
            "() => { var t = performance.now(); window._renderDiagramsInContainer(document.body); return t; }");
        await Page.WaitForFunctionAsync(AllRenderedJs, null, new() { Timeout = 300000, PollingInterval = 200 });
        var t1 = await Page.EvaluateAsync<double>("() => performance.now()");
        var (blocked, worst, count) = await LongTasksBetween(t0, t1);

        // Toggle: expand the first collapsed note of the diagram with the most fragments.
        var toggle = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var best = null, bestFrags = -1;
                document.querySelectorAll('.plantuml-browser').forEach(function (el) {
                    var n = el.querySelectorAll('.puml-fragment').length;
                    if (n > bestFrags && el.querySelector('.note-toggle-icon')) { best = el; bestFrags = n; }
                });
                if (!best) return { ok: false, reason: 'no diagram with note toggles' };
                var icons = best.querySelectorAll('.note-toggle-icon');
                var icon = null;
                for (var i = 0; i < icons.length; i++) if (icons[i].getAttribute('data-note-btn') === 'plus') { icon = icons[i]; break; }
                if (!icon) icon = icons[0];
                var rect = icon.querySelector('rect');
                if (!rect) return { ok: false, reason: 'icon without rect' };
                var before = Array.prototype.slice.call(best.querySelectorAll('svg'));
                var stats = window.__kronikolRender || {};
                window.__toggle = { t0: 0, t1: null, rendersBefore: stats.renders || 0 };
                var mo = new MutationObserver(function () {
                    if (window.__toggle.t1 !== null) return;
                    if (best._noteRendering || window._plantumlRendering) return;
                    if (best.querySelector('.puml-fragment-new')) return;
                    var cur = Array.prototype.slice.call(best.querySelectorAll('svg'));
                    if (!cur.length) return;
                    for (var c = 0; c < cur.length; c++) if (before.indexOf(cur[c]) < 0) { window.__toggle.t1 = performance.now(); mo.disconnect(); return; }
                });
                mo.observe(best, { childList: true, subtree: true });
                window.__toggle.t0 = performance.now();
                rect.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                return { ok: true, fragments: bestFrags, id: best.id };
            }
            """);
        Assert.True(toggle.GetProperty("ok").GetBoolean(), toggle.ToString());
        await Page.WaitForFunctionAsync("() => window.__toggle && window.__toggle.t1 !== null", null,
            new() { Timeout = 120000, PollingInterval = 200 });
        var tt0 = await Page.EvaluateAsync<double>("() => window.__toggle.t0");
        var tt1 = await Page.EvaluateAsync<double>("() => window.__toggle.t1");
        var (tBlocked, tWorst, _) = await LongTasksBetween(tt0, tt1);
        var rendersBefore = await Page.EvaluateAsync<int>("() => window.__toggle.rendersBefore");
        var t = await Page.EvaluateAsync<JsonElement>("""
            () => {
                var r = window.__kronikolRender || {};
                return {
                    mode: r.mode || 'legacy', workers: r.workers || 0, renders: r.renders || 0, cacheHits: r.cacheHits || 0,
                    maxInFlight: r.maxInFlight || 0, workerMs: r.workerMs || 0, injectMs: r.injectMs || 0,
                    engineReadyAt: r.engineReadyAt || 0,
                    expectedWorkers: Math.max(1, Math.min(r.workersRequested || 0, navigator.hardwareConcurrency || 2))
                };
            }
            """);

        var m = new Metrics(readyAt, t.GetProperty("engineReadyAt").GetDouble(), t1 - t0, blocked, worst, count, tt1 - tt0, tBlocked, tWorst,
            t.GetProperty("renders").GetInt32() - rendersBefore, toggle.GetProperty("fragments").GetInt32(),
            t.GetProperty("mode").GetString()!, t.GetProperty("workers").GetInt32(), t.GetProperty("expectedWorkers").GetInt32(),
            t.GetProperty("maxInFlight").GetInt32(), t.GetProperty("renders").GetInt32(), t.GetProperty("cacheHits").GetInt32(),
            t.GetProperty("workerMs").GetDouble(), t.GetProperty("injectMs").GetDouble());
        _output.WriteLine("render-bench " + fileName + ": " + JsonSerializer.Serialize(m));
        File.AppendAllText(Path.Combine(OutputDir, "render-bench-results.txt"),
            $"{DateTime.UtcNow:O} {fileName} {JsonSerializer.Serialize(m)}{Environment.NewLine}");
        return m;
    }

    private async Task<(double Blocked, double Worst, int Count)> LongTasksBetween(double from, double to)
    {
        var r = await Page.EvaluateAsync<JsonElement>("""
            (w) => {
                var blocked = 0, worst = 0, n = 0;
                (window.__bench.longTasks || []).forEach(function (t) {
                    if (t.start + t.duration < w[0] || t.start > w[1]) return;
                    blocked += t.duration; n++; if (t.duration > worst) worst = t.duration;
                });
                return { blocked: blocked, worst: worst, n: n };
            }
            """, new[] { from, to });
        return (r.GetProperty("blocked").GetDouble(), r.GetProperty("worst").GetDouble(), r.GetProperty("n").GetInt32());
    }

    // ═══════════════════════════════════════════════════════════
    // Large report: interactive at once, parallel render, no freezes, fast toggles
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Large_report_renders_off_the_main_thread_within_budget()
    {
        // Two kinds of guarantee, treated differently. The STRUCTURAL/RELATIVE ones (worker mode,
        // worker count, ready-never-waits-for-the-engine, parallel in-flight renders, toggle
        // re-renders only its own fragments) are load-independent and must hold on every attempt.
        // The ABSOLUTE budgets are capability checks that pure CPU contention can push over on any
        // single attempt: raising them chased the load instead of removing it (2500→4500 ReadyMs
        // then 7208 measured under a 51-project local suite; 500 WorstTaskMs then 582 and 800
        // ToggleWorstTaskMs then 1254 measured on a saturated 2-core CI runner, blocked 23.9 s of
        // 33.1 s). So the budgets keep their calibrated values and the measurement retries: one
        // clean attempt out of three proves the page CAN run unblocked, while a real regression
        // (rendering back on the main thread is a multi-second task every time) fails all three.
        const int attempts = 3;
        string budgetFailure = "";
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var m = await MeasureLargeReport($"LargeRenderBench{(attempt == 1 ? "" : "-retry" + attempt)}.html");

            Assert.True(m.Mode == "worker", "expected worker mode, got " + m.Mode);
            Assert.Equal(m.ExpectedWorkers, m.Workers);
            Assert.True(m.ReadyMs < m.EngineReadyMs, $"plantuml-ready ({m.ReadyMs:F0} ms) must not wait for the engine ({m.EngineReadyMs:F0} ms)");
            // Phase 3: several fragments in flight at once (the first worker renders alone until its
            // first result; the rest of the fixture fans out over the remaining workers).
            Assert.True(m.MaxInFlight >= Math.Min(2, m.ExpectedWorkers), $"expected parallel renders, max in flight was {m.MaxInFlight}");
            // Phase 4: the toggle re-renders only what changed (prefetch + cache).
            Assert.True(m.ToggleRenders <= m.Fragments + 2, $"toggle re-rendered {m.ToggleRenders} fragments of {m.Fragments}");

            // Absolute budgets — isolated runs measure well under half of each of these. What stays
            // on the main thread is innerHTML injection of multi-MB SVGs and the post-render hooks.
            budgetFailure =
                m.ReadyMs >= 4500 ? $"page should be interactive long before the engine is loaded; plantuml-ready at {m.ReadyMs:F0} ms" :
                m.WorstTaskMs >= 500 ? $"worst main-thread long task during the full render was {m.WorstTaskMs:F0} ms (blocked {m.BlockedMs:F0} ms of {m.AllMs:F0} ms)" :
                m.AllMs >= 60000 ? $"full render of the fixture took {m.AllMs:F0} ms" :
                m.ToggleMs >= 5000 ? $"note toggle on the largest diagram took {m.ToggleMs:F0} ms" :
                m.ToggleWorstTaskMs >= 800 ? $"note toggle blocked the main thread for a {m.ToggleWorstTaskMs:F0} ms task" :
                "";
            if (budgetFailure.Length == 0) return;
            _output.WriteLine($"attempt {attempt}/{attempts} breached an absolute budget (contention?): {budgetFailure}");
        }
        Assert.Fail($"absolute budgets breached on all {attempts} attempts — this is sustained, not contention; last: {budgetFailure}");
    }

    [Fact]
    public async Task Zero_workers_option_renders_on_the_main_thread()
    {
        await Page.GotoAsync(LargeReportFixture.Generate(TempDir, OutputDir, "ZeroWorkers.html", diagrams: 2, stepsPerDiagram: 2, browserRenderWorkers: 0));
        await Page.WaitForFunctionAsync("() => document.body.classList.contains('plantuml-ready')", null, new() { Timeout = 60000, PollingInterval = 200 });
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        var r = await Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender");
        Assert.Equal("main-thread", r.GetProperty("mode").GetString());
        Assert.Equal(0, r.GetProperty("workers").GetInt32());
        Assert.Contains("BrowserRenderWorkers = 0", r.GetProperty("fallbackReason").GetString());
        Assert.True(r.GetProperty("renders").GetInt32() >= 1);
    }

    // ═══════════════════════════════════════════════════════════
    // Phase 2: worker mode from file://, fallback, failure markup
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Diagram_renders_in_a_worker_from_a_file_url()
    {
        var url = GenerateReport("WorkerFileUrl.html");
        Assert.StartsWith("file:", url);
        await Page.GotoAsync(url);
        await Page.WaitForFunctionAsync("() => document.body.classList.contains('plantuml-ready')", null, new() { Timeout = 60000, PollingInterval = 200 });
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        var r = await Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender");
        Assert.Equal("worker", r.GetProperty("mode").GetString());
        Assert.True(r.GetProperty("workers").GetInt32() >= 1);
        Assert.True(r.GetProperty("renders").GetInt32() >= 1);
        Assert.True(r.GetProperty("engineFetchMs").GetDouble() > 0);
        // The engine never ran on the main thread: no <script src=…plantuml.js> was injected.
        var engineScripts = await Page.EvaluateAsync<int>("() => document.querySelectorAll('script[src*=\"plantuml.js\"]').length");
        Assert.Equal(0, engineScripts);
    }

    [Fact]
    public async Task Falls_back_to_the_main_thread_engine_when_workers_are_unavailable()
    {
        await Page.AddInitScriptAsync("window.Worker = undefined;");
        await Page.GotoAsync(GenerateReport("WorkerFallback.html"));
        await Page.WaitForFunctionAsync("() => document.body.classList.contains('plantuml-ready')", null, new() { Timeout = 60000, PollingInterval = 200 });
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(90000);
        var r = await Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender");
        Assert.Equal("main-thread", r.GetProperty("mode").GetString());
        Assert.Contains("Worker unavailable", r.GetProperty("fallbackReason").GetString());
        var engineScripts = await Page.EvaluateAsync<int>("() => document.querySelectorAll('script[src*=\"plantuml.js\"]').length");
        Assert.Equal(1, engineScripts);
        // And the post-render hooks still ran (notes are interactive on the fallback path too).
        await WaitForNoteElements();
    }

    [Fact]
    public async Task Engine_failure_in_a_worker_surfaces_the_raw_plantuml_block()
    {
        // A note with one 60,000-character unbreakable line is wider than the engine's canvas: it
        // answers with text ("Diagram too large for browser rendering…") instead of an <svg>. Through
        // the worker that text must still become the legible block with the collapsed raw source.
        var source = "@startuml\nAlice -> Bob: Hello\nnote right\n" + new string('x', 60000) + "\nend note\n@enduml";
        var encoded = System.Net.WebUtility.HtmlEncode(source);
        var html = $$"""
            <!DOCTYPE html><html><head><title>too large</title>
            <style>{{DiagramContextMenu.GetInlineSvgStyles()}}</style>
            {{DiagramContextMenu.GetPlantUmlBrowserRenderScript()}}
            </head><body><div class="scenario">
            <div class="plantuml-browser" id="puml-1" data-plantuml="{{encoded}}" data-diagram-type="plantuml"></div>
            </div></body></html>
            """;
        await Page.GotoAsync(ServePage(html));
        await Page.Locator("[data-engine-failure='too-large']").WaitForAsync(new() { Timeout = 120000 });
        var text = await Page.Locator("#puml-1").InnerTextAsync();
        Assert.Contains("Diagram too large for client-side rendering", text);
        Assert.Contains("Raw PlantUML", text);
        Assert.Equal("1", await Page.Locator("#puml-1").GetAttributeAsync("data-rendered"));
        var r = await Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender");
        Assert.Equal("worker", r.GetProperty("mode").GetString());
        // A failure is never cached as a result.
        Assert.Equal(0, r.GetProperty("cacheEntries").GetInt32());
    }

    // ═══════════════════════════════════════════════════════════
    // Phase 1: fidelity — worker output equals main-thread output
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Worker_output_matches_main_thread_output()
    {
        string[] sources =
        [
            "@startuml\nparticipant Alice\nparticipant Bob\nAlice -> Bob: Hello\nBob --> Alice: Hi\n@enduml",
            "@startuml\nparticipant \"AuthenticationService\" as a1\nparticipant \"OrderProcessingUnit\" as a4\nparticipant \"InventoryTracker\" as a5\n" +
            "a1 -> a4 : processOrder\nnote left\nOrder payload\n{\"item\":\"Widget\",\"qty\":2}\nend note\na4 -> a5 : checkInventory\na5 --> a1 : complete\n@enduml",
            LargeReportFixture.BuildDiagram(0, 3)
        ];
        await Page.GotoAsync(GenerateReport("WorkerFidelity.html"));
        await Page.WaitForFunctionAsync("() => document.body.classList.contains('plantuml-ready')", null, new() { Timeout = 60000, PollingInterval = 200 });

        // Worker side: through the shim, raw (no post-render hooks), into fresh divs.
        var worker = await Page.EvaluateAsync<JsonElement>("""
            (sources) => new Promise(function (resolve) {
                var left = sources.length, out = [];
                sources.forEach(function (src, i) {
                    var el = document.createElement('div'); el.id = 'fid-wk-' + i; document.body.appendChild(el);
                    new MutationObserver(function (m, mo) {
                        var svg = el.querySelector('svg'); if (!svg) return; mo.disconnect();
                        out[i] = { width: svg.getAttribute('width'), height: svg.getAttribute('height'), viewBox: svg.getAttribute('viewBox'),
                                   texts: svg.querySelectorAll('text').length, elements: svg.querySelectorAll('*').length };
                        if (--left === 0) resolve(out);
                    }).observe(el, { childList: true, subtree: true });
                    window.plantuml.render(src.split('\n'), el.id);
                });
            })
            """, sources);
        var mode = await Page.EvaluateAsync<string>("() => window.__kronikolRender.mode");
        Assert.Equal("worker", mode);

        // Main-thread side: load the real engine into the page and render the same sources with it.
        var main = await Page.EvaluateAsync<JsonElement>("""
            (args) => new Promise(function (resolve, reject) {
                var cdn = args[0], sources = args[1];
                var shim = window.plantuml; window.plantuml = undefined;
                // A promise that never settles hangs the whole test run (EvaluateAsync has no timeout).
                setTimeout(function () { reject(new Error('main-thread engine did not render within 180 s')); }, 180000);
                function add(src) { return new Promise(function (res, rej) { var s = document.createElement('script'); s.src = src; s.async = false; s.onload = res; s.onerror = function () { rej(new Error('load ' + src)); }; document.head.appendChild(s); }); }
                var preLoad = window.plantumlLoad;
                add(cdn + '/viz-global.js').catch(function () {});
                add(cdn + '/plantuml.js').then(function () {
                    // A classic build replaces window.plantumlLoad; the ES-module build (parsed to
                    // nothing as a classic script) leaves the shim's no-op — import() it instead,
                    // exactly as the shim's own main-thread fallback does.
                    if (typeof window.plantumlLoad === 'function' && window.plantumlLoad !== preLoad) {
                        return new Promise(function (res) {
                            window.plantumlLoad([], function () {
                                var engine = window.plantuml; window.plantuml = shim;
                                res(function (lines, id) { engine.render(lines, id); });
                            });
                        });
                    }
                    return new Function('u', 'return import(u)')(cdn + '/plantuml.js').then(function (mod) {
                        if (!mod || typeof mod.render !== 'function') throw new Error('the engine module has no render export');
                        window.plantuml = shim;
                        return function (lines, id) { mod.render(lines, id, {}); };
                    });
                }).then(function (render) {
                    var left = sources.length, out = [];
                    function renderNext(i) {
                        if (i >= sources.length) return;
                        var el = document.createElement('div'); el.id = 'fid-mt-' + i; document.body.appendChild(el);
                        new MutationObserver(function (m, mo) {
                            var svg = el.querySelector('svg'); if (!svg) return; mo.disconnect();
                            out[i] = { width: svg.getAttribute('width'), height: svg.getAttribute('height'), viewBox: svg.getAttribute('viewBox'),
                                       texts: svg.querySelectorAll('text').length, elements: svg.querySelectorAll('*').length };
                            if (--left === 0) resolve(out); else renderNext(i + 1);
                        }).observe(el, { childList: true, subtree: true });
                        render(sources[i].split('\n'), el.id);
                    }
                    renderNext(0);
                }).catch(reject);
            })
            """, new object[] { Constants.TrackingDefaults.PlantUmlJsCdnBase, sources });

        for (var i = 0; i < sources.Length; i++)
        {
            var w = worker[i]; var m = main[i];
            _output.WriteLine($"fidelity[{i}] worker={w} main={m}");
            Assert.Equal(m.GetProperty("width").GetString(), w.GetProperty("width").GetString());
            Assert.Equal(m.GetProperty("height").GetString(), w.GetProperty("height").GetString());
            Assert.Equal(m.GetProperty("viewBox").GetString(), w.GetProperty("viewBox").GetString());
            Assert.Equal(m.GetProperty("texts").GetInt32(), w.GetProperty("texts").GetInt32());
            Assert.Equal(m.GetProperty("elements").GetInt32(), w.GetProperty("elements").GetInt32());
        }
    }

    // ═══════════════════════════════════════════════════════════
    // Phase 4: cache — hits are synchronous, observers still fire, the byte bound holds
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Second_render_of_an_identical_source_is_a_synchronous_cache_hit_that_still_fires_the_observer()
    {
        await Page.GotoAsync(GenerateReport("WorkerCacheHit.html"));
        await Page.WaitForFunctionAsync("() => document.body.classList.contains('plantuml-ready')", null, new() { Timeout = 60000, PollingInterval = 200 });
        // Let the page's own (first-scenario) diagram land first, so its cache entry cannot interleave.
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await Page.WaitForFunctionAsync("() => !window._plantumlRendering && window.__kronikolRender.inFlight === 0", null, new() { Timeout = 60000, PollingInterval = 200 });
        var r = await Page.EvaluateAsync<JsonElement>("""
            () => new Promise(function (resolve) {
                var src = '@startuml\nA -> B: cache me\nB --> A: ok\n@enduml'.split('\n');
                var a = document.createElement('div'); a.id = 'cache-a'; document.body.appendChild(a);
                new MutationObserver(function (m, mo) {
                    if (!a.querySelector('svg')) return; mo.disconnect();
                    var hitsBefore = window.__kronikolRender.cacheHits, rendersBefore = window.__kronikolRender.renders;
                    var b = document.createElement('div'); b.id = 'cache-b'; document.body.appendChild(b);
                    var observerFired = false;
                    new MutationObserver(function (m2, mo2) { if (b.querySelector('svg')) { observerFired = true; mo2.disconnect(); } }).observe(b, { childList: true, subtree: true });
                    window.plantuml.render(src, 'cache-b');
                    var syncSvg = !!b.querySelector('svg');
                    setTimeout(function () {
                        resolve({ syncSvg: syncSvg, observerFired: observerFired,
                                  hits: window.__kronikolRender.cacheHits - hitsBefore,
                                  renders: window.__kronikolRender.renders - rendersBefore,
                                  same: a.innerHTML === b.innerHTML });
                    }, 50);
                }).observe(a, { childList: true, subtree: true });
                window.plantuml.render(src, 'cache-a');
            })
            """);
        Assert.True(r.GetProperty("syncSvg").GetBoolean(), "cache hit should be injected synchronously");
        Assert.True(r.GetProperty("observerFired").GetBoolean(), "the caller's MutationObserver must still fire on a cache hit");
        Assert.Equal(1, r.GetProperty("hits").GetInt32());
        Assert.Equal(0, r.GetProperty("renders").GetInt32());
        Assert.True(r.GetProperty("same").GetBoolean());
    }

    [Fact]
    public async Task Cache_eviction_respects_the_byte_bound()
    {
        await Page.GotoAsync(GenerateReport("WorkerCacheBound.html"));
        await Page.WaitForFunctionAsync("() => document.body.classList.contains('plantuml-ready')", null, new() { Timeout = 60000, PollingInterval = 200 });
        // Let the page's own (first-scenario) diagram land first, so its cache entry cannot interleave.
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await Page.WaitForFunctionAsync("() => !window._plantumlRendering && window.__kronikolRender.inFlight === 0", null, new() { Timeout = 60000, PollingInterval = 200 });
        var r = await Page.EvaluateAsync<JsonElement>("""
            () => new Promise(function (resolve) {
                function render(id, src) {
                    return new Promise(function (res) {
                        var el = document.createElement('div'); el.id = id; document.body.appendChild(el);
                        new MutationObserver(function (m, mo) { if (el.querySelector('svg') || (el.textContent || '').trim()) { mo.disconnect(); res(); } }).observe(el, { childList: true, subtree: true });
                        window.plantuml.render(src.split('\n'), id);
                    });
                }
                window.plantuml.clearCache();
                render('bound-1', '@startuml\nA -> B: first\n@enduml').then(function () {
                    var s1 = window.plantuml.cacheStats();
                    return render('bound-2', '@startuml\nA -> B: second, a bit longer label here\n@enduml').then(function () {
                        var both = window.plantuml.cacheStats();
                        // Room for the newer (second) entry only: the older one must be evicted.
                        window.plantuml.setCacheLimit(both.bytes - s1.bytes + 64);
                        var s2 = window.plantuml.cacheStats();
                        var hitsBefore = s2.hits;
                        // The first source is gone (a miss), the second is present (a hit).
                        var c = document.createElement('div'); c.id = 'bound-3'; document.body.appendChild(c);
                        window.plantuml.render('@startuml\nA -> B: second, a bit longer label here\n@enduml'.split('\n'), 'bound-3');
                        var secondHit = window.plantuml.cacheStats().hits - hitsBefore;
                        resolve({ entries1: s1.entries, bytes1: s1.bytes, entriesBoth: both.entries, entries2: s2.entries, bytes2: s2.bytes, limit: s2.limit, secondHit: secondHit, thirdSync: !!c.querySelector('svg') });
                    });
                });
            })
            """);
        Assert.Equal(1, r.GetProperty("entries1").GetInt32());
        Assert.Equal(2, r.GetProperty("entriesBoth").GetInt32());
        Assert.Equal(1, r.GetProperty("entries2").GetInt32());
        Assert.True(r.GetProperty("bytes2").GetDouble() <= r.GetProperty("limit").GetDouble());
        Assert.Equal(1, r.GetProperty("secondHit").GetInt32());
        Assert.True(r.GetProperty("thirdSync").GetBoolean());
    }
}
