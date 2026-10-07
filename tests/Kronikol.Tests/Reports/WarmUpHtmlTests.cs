using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What the report draws of the first-call warm-up (plans/WARM_UP_PLAN.md 4.5, R2): a marked scenario's duration badge
/// keeps its wall time and adds the warm-up as muted text (Q5, A) with a tooltip naming the calls, its colour follows the
/// time left, the Scenario Timeline shades the warm-up's share of a bar, the P50 to P99 filter ranks without it, and a
/// page with no mark is written as it was before the pass.
/// </summary>
public class WarmUpHtmlTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-warm-up-html").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_marked_scenario_keeps_its_wall_time_and_adds_its_warm_up_as_muted_text()
    {
        var page = Page(Marked());

        var scenario = Scenario(page, "s1");
        Assert.Equal("2000", scenario.GetAttribute("data-duration-ms"));
        Assert.Equal("600", scenario.GetAttribute("data-warmup-ms"));
        var badge = Badge(scenario);
        Assert.Equal("2.0s", badge.ChildNodes.OfType<IText>().Single().Data);
        Assert.Equal(" · 600ms warm-up", badge.QuerySelector(":scope > .warm-up-note")!.TextContent);
        Assert.Equal("590", Scenario(page, "s2").GetAttribute("data-warmup-ms"));
    }

    [Fact]
    public void The_badge_tooltip_names_each_call_and_its_later_calls_median()
    {
        var page = Page(Marked());

        Assert.Equal("2 s, 600 ms of it first-call warm-up: POST /orders 600 ms (later calls 5 ms)",
            Badge(Scenario(page, "s1")).GetAttribute("title"));
        Assert.Equal("2 s, 590 ms of it first-call warm-up: POST /orders 590 ms, waiting for the run's first (later calls 5 ms)",
            Badge(Scenario(page, "s2")).GetAttribute("title"));
    }

    [Theory]
    // The badge cuts a time under a second to its whole milliseconds, as it always has; the tooltip rounded it, so the
    // badge read 696ms and its tooltip 697 ms (4.9.0, measured on BreakfastProvider's live report).
    [InlineData(696.6, 612.7, "696ms", " · 612ms warm-up", "696 ms, 612 ms of it first-call warm-up: POST /orders 612 ms (later calls 5 ms)")]
    // Just under a second the tooltip wrote 1000 ms beside a badge reading 999ms.
    [InlineData(1999.7, 999.7, "2.0s", " · 999ms warm-up", "2 s, 999 ms of it first-call warm-up: POST /orders 999 ms (later calls 5 ms)")]
    public void The_tooltip_reads_each_time_as_the_badge_writes_it(double durationMs, double firstMs, string text, string note, string tooltip)
    {
        var (features, logs) = Marked(firstMs);
        features[0].Scenarios[0].Duration = TimeSpan.FromTicks((long)Math.Round(durationMs * TimeSpan.TicksPerMillisecond));

        var page = Page((features, logs));

        var badge = Badge(Scenario(page, "s1"));
        Assert.Equal(text, badge.ChildNodes.OfType<IText>().Single().Data);
        Assert.Equal(note, badge.QuerySelector(":scope > .warm-up-note")!.TextContent);
        Assert.Equal(tooltip, badge.GetAttribute("title"));
        var row = page.QuerySelectorAll("#scenario-timeline .timeline-row").Single(r => r.QuerySelector(".timeline-label")!.TextContent == "Place");
        Assert.Equal(text, row.QuerySelector(".timeline-duration")!.TextContent);
        Assert.Equal(tooltip, row.QuerySelector(".timeline-bar")!.GetAttribute("title"));
    }

    [Fact]
    public void The_badge_colour_follows_the_time_left()
    {
        var page = Page(Marked());

        // 2.0 s of wall time is moderate; 1.4 s left once the warm-up is out is fast.
        Assert.Contains("duration-fast", Badge(Scenario(page, "s1")).ClassList);
        Assert.Contains("duration-moderate", Badge(Scenario(page, "s3")).ClassList);
    }

    [Fact]
    public void A_scenario_with_no_warm_up_is_drawn_as_before()
    {
        var page = Page(Marked());

        var scenario = Scenario(page, "s3");
        Assert.False(scenario.HasAttribute("data-warmup-ms"));
        Assert.Equal("<span class=\"duration-badge duration-moderate\">2.0s</span>", Badge(scenario).OuterHtml);
    }

    [Fact]
    public void Only_a_page_that_draws_a_mark_carries_the_warm_up_rules()
    {
        var marked = Html(Marked());
        var unmarked = Html(Marked(firstMs: 5));

        Assert.Contains(Stylesheets.WarmUpStyleSheet, Styles(marked));
        Assert.DoesNotContain(".warm-up-note", Styles(unmarked));
        Assert.DoesNotContain(".timeline-warm-up", Styles(unmarked));
        Assert.Empty(Parse(unmarked).QuerySelectorAll("[data-warmup-ms], .warm-up-note, .timeline-warm-up"));
    }

    [Fact]
    public void A_page_with_no_mark_is_the_page_written_without_the_pass()
    {
        // T44: the calls are read and nothing is marked. Written once with the pass and once with its result left
        // out, the page is the same bytes.
        var (features, logs) = Marked(firstMs: 5);

        var withPass = Html((features, logs), warmUp: null);
        var withoutPass = Html((features, logs), warmUp: WarmUpResult.None);

        Assert.Equal(withoutPass, withPass);
    }

    [Fact]
    public void The_percentiles_are_over_each_scenarios_time_left()
    {
        var filters = Parse(Html(Marked())).QuerySelector(".duration-filters")!;

        // Time left: 1,400, 1,410 and 2,000 ms; by wall time all three are 2,000.
        Assert.Equal("1410", filters.GetAttribute("data-p50"));
        Assert.Equal("2000", filters.GetAttribute("data-p90"));
        Assert.Equal("Each scenario's own time: the first-call warm-up it paid is left out",
            filters.QuerySelector(".duration-filters-label")!.GetAttribute("title"));
    }

    [Fact]
    public void The_filter_label_has_no_title_when_nothing_is_marked()
    {
        var filters = Parse(Html(Marked(firstMs: 5))).QuerySelector(".duration-filters")!;

        Assert.False(filters.QuerySelector(".duration-filters-label")!.HasAttribute("title"));
    }

    [Theory]
    // The P50 of two is the smaller: sorted[(int)(n * p)] took the larger.
    [InlineData(new double[] { 100, 200 }, "data-p50", "100")]
    // The P90 of ten is the ninth: sorted[(int)(n * p)] took the tenth, the largest.
    [InlineData(new double[] { 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000 }, "data-p90", "9000")]
    [InlineData(new double[] { 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000 }, "data-p50", "5000")]
    [InlineData(new double[] { 700 }, "data-p99", "700")]
    public void A_percentile_is_the_nearest_rank(double[] durationsMs, string attribute, string expected)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Timings",
                Scenarios = durationsMs.Select((ms, i) => new Scenario
                    { Id = $"p{i}", DisplayName = $"Scenario {i}", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(ms) }).ToArray()
            }
        ];

        var filters = Parse(Html((features, []))).QuerySelector(".duration-filters")!;

        Assert.Equal(expected, filters.GetAttribute(attribute));
    }

    [Fact]
    public void The_timeline_shades_the_warm_up_share_of_a_marked_bar()
    {
        var page = Page(Marked());

        var bars = page.QuerySelectorAll("#scenario-timeline .timeline-row")
            .ToDictionary(row => row.QuerySelector(".timeline-label")!.TextContent, row => row.QuerySelector(".timeline-bar")!);
        var shade = bars["Place"].QuerySelector(":scope > .timeline-warm-up")!;
        Assert.Equal("width:30.0%", shade.GetAttribute("style"));
        Assert.Equal(Badge(Scenario(page, "s1")).GetAttribute("title"), bars["Place"].GetAttribute("title"));
        Assert.Equal("width:29.5%", bars["Place again"].QuerySelector(":scope > .timeline-warm-up")!.GetAttribute("style"));
        Assert.Empty(bars["Place twice"].Children);
        Assert.Equal("2.0s", bars["Place twice"].GetAttribute("title"));
    }

    [Fact]
    public void The_timeline_names_the_colours_it_paints()
    {
        var info = Page(Marked()).QuerySelector("#scenario-timeline .timeline-info")!.GetAttribute("title")!;

        // Skipped bars are painted grey and bypassed ones orange (stylesheets.css .timeline-bar-skipped, -bypassed).
        Assert.Contains("green = passed, red = failed, grey = skipped, orange = bypassed.", info);
        Assert.DoesNotContain("yellow", info);
    }

    [Fact]
    public void A_parameterized_group_sums_its_rows_warm_up_as_it_sums_their_durations()
    {
        var (features, logs) = Marked();
        foreach (var (scenario, item) in features[0].Scenarios.Zip(new[] { "Widget", "Gadget", "Gizmo" }))
        {
            scenario.OutlineId = "Place an order";
            scenario.ExampleValues = new Dictionary<string, string> { ["item"] = item };
        }

        var group = Parse(Html((features, logs))).QuerySelector("details.scenario-parameterized")!;

        Assert.Equal("6000", group.GetAttribute("data-duration-ms"));
        Assert.Equal("1190", group.GetAttribute("data-warmup-ms"));
        var badge = group.QuerySelector(":scope > summary .duration-badge")!;
        // 6.0 s is slow; the 4.8 s left is moderate.
        Assert.Contains("duration-moderate", badge.ClassList);
        Assert.Equal(" · 1.2s warm-up", badge.QuerySelector(":scope > .warm-up-note")!.TextContent);
        Assert.StartsWith("6 s, 1.19 s of it first-call warm-up: POST /orders 600 ms (later calls 5 ms), POST /orders 590 ms, waiting",
            badge.GetAttribute("title"));
    }

    /// <summary>The fixture R1's data facts use: a 600 ms first call in s1, a call in s2 that waited for it, and two
    /// 5 ms calls in s3 after both ended. With <paramref name="firstMs"/> small nothing is marked.</summary>
    internal static (Feature[] Features, List<RequestResponseLog> Logs) Marked(double firstMs = 600)
    {
        var logs = new List<RequestResponseLog>();
        Call(logs, "s1", 0, firstMs);
        Call(logs, "s2", 10, firstMs > 20 ? firstMs - 10 : firstMs);
        Call(logs, "s3", 1000, 5);
        Call(logs, "s3", 1100, 5);
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                    new Scenario { Id = "s2", DisplayName = "Place again", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                    new Scenario { Id = "s3", DisplayName = "Place twice", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) }
                ]
            }
        ];
        return (features, logs);
    }

    internal static Guid Call(List<RequestResponseLog> logs, string scenario, double startMs, double durationMs)
    {
        var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri("http://orders/orders");
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Request, trace, id, false)
            { Timestamp = T0.AddTicks((long)Math.Round(startMs * TimeSpan.TicksPerMillisecond)) });
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Response, trace, id, false, HttpStatusCode.OK)
            { Timestamp = T0.AddTicks((long)Math.Round((startMs + durationMs) * TimeSpan.TicksPerMillisecond)) });
        return id;
    }

    private string Html((Feature[] Features, List<RequestResponseLog> Logs) run, WarmUpResult? warmUp = null)
    {
        var path = ReportGenerator.GenerateHtmlReportCore([], run.Features, T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1), null,
            Path.Combine(_directory, $"WarmUp_{Guid.NewGuid():N}.html"), "Warm-up", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            trackedLogs: run.Logs.ToArray(), warmUp: warmUp);
        return File.ReadAllText(path);
    }

    private IDocument Page((Feature[] Features, List<RequestResponseLog> Logs) run) => Parse(Html(run));

    private static IDocument Parse(string html) =>
        BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();

    private static string Styles(string html) =>
        string.Concat(Parse(html).QuerySelectorAll("style").Select(s => s.TextContent));

    private static IElement Scenario(IDocument page, string id) =>
        page.QuerySelectorAll("details.scenario").Single(s => s.GetAttribute("data-stable-id") is not null
            && s.QuerySelector(":scope > summary .copy-scenario-name") is { } copy
            && copy.GetAttribute("data-scenario-name") == Name(id));

    private static string Name(string id) => id switch { "s1" => "Place", "s2" => "Place again", _ => "Place twice" };

    private static IElement Badge(IElement scenario) => scenario.QuerySelector(":scope > summary .duration-badge")!;
}
