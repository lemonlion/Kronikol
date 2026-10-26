using System.Text.Json;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The search's budgets (plans/SEARCH_PLAN.md 4.12) on the 2,000-scenario report (<see cref="SearchScaleFixture"/>), in
/// Chromium, each scaled by the load measured while it ran as the render bench's are (<see cref="ContentionScale"/>):
/// <c>SearchOpenMs</c>, the query builder open and painted within 50 ms; a keystroke's suggestions within 16 ms;
/// <c>SearchResultsMs</c>, Enter to a painted page of results within the instant pass's own time and 100 ms more, past
/// what hiding the Features tab costs Chrome (its 2,000 scenarios' layout taken down, 60 to 90 ms on a quiet machine, which
/// any tab that leaves it pays too: timed here by hiding the panel and laying the page out, before the search); and
/// <c>HitOpenMs</c>, what following a result's line adds once the navigation script has arrived at its scenario, within
/// 150 ms (the tab change and the arrival are the navigation script's, timed by its own facts). Measured once the page has
/// gone a second without a long task, so the page's own start-up work is not charged to the search; three attempts, as the
/// bench has, so one stretch of load the probe did not see cannot fail the build.
/// </summary>
[Collection(PlaywrightCollections.Search)]
public class SearchBudgetTests : PlaywrightTestBase
{
    private readonly ITestOutputHelper _output;

    public SearchBudgetTests(PlaywrightFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    private sealed record Measured(double OpenMs, double KeyMs, double ResultsMs, double InstantMs, double SwitchMs, double HitMs, double LineMs, double Stretch);

    private string? _url;

    private async Task<Measured> MeasureAsync()
    {
        _url ??= SearchScaleFixture.Generate(TempDir, OutputDir, "SearchScale.html");
        await Page.GotoReportAsync(_url);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 120000 });
        await Page.WaitForFunctionAsync(
            "() => !!window.kronikol_search && !!window.kronikol_search_results && !!window.kronikol_hit && window.__bench && window.__bench.probe.length >= 3",
            null, new() { Timeout = 120000, PollingInterval = 200 });
        await Page.WaitForFunctionAsync("""
            () => {
                const b = window.__bench, now = performance.now();
                const last = b.longTasks.reduce((m, l) => Math.max(m, l.start + l.duration), 0);
                return now - last > 1000;
            }
            """, null, new() { Timeout = 120000, PollingInterval = 200 });

        // what hiding the Features tab costs the browser, the search aside: the panel hidden and the page laid out, then back
        var switchMs = await Page.EvaluateAsync<double>("""
            () => {
                const panel = document.getElementById('panel-features');
                void document.body.offsetHeight;
                const t = performance.now();
                panel.hidden = true;
                void document.body.offsetHeight;
                const ms = performance.now() - t;
                panel.hidden = false;
                void document.body.offsetHeight;
                return ms;
            }
            """);

        // the search button to the builder painted, its input holding the focus
        var open = await Page.EvaluateAsync<JsonElement>("""
            () => new Promise(resolve => {
                const t = performance.now();
                document.getElementById('search-button').click();
                requestAnimationFrame(() => resolve({ ms: performance.now() - t, focused: document.activeElement === document.getElementById('search-input') }));
            })
            """);
        Assert.True(open.GetProperty("focused").GetBoolean(), "the builder did not open with its input focused");

        // each keystroke of a word, its suggestions drawn before the handler returns (report-search-ui-script.js)
        var keys = await Page.EvaluateAsync<double[]>("""
            () => {
                const input = document.getElementById('search-input'), times = [];
                for (const draft of ['o', 'or', 'ord', 'orde', 'order']) {
                    input.value = draft;
                    const t = performance.now();
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    times.push(performance.now() - t);
                }
                return times;
            }
            """);
        Assert.True(await Page.Locator("#search-suggestions [role=option]").CountAsync() > 0, "no suggestions for 'order'");

