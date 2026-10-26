using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The first-call warm-up as the report paints it (plans/WARM_UP_PLAN.md 4.5, R2, T40 to T43). The run's first call
/// down a path can take many times what later ones do, and whichever scenario goes first pays it: a marked scenario's
/// badge keeps its wall time and adds the warm-up as muted text, the Scenario Timeline shades the warm-up's share of its
/// bar, and the P50 to P99 filter ranks each scenario by the time it took of its own.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class WarmUpMarkTests : PlaywrightTestBase
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    public WarmUpMarkTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>
    /// "Place" pays a 1,500 ms first call and runs 2,000 ms; "Place again" runs 1,200 ms with a 5 ms call of the same
    /// shape; "Browse" and "Search" run 300 and 400 ms with no call. By wall time "Place" is the slowest; by its own time
    /// (500 ms) it is below "Place again".
    /// </summary>
    private string Generate(string fileName, bool includeTestRunData = true, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null) =>
        WriteOrders(TempDir, OutputDir, fileName, includeTestRunData, diagrams);

    /// <summary>The same report for another class's facts (TimelineTabTests, 5.0.0): written in <paramref name="tempDir"/>,
    /// copied to <paramref name="outputDir"/>.</summary>
    internal static string WriteOrders(string tempDir, string outputDir, string fileName, bool includeTestRunData = true,
        DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null)
    {
        var logs = new List<RequestResponseLog>();
        Call(logs, "place", 0, 1500);
        Call(logs, "again", 3000, 5);
        Call(logs, "again", 3100, 5);
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    Scenario("place", "Place", 2000),
                    Scenario("again", "Place again", 1200),
                    Scenario("browse", "Browse", 300),
                    Scenario("search", "Search", 400)
                ]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport(diagrams ?? [], features, T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1), null,
            Path.Combine(tempDir, fileName), "Warm-up", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, trackedLogs: logs.ToArray());
        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private static Scenario Scenario(string id, string name, double ms) =>
        new() { Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(ms) };

    private static void Call(List<RequestResponseLog> logs, string scenario, double startMs, double durationMs)
    {
        var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri("http://orders/orders");
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Request, trace, id, false)
            { Timestamp = At(startMs) });
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Response, trace, id, false, HttpStatusCode.OK)
            { Timestamp = At(startMs + durationMs) });
    }

    private static DateTimeOffset At(double ms) => T0.AddTicks((long)Math.Round(ms * TimeSpan.TicksPerMillisecond));

    /// <summary>
    /// One feature of the scenarios named, each with its wall time: one with a first call pays it from the run's start, and
    /// "Again" makes two later 5 ms calls of the same shape, so that call is the run's first-call warm-up.
    /// </summary>
    private string GenerateOwnTimes(string fileName, params (string Name, double WallMs, double FirstCallMs)[] scenarios)
    {
        var logs = new List<RequestResponseLog>();
        foreach (var scenario in scenarios.Where(s => s.FirstCallMs > 0))
            Call(logs, scenario.Name, 0, scenario.FirstCallMs);
        Call(logs, "Again", 3000, 5);
        Call(logs, "Again", 3100, 5);
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = scenarios.Select(s => new Scenario
                {
                    Id = s.Name, DisplayName = s.Name, Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromTicks((long)Math.Round(s.WallMs * TimeSpan.TicksPerMillisecond))
                }).ToArray()
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([], features, T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1), null,
            Path.Combine(TempDir, fileName), "Warm-up", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, trackedLogs: logs.ToArray());
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private ILocator ScenarioNamed(string name) =>
        Page.Locator("details.scenario").Filter(new() { Has = Page.Locator($":scope > summary .copy-scenario-name[data-scenario-name='{name}']") });

    private async Task<string[]> VisibleScenarios() =>
        await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('details.scenario'))
                .filter(s => s.style.display !== 'none')
                .map(s => s.querySelector(':scope > summary .copy-scenario-name').getAttribute('data-scenario-name'))
            """);

    [Fact]
    public async Task The_marker_and_its_tooltip_are_on_the_marked_scenario_alone()
    {
        await Page.GotoReportAsync(Generate("WarmUpMarker.html"));

        var notes = Page.Locator(".duration-badge > .warm-up-note");
        await Expect(notes).ToHaveCountAsync(1);
        var badge = ScenarioNamed("Place").Locator(":scope > summary .duration-badge");
        await Expect(badge).ToHaveTextAsync("2.0s · 1.5s warm-up");
        await Expect(badge).ToHaveAttributeAsync("title", "2 s, 1.5 s of it first-call warm-up: POST /orders 1.5 s (later calls 5 ms)");
        // 2.0 s of wall time is moderate; the 500 ms left is fast.
        await Expect(badge).ToHaveClassAsync("duration-badge duration-fast");
        // Painted muted: lighter than the badge's own text.
        var opacity = await badge.Locator(".warm-up-note").EvaluateAsync<string>("n => getComputedStyle(n).opacity");
        Assert.Equal("0.75", opacity);
        foreach (var name in new[] { "Place again", "Browse", "Search" })
            await Expect(ScenarioNamed(name).Locator(":scope > summary .duration-badge")).Not.ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex(".*"));
    }

    [Fact]
    public async Task The_timeline_shades_the_warm_ups_share_of_the_bar()
    {
        await Page.GotoReportAsync(Generate("WarmUpTimeline.html"));
        await OpenReportTabAsync("timeline"); // 5.0.0: a tab of the report bar (plans/OPTION_C_PLAN.md S3)
        await Expect(Page.Locator("#scenario-timeline")).ToBeVisibleAsync();

        var widths = await Page.EvaluateAsync<double[]>("""
            () => {
                var shade = document.querySelector('#scenario-timeline .timeline-warm-up');
                var bar = shade.parentElement;
                return [shade.getBoundingClientRect().width, bar.getBoundingClientRect().width,
                        document.querySelectorAll('#scenario-timeline .timeline-warm-up').length];
            }
            """);

        // 1,500 of the bar's 2,000 ms, within a pixel, on the one marked bar.
        Assert.Equal(1, widths[2]);
        Assert.InRange(widths[0], widths[1] * 0.75 - 1, widths[1] * 0.75 + 1);
    }

    [Fact]
    public async Task The_percentile_filter_ranks_by_each_scenarios_own_time()
    {
        await Page.GotoReportAsync(Generate("WarmUpFilter.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Own time: Browse 300, Search 400, Place 500, Place again 1,200 ms, so P90 is 1,200 ms, and the filter shows
        // what is at or above it. Place ran 2,000 ms of wall time, above P90, and is hidden: 500 ms of it was its own.
        await OpenFilterRowsAsync();
        var p90 = Page.Locator(".percentile-btn", new() { HasTextString = "P90" });
        await Expect(p90).ToHaveTextAsync("P90 (1.2s)");
        await p90.ClickAsync();

        Assert.Equal(["Place again"], await VisibleScenarios());
    }

    [Fact]
    public async Task A_percentile_button_filters_at_the_time_it_names()
    {
        // Rounded to a tenth of a second, a P50 of 30 ms read 0.0 s, which filters nothing, and a P90 of 1,259 ms read 1.3 s,
        // which hid the scenario it names (until 4.16.3). Place's own time is 30 ms here: 1,600 less 1,570 of warm-up.
        await Page.GotoReportAsync(GenerateOwnTimes("WarmUpPercentileUnder.html",
            ("Place", 1600, 1570), ("Again", 40, 0), ("Browse", 10, 0), ("Search", 20, 0), ("Pay", 2000, 0)));
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenFilterRowsAsync();
        var p50 = Page.Locator(".percentile-btn", new() { HasTextString = "P50" });
        await Expect(p50).ToHaveTextAsync("P50 (30ms)");
        await p50.ClickAsync();

        // The page lists scenarios in its own order; what the button shows is the question, not where.
        Assert.Equal(["Again", "Pay", "Place"], (await VisibleScenarios()).Order(StringComparer.Ordinal));

        // 1,910.4 ms with 650.6 ms of warm-up: the filter reads 1,910 less 651, 1,259 ms of its own, and that is the P90.
        await Page.GotoReportAsync(GenerateOwnTimes("WarmUpPercentileOver.html",
            ("Place", 1910.4, 650.6), ("Again", 100, 0), ("Browse", 200, 0), ("Search", 300, 0), ("Pay", 400, 0)));
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenFilterRowsAsync();
        var p90 = Page.Locator(".percentile-btn", new() { HasTextString = "P90" });
        await Expect(p90).ToHaveTextAsync("P90 (1.3s)");
        await p90.ClickAsync();

        Assert.Equal(["Place"], await VisibleScenarios());
    }

    [Fact]
    public async Task A_saved_diagram_is_named_for_its_scenario_without_the_warm_up()
    {
        // The menu names a saved diagram after its scenario's title with the duration badge taken out; the warm-up note sits
        // inside the badge and goes with it (plans/WARM_UP_PLAN.md 4.5, which 4.9.0 read and did not run).
        await Page.GotoReportAsync(Generate("WarmUpDiagramName.html",
            diagrams: [new("place", "", "@startuml\nCaller -> Orders : POST /orders\nOrders --> Caller : 201 Created\n@enduml")]));
        await Expect(ScenarioNamed("Place").Locator(":scope > summary .duration-badge > .warm-up-note")).ToHaveTextAsync(" · 1.5s warm-up");
        await Page.Locator("details.feature > summary").First.ClickAsync();
        await ScenarioNamed("Place").Locator(":scope > summary").ClickAsync();
        // the diagram's own picture: since 5.0.0 the scenario's settings gear is an svg too, ahead of it (S8)
        var svg = ScenarioNamed("Place").Locator("[data-diagram-type='plantuml'] svg").First;
        await svg.WaitForAsync(new() { Timeout = 60_000 });

        await DispatchContextMenu(svg);
        var saveImage = Page.Locator(".diagram-ctx-menu .submenu-parent", new() { HasTextString = "Save image" });
        await saveImage.HoverAsync();
        var item = saveImage.Locator(".submenu").GetByText("Save as SVG", new() { Exact = true });
        var download = await Page.RunAndWaitForDownloadAsync(() => item.ClickAsync(), new() { Timeout = 10_000 });

        Assert.Equal("place.svg", download.SuggestedFilename);
    }

    [Fact]
    public async Task The_specifications_page_draws_the_same_mark_and_the_exports_keep_what_they_kept()
    {
        await Page.GotoReportAsync(Generate("WarmUpSpecifications.html", includeTestRunData: false));
        await Expect(ScenarioNamed("Place").Locator(":scope > summary .duration-badge > .warm-up-note")).ToHaveTextAsync(" · 1.5s warm-up");

        await Page.GotoReportAsync(Generate("WarmUpExports.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Export Filtered CSV writes wall time.
        var csv = await Page.RunAndWaitForDownloadAsync(async () =>
            await PressBoxExportAsync(Page, "Export Filtered CSV"));
        var csvPath = Path.Combine(TempDir, $"warm-up_{Guid.NewGuid():N}.csv");
        await csv.SaveAsAsync(csvPath);
        var row = Assert.Single((await File.ReadAllTextAsync(csvPath)).Split('\n'),
            line => line.StartsWith("\"Orders\",\"Place\",", StringComparison.Ordinal));
        Assert.EndsWith(",\"2000\"", row);

        // Export Filtered HTML keeps the marker, its tooltip and the rule that mutes it.
        var html = await Page.RunAndWaitForDownloadAsync(async () =>
            await PressBoxExportAsync(Page, "Export Filtered HTML"));
        var htmlPath = Path.Combine(TempDir, $"warm-up_{Guid.NewGuid():N}.html");
        await html.SaveAsAsync(htmlPath);
        await Page.GotoReportAsync(new Uri(htmlPath).AbsoluteUri);
        var badge = ScenarioNamed("Place").Locator(":scope > summary .duration-badge");
        await Expect(badge).ToHaveAttributeAsync("title", "2 s, 1.5 s of it first-call warm-up: POST /orders 1.5 s (later calls 5 ms)");
        Assert.Equal("0.75", await badge.Locator(".warm-up-note").EvaluateAsync<string>("n => getComputedStyle(n).opacity"));
    }
}
