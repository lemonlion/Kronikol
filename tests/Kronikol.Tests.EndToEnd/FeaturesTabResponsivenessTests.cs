using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// On a report of 750 payload-dense scenarios with internal flow (<see cref="DenseReportFixture.GenerateWithFlows"/>),
/// opening the Features tab, sorting the features and a click anywhere answer at once: no main-thread task any of them
/// starts runs long. In 5.0.0 each froze the page on a consumer's report of that shape: the tab asked every diagram
/// whether it showed, styles worked out for each in a closed scenario, and every click laid out every scenario's
/// diagram tab bar, the page laid out once a bar. With every scenario open the tab and the sort still held it for
/// seconds: the page lays out only the scenarios near the screen, and every box read in one far from it laid that one
/// out (5.1.0). What the page now leaves until it is needed is done by then: a bar far down the page is laid out as it
/// comes near. The budgets scale by the run's measured contention (<see cref="ContentionScale"/>), as the render
/// benches' do.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class FeaturesTabResponsivenessTests : PlaywrightTestBase
{
    private readonly ITestOutputHelper _output;

    public FeaturesTabResponsivenessTests(PlaywrightFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    /// <summary>The longest main-thread task an action may cost, before the contention scale.</summary>
    private const double TaskBudgetMs = 200;

    // written once for the class (the whole pipeline takes a while over 750 scenarios), outside any test's folder
    private static readonly Lazy<string> Report = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-dense-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return DenseReportFixture.GenerateWithFlows(dir, OutputDir, "Dense_Flows.html");
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    // one feature of 300: near the screen however far down its last scenarios are, so only a scenario's own box says
    // whether anything in it is near
    private static readonly Lazy<string> OneLargeFeature = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-dense-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return DenseReportFixture.GenerateWithFlows(dir, OutputDir, "Dense_OneFeature.html", features: 1, scenariosPerFeature: 300);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private Task OpenDenseReportAsync() => OpenAsync(Report.Value, 700);

    private async Task OpenAsync(string report, int bars)
    {
        await Page.AddInitScriptAsync(BrowserRenderWorkerTests.BenchInitWithProbe);
        await Page.GotoReportAsync(report, new() { Timeout = PageOpenTimeout });
        Assert.True(await Page.Locator("summary + .diagram-toggle").CountAsync() >= bars, "the scenarios have diagram tab bars");
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

    private Task<double> NowAsync() => Page.EvaluateAsync<double>("() => performance.now()");

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
    public async Task Opening_the_Features_tab_of_a_dense_report_runs_no_long_task()
    {
        await OpenDenseReportAsync();
        await OpenReportTabAsync("timeline");
        await SettleAsync();

        var from = await NowAsync();
        await OpenReportTabAsync("features");
        var (worst, stretch) = await WorstTaskSinceAsync(from);

        AssertWithinBudget("opening the Features tab", worst, stretch);
    }

    [Fact]
    public async Task Sorting_the_features_of_a_dense_report_runs_no_long_task()
    {
        await OpenDenseReportAsync();

        var from = await NowAsync();
        // a reader clicks the list, then picks an order
        var sort = Page.Locator("#features-sort");
        await sort.DispatchEventAsync("click");
        await sort.SelectOptionAsync("duration");
        var (worst, stretch) = await WorstTaskSinceAsync(from);

        AssertWithinBudget("sorting the features", worst, stretch);
    }

    // Every feature and scenario open: the browser lays out only the ones near the screen (content-visibility: auto), and
    // a box read in one far from it lays that one out for the reading
    private async Task ExpandEverythingAsync()
    {
        await ExpandAllScenariosAsync();
        await SettleAsync();
    }

    [Fact]
    public async Task Opening_the_Features_tab_with_every_scenario_open_runs_no_long_task()
    {
        await OpenDenseReportAsync();
        await ExpandEverythingAsync();
        await OpenReportTabAsync("timeline");
        await SettleAsync();

        var from = await NowAsync();
        await OpenReportTabAsync("features");
        var (worst, stretch) = await WorstTaskSinceAsync(from);

        AssertWithinBudget("opening the Features tab with every scenario open", worst, stretch);
    }

    [Fact]
    public async Task Sorting_with_every_scenario_open_runs_no_long_task()
    {
        await OpenDenseReportAsync();
        await ExpandEverythingAsync();

        var from = await NowAsync();
        var sort = Page.Locator("#features-sort");
        await sort.DispatchEventAsync("click");
        await sort.SelectOptionAsync("duration");
        var (worst, stretch) = await WorstTaskSinceAsync(from);

        AssertWithinBudget("sorting with every scenario open", worst, stretch);
    }

    [Fact]
    public async Task Opening_the_Features_tab_with_every_scenario_of_one_large_feature_open_runs_no_long_task()
    {
        await OpenAsync(OneLargeFeature.Value, 290);
        await ExpandEverythingAsync();
        await OpenReportTabAsync("timeline");
        await SettleAsync();

        var from = await NowAsync();
        await OpenReportTabAsync("features");
        var (worst, stretch) = await WorstTaskSinceAsync(from);

        AssertWithinBudget("opening the Features tab with one large feature open", worst, stretch);
    }

    [Fact]
    public async Task A_click_with_every_scenario_of_one_large_feature_open_runs_no_long_task()
    {
        await OpenAsync(OneLargeFeature.Value, 290);
        await ExpandEverythingAsync();

        var from = await NowAsync();
        await Page.Locator(".run-band").First.DispatchEventAsync("click");
        var (worst, stretch) = await WorstTaskSinceAsync(from);

        AssertWithinBudget("a click with one large feature open", worst, stretch);
    }

    /// <summary>
    /// Waits until every diagram tab bar on the screen is laid out on its title line, as each fits at this width, and at
    /// least one is on the screen. Read as the page reads, a feature's box before anything in it, so the check lays out
    /// nothing far from the screen itself.
    /// </summary>
    private Task BarsOnScreenAreLaidOutAsync() => Page.WaitForFunctionAsync("""
        () => {
            var vh = innerHeight, seen = 0;
            function onScreen(el) { var r = el.getBoundingClientRect(); return r.width > 0 && r.bottom > 0 && r.top < vh; }
            var features = document.querySelectorAll('details.feature[open]');
            for (var i = 0; i < features.length; i++) {
                if (!onScreen(features[i])) continue;
                var scenarios = features[i].querySelectorAll('details.scenario[open]');
                for (var j = 0; j < scenarios.length; j++) {
                    if (!onScreen(scenarios[j])) continue;
                    var bars = scenarios[j].querySelectorAll('summary + .diagram-toggle');
                    for (var k = 0; k < bars.length; k++) {
                        if (bars[k].closest('details:not([open]), [hidden]') || !onScreen(bars[k])) continue;
                        seen++;
                        if (bars[k].getAttribute('data-layout') !== 'inline') return false;
                    }
                }
            }
            return seen > 0;
        }
        """, null, new() { Timeout = 30_000, PollingInterval = 200 });

    // What the page leaves until a bar comes near is done by the time it shows: scrolled to, or brought there by a sort.
    [Fact]
    public async Task A_bar_far_down_the_page_is_laid_out_once_it_comes_near()
    {
        await OpenDenseReportAsync();
        await ExpandEverythingAsync();
        var bar = Page.Locator("details.scenario").Last.Locator("summary + .diagram-toggle").First;
        Assert.Null(await bar.GetAttributeAsync("data-layout")); // far from the screen, the page has left it alone

        await bar.EvaluateAsync("b => b.scrollIntoView({ block: 'center' })");

        await Assertions.Expect(bar).ToHaveAttributeAsync("data-layout", "inline", new() { Timeout = 30_000 });
        await BarsOnScreenAreLaidOutAsync();
    }

    [Fact]
    public async Task The_bars_a_sort_brings_onto_the_screen_are_laid_out()
    {
        await OpenDenseReportAsync();
        await ExpandEverythingAsync();
        var first = Page.Locator("#report-content > details.feature > summary").First;
        var before = await first.TextContentAsync();

        var sort = Page.Locator("#features-sort");
        await sort.DispatchEventAsync("click");
        await sort.SelectOptionAsync("duration");

        Assert.NotEqual(before, await first.TextContentAsync()); // the sort put another feature at the top
        await BarsOnScreenAreLaidOutAsync();
    }

    [Fact]
    public async Task A_click_on_a_dense_report_runs_no_long_task()
    {
        await OpenDenseReportAsync();

        var from = await NowAsync();
        await Page.Locator(".run-band").First.DispatchEventAsync("click");
        var (worst, stretch) = await WorstTaskSinceAsync(from);

        AssertWithinBudget("a click on the run band", worst, stretch);
    }
}