        // Enter to the Search tab's first page of results painted
        var results = await Page.EvaluateAsync<JsonElement>("""
            () => new Promise((resolve, reject) => {
                setTimeout(() => reject(new Error('no page of results within 30 s')), 30000);
                const input = document.getElementById('search-input');
                const t = performance.now();
                input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
                const painted = () => {
                    const panel = document.getElementById('panel-search');
                    if (panel && !panel.hidden && panel.querySelector('.search-result'))
                        requestAnimationFrame(() => resolve({ ms: performance.now() - t, instantMs: window.kronikol_search_results.timing().instantMs }));
                    else requestAnimationFrame(painted);
                };
                painted();
            })
            """);

        // the first result's first line followed to its place: from the navigation script handing the match over, once it
        // has arrived at the scenario, to the focus on the place
        var hit = await Page.EvaluateAsync<JsonElement>("""
            () => new Promise((resolve, reject) => {
                setTimeout(() => reject(new Error('the way to the match did not end within 30 s')), 30000);
                const line = document.querySelector('#panel-search .search-result .search-line, #panel-search .search-result .search-result-title');
                const follow = window.kronikol_hit.follow;
                let handed = 0;
                window.kronikol_hit.follow = function () { handed = performance.now(); return follow.apply(this, arguments); };
                const t = performance.now();
                line.click();
                const there = () => {
                    const f = document.activeElement;
                    const said = document.querySelector('.kron-nav-status');
                    if (handed && f && f.closest('#panel-features') && said && said.textContent.indexOf('Match in') === 0) {
                        const now = performance.now();
                        window.kronikol_hit.follow = follow;
                        resolve({ ms: now - handed, total: now - t });
                    }
                    else requestAnimationFrame(there);
                };
                requestAnimationFrame(there);
            })
            """);

        var probe = await Page.EvaluateAsync<double[]>("() => window.__bench.probe");
        return new Measured(open.GetProperty("ms").GetDouble(), keys.Max(), results.GetProperty("ms").GetDouble(),
            results.GetProperty("instantMs").GetDouble(), switchMs, hit.GetProperty("ms").GetDouble(), hit.GetProperty("total").GetDouble(),
            ContentionScale.Stretch(probe));
    }

    [Fact]
    public async Task The_search_keeps_its_budgets_on_the_2000_scenario_report()
    {
        await Page.AddInitScriptAsync(BrowserRenderWorkerTests.BenchInitWithProbe);
        await Page.SetViewportSizeAsync(1280, 800);

        const int attempts = 3;
        var failure = "";
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var m = await MeasureAsync();
            var s = m.Stretch;
            _output.WriteLine($"attempt {attempt}: open {m.OpenMs:F1} ms, worst keystroke {m.KeyMs:F1} ms, Enter to results {m.ResultsMs:F1} ms "
                + $"(instant pass {m.InstantMs:F1} ms, tab change {m.SwitchMs:F1} ms), line to its place {m.HitMs:F1} ms after the arrival ({m.LineMs:F1} ms from the click); stretch {s:F2}");
            failure =
                m.OpenMs >= 50 * s ? $"SearchOpenMs: the builder opened in {m.OpenMs:F0} ms (budget 50 x {s:F2})" :
                m.KeyMs >= 16 * s ? $"a keystroke's suggestions took {m.KeyMs:F1} ms (budget 16 x {s:F2})" :
                m.ResultsMs - m.InstantMs - m.SwitchMs >= 100 * s ? $"SearchResultsMs: Enter to the results took {m.ResultsMs:F0} ms, {m.ResultsMs - m.InstantMs - m.SwitchMs:F0} ms past the instant pass's {m.InstantMs:F0} ms and the tab change's {m.SwitchMs:F0} ms (budget 100 x {s:F2})" :
                m.HitMs >= 150 * s ? $"HitOpenMs: a line took {m.HitMs:F0} ms past the arrival to its place (budget 150 x {s:F2})" :
                "";
            if (failure.Length == 0) return;
            _output.WriteLine($"attempt {attempt}/{attempts} breached a budget: {failure}");
        }
        Assert.Fail($"the search's contention-scaled budgets were breached on all {attempts} attempts; last: {failure}");
    }
}
