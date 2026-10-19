using System.Globalization;
using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The Timeline tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D27): Show Filtered or Full, and where a row paid a
/// first-call warm-up, Warm-up Shown or Hidden and Order With or Without warm-up, in the timeline's header line, with a
/// note while Filtered draws fewer rows than the timeline has. Filtered is the start. The page joins a row to its scenario
/// by stable id and reads its times from whole milliseconds. The preview read a row's seconds back from its bar's title
/// and matched rows to scenarios by name: a row of a minute or more ("1m 5s") kept its width, fell out of the scale and
/// sorted last, and two scenarios of one name in two features showed and hid together. Neither is ported.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class TimelineTabTests : PlaywrightTestBase
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    public TimelineTabTests(PlaywrightFixture fixture) : base(fixture) { }

    private string Write(string fileName, Feature[] features, List<RequestResponseLog>? logs = null)
    {
        var path = ReportGenerator.GenerateHtmlReport([], features, T0.UtcDateTime, T0.UtcDateTime.AddMinutes(2), null,
            Path.Combine(TempDir, fileName), "Timeline", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, trackedLogs: logs?.ToArray());
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private static Scenario Timed(string id, string name, double ms, ExecutionResult result = ExecutionResult.Passed) =>
        new() { Id = id, DisplayName = name, Result = result, Duration = TimeSpan.FromMilliseconds(ms) };

    /// <summary>A call the scenario made down POST /orders, as WarmUpMarkTests writes them.</summary>
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

    private async Task OpenAsync(string url)
    {
        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    private async Task OpenTimelineAsync(string url)
    {
        await OpenAsync(url);
        await OpenReportTabAsync("timeline");
    }

    /// <summary>Shows only the scenarios of <paramref name="status"/>, through the filter box's Status row.</summary>
    private async Task ShowOnlyAsync(string status)
    {
        await OpenFilterRowsAsync();
        await Page.Locator($".status-toggle[data-status='{status}']").ClickAsync();
    }

    private ILocator Segment(string key, string value) => Page.Locator($"#scenario-timeline .tl-seg-btn[data-{key}='{value}']");

    private ILocator Note => Page.Locator("#scenario-timeline > .filtered-note");

    /// <summary>The rows the timeline shows, top to bottom: each one's name, the time it reads, and its bar's painted share
    /// of its track, to three places.</summary>
    private Task<string[][]> ShownRowsAsync() => Page.EvaluateAsync<string[][]>("""
        () => Array.from(document.querySelectorAll('#scenario-timeline > .timeline-row'))
            .filter(r => getComputedStyle(r).display !== 'none')
            .map(r => {
                const bar = r.querySelector('.timeline-bar');
                return [r.querySelector('.timeline-label').textContent, r.querySelector('.timeline-duration').textContent,
                        (bar.getBoundingClientRect().width / bar.parentElement.getBoundingClientRect().width).toFixed(3)];
            })
        """);

    private async Task<string[]> ShownNamesAsync() => (await ShownRowsAsync()).Select(r => r[0]).ToArray();

    private static void Share(string[] row, double expected) =>
        Assert.InRange(double.Parse(row[2], CultureInfo.InvariantCulture), expected - 0.01, expected + 0.01);

    /// <summary>Each [what, selector, pseudo-element or "", property, token]: what the element paints and what the token
    /// gives in the page's theme, each read as a colour.</summary>
    private Task<string[][]> PaintedAsync(string[][] checks) => Page.EvaluateAsync<string[][]>("""
        checks => checks.map(([what, selector, pseudo, property, token]) => {
            const el = document.querySelector(selector);
            const probe = document.createElement('div');
            probe.style.setProperty('color', `var(${token})`);
            document.body.appendChild(probe);
            const wanted = getComputedStyle(probe).color;
            probe.remove();
            return [what, el ? getComputedStyle(el, pseudo || null)[property] : 'missing', wanted];
        })
        """, checks);

    private static void AssertPainted(string theme, string[][] painted)
    {
        foreach (var p in painted)
            Assert.True(p[1] == p[2], $"{p[0]} is painted {p[1]}, where the {theme} theme's token gives {p[2]}");
    }

    // The default report (ReportTestHelper.CreateTestData): five scenarios with times, slowest first "Delete order fails
    // gracefully" (5 s, the one that failed), "Create order successfully" (2 s), "List orders returns paginated results"
    // (1 s), "Process payment" (500 ms) and "Refund payment" (100 ms, skipped). Nothing in it paid a warm-up.

    [Fact]
    public async Task Filtered_is_the_start_and_the_timeline_follows_the_filters()
    {
        await OpenAsync(GenerateReport("TimelineFiltered.html"));
        await ShowOnlyAsync("Failed");
        await OpenReportTabAsync("timeline");

        Assert.Equal(["Delete order fails gracefully"], await ShownNamesAsync());
        await Expect(Note).ToBeVisibleAsync();
        await Expect(Note).ToHaveTextAsync("Filtered: this timeline shows the 1 of 5 scenarios that match the filters.");
        await Expect(Segment("view", "filtered")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Segment("view", "full")).ToHaveAttributeAsync("aria-pressed", "false");

        // a change made while the timeline shows: Clear All puts every row back, and the note goes
        await Page.Locator(".filtering-box-export .export-btn", new() { HasTextString = "Clear All" }).ClickAsync();
        Assert.Equal(5, (await ShownRowsAsync()).Length);
        await Expect(Note).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Full_draws_every_row_whatever_the_filters()
    {
        await OpenAsync(GenerateReport("TimelineFull.html"));
        await ShowOnlyAsync("Failed");
        await OpenReportTabAsync("timeline");

        await Segment("view", "full").ClickAsync();

        var rows = await ShownRowsAsync();
        Assert.Equal(["Delete order fails gracefully", "Create order successfully", "List orders returns paginated results", "Process payment", "Refund payment"],
            rows.Select(r => r[0]));
        // on the whole run's scale again: the slowest, 5 s, fills its track
        Share(rows[0], 1.0);
        Share(rows[1], 0.4);
        await Expect(Note).ToBeHiddenAsync();
        await Expect(Segment("view", "full")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Segment("view", "filtered")).ToHaveAttributeAsync("aria-pressed", "false");

        await Segment("view", "filtered").ClickAsync();
        var filtered = Assert.Single(await ShownRowsAsync());
        Assert.Equal("Delete order fails gracefully", filtered[0]);
        Share(filtered, 1.0);
        await Expect(Note).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_filter_in_the_pages_link_reaches_the_timeline_as_it_opens()
    {
        // the link's filters are applied as the page loads, before the timeline's script starts
        await OpenAsync(GenerateReport("TimelineLink.html") + "#status=Failed");
        await OpenReportTabAsync("timeline");

        Assert.Equal(["Delete order fails gracefully"], await ShownNamesAsync());
        await Expect(Note).ToHaveTextAsync("Filtered: this timeline shows the 1 of 5 scenarios that match the filters.");
    }

    [Fact]
    public async Task With_the_warm_up_hidden_each_bar_and_time_is_the_scenarios_own()
    {
        // Place runs 2,000 ms, 1,500 of it the run's first call; Place again 1,200, Search 400 and Browse 300 (WarmUpMarkTests)
        await OpenTimelineAsync(WarmUpMarkTests.WriteOrders(TempDir, OutputDir, "TimelineWarmUpHidden.html"));
        var shade = Page.Locator("#scenario-timeline .timeline-warm-up");
        await Expect(shade).ToBeVisibleAsync();

        await Segment("warm", "hidden").ClickAsync();

        // still slowest first by the whole time (Order: With warm-up), drawn to the longest own time, Place again's
        var rows = await ShownRowsAsync();
        Assert.Equal(["Place", "Place again", "Search", "Browse"], rows.Select(r => r[0]));
        Assert.Equal(["500ms", "1.2s", "400ms", "300ms"], rows.Select(r => r[1]));
        Share(rows[0], 500.0 / 1200);
        Share(rows[1], 1.0);
        Share(rows[2], 400.0 / 1200);
        Share(rows[3], 300.0 / 1200);
        await Expect(shade).ToBeHiddenAsync();
        await Expect(Segment("warm", "hidden")).ToHaveAttributeAsync("aria-pressed", "true");

        await Segment("warm", "shown").ClickAsync();

        rows = await ShownRowsAsync();
        Assert.Equal(["2.0s", "1.2s", "400ms", "300ms"], rows.Select(r => r[1]));
        Share(rows[0], 1.0);
        Share(rows[1], 0.6);
        await Expect(shade).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Order_without_warm_up_puts_the_slowest_by_its_own_time_first()
    {
        await OpenTimelineAsync(WarmUpMarkTests.WriteOrders(TempDir, OutputDir, "TimelineOrder.html"));
        Assert.Equal(["Place", "Place again", "Search", "Browse"], await ShownNamesAsync());

        await Segment("order", "without").ClickAsync();

        // Place took 500 ms of its own, under Place again's 1,200; the bars still count the warm-up (Warm-up: Shown)
        var rows = await ShownRowsAsync();
        Assert.Equal(["Place again", "Place", "Search", "Browse"], rows.Select(r => r[0]));
        Share(rows[0], 0.6);
        Share(rows[1], 1.0);
        await Expect(Segment("order", "without")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Segment("order", "with")).ToHaveAttributeAsync("aria-pressed", "false");

        await Segment("order", "with").ClickAsync();

        Assert.Equal(["Place", "Place again", "Search", "Browse"], await ShownNamesAsync());
    }

    [Fact]
    public async Task A_row_of_a_minute_or_more_is_scaled_sorted_and_read_like_any_other()
    {
        // Long runs 70 s, 5 s of it the run's first call down POST /orders; Half runs 30 s; Again runs 300 ms and makes
        // two 5 ms calls down the same path after the first. With the warm-up left out Long is a 65 s row.
        var logs = new List<RequestResponseLog>();
        Call(logs, "long", 0, 5000);
        Call(logs, "again", 80_000, 5);
        Call(logs, "again", 80_100, 5);
        Feature[] features =
        [
            new() { DisplayName = "Orders", Scenarios = [Timed("long", "Long", 70_000), Timed("half", "Half", 30_000), Timed("again", "Again", 300)] }
        ];
        await OpenTimelineAsync(Write("TimelineMinute.html", features, logs));
        Assert.Equal(["Long", "Half", "Again"], await ShownNamesAsync());

        await Segment("warm", "hidden").ClickAsync();

        var rows = await ShownRowsAsync();
        Assert.Equal(["Long", "Half", "Again"], rows.Select(r => r[0]));
        Assert.Equal(["1m 5s", "30.0s", "300ms"], rows.Select(r => r[1]));
        Share(rows[0], 1.0);
        Share(rows[1], 30.0 / 65);

        await Segment("order", "without").ClickAsync();

        Assert.Equal(["Long", "Half", "Again"], await ShownNamesAsync());
    }

    [Fact]
    public async Task Two_scenarios_of_one_name_in_two_features_show_and_hide_apart()
    {
        Feature[] features =
        [
            new() { DisplayName = "Checkout", Scenarios = [Timed("c1", "Pay", 2000), Timed("c2", "Ship", 3000)] },
            new() { DisplayName = "Refunds", Scenarios = [Timed("r1", "Pay", 1000, ExecutionResult.Failed)] }
        ];
        await OpenAsync(Write("TimelineSameName.html", features));
        await ShowOnlyAsync("Failed");
        await OpenReportTabAsync("timeline");

        // the refund's Pay alone, drawn to its own length; matched by name, Checkout's Pay showed with it
        var row = Assert.Single(await ShownRowsAsync());
        Assert.Equal("Pay", row[0]);
        Assert.Equal("1.0s", row[1]);
        Share(row, 1.0);
        await Expect(Page.Locator("#scenario-timeline > .timeline-row.timeline-row-failed")).ToBeVisibleAsync();
        await Expect(Note).ToBeVisibleAsync();
        await Expect(Note).ToHaveTextAsync("Filtered: this timeline shows the 1 of 3 scenarios that match the filters.");
    }

    [Fact]
    public async Task An_outlines_example_rows_show_and_hide_with_their_scenario()
    {
        static Scenario Example(string id, string tier, double ms) => new()
        {
            Id = id, DisplayName = "Discount applies", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(ms),
            OutlineId = "discount", ExampleValues = new Dictionary<string, string> { ["tier"] = tier }
        };
        Feature[] features =
        [
            new() { DisplayName = "Pricing", Scenarios = [Example("p1", "gold", 2000), Example("p2", "silver", 1000), Timed("p3", "Checkout", 3000, ExecutionResult.Failed)] }
        ];
        await OpenAsync(Write("TimelineOutline.html", features));
        // the filters show and hide the outline as one scenario; the timeline has a row for each of its examples
        await Expect(Page.Locator("details.scenario-parameterized")).ToHaveCountAsync(1);
        await ShowOnlyAsync("Passed");
        await OpenReportTabAsync("timeline");

        Assert.Equal(["Discount applies", "Discount applies"], await ShownNamesAsync());
        await Expect(Note).ToBeVisibleAsync();
        await Expect(Note).ToHaveTextAsync("Filtered: this timeline shows the 2 of 3 scenarios that match the filters.");

        await Page.Locator(".status-toggle[data-status='Passed']").ClickAsync();
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();

        Assert.Equal(["Checkout"], await ShownNamesAsync());
    }

    [Fact]
    public async Task Without_a_warm_up_the_timeline_has_only_its_show_control()
    {
        await OpenTimelineAsync(GenerateReport("TimelineNoWarmUp.html"));

        var groups = Page.Locator("#scenario-timeline .timeline-controls > .tl-seg");
        await Expect(groups).ToHaveCountAsync(1);
        await Expect(groups.First).ToHaveAttributeAsync("aria-label", "Show");
        await Expect(groups.First.Locator(".tl-seg-btn")).ToHaveTextAsync(new[] { "Filtered", "Full" });
        await Expect(Page.Locator("#scenario-timeline .tl-seg-btn[data-warm], #scenario-timeline .tl-seg-btn[data-order]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_page_writes_a_time_as_the_report_does()
    {
        // The timeline writes a scenario's own time in the page, with the warm-up hidden; every other time on the page is
        // the generator's (FormatDurationBadge). No time here falls on an exact tie of a tenth.
        await OpenAsync(GenerateReport("TimelineTimes.html"));
        double[] times = [0, 1, 999, 1000, 1049, 1050, 1951, 9999, 59_949, 59_950, 59_999, 60_000, 61_999, 65_000, 599_999, 3_599_999, 3_600_000, 7_384_500];

        var written = await Page.EvaluateAsync<string[]?>(
            "times => typeof window._kronFormatDuration === 'function' ? times.map(t => window._kronFormatDuration(t)) : null", times);

        Assert.NotNull(written);
        Assert.Equal(times.Select(t => ReportGenerator.FormatDurationBadge(TimeSpan.FromMilliseconds(t))), written);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_controls_the_bars_and_the_note_take_the_themes_colours(string theme)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Statuses",
                Scenarios =
                [
                    Timed("p", "Passes", 4000), Timed("f", "Fails", 3000, ExecutionResult.Failed),
                    Timed("s", "Skips", 2000, ExecutionResult.Skipped), Timed("b", "Bypasses", 1000, ExecutionResult.Bypassed)
                ]
            }
        ];
        await Context.AddInitScriptAsync($"try {{ localStorage.setItem('kronikol.theme', '{theme}'); }} catch (e) {{ }}");
        await OpenTimelineAsync(Write($"TimelineColours_{theme}.html", features));

        AssertPainted(theme, await PaintedAsync(
        [
            ["a passed bar", ".timeline-bar-passed", "", "backgroundColor", "--kron-pass-ring"],
            ["a failed bar", ".timeline-bar-failed", "", "backgroundColor", "--kron-fail"],
            ["a skipped bar", ".timeline-bar-skipped", "", "backgroundColor", "--kron-dot"],
            ["a bypassed bar", ".timeline-bar-bypassed", "", "backgroundColor", "--kron-bypass"],
            ["the track", ".timeline-track", "", "backgroundColor", "--kron-track"],
            ["the card", "#scenario-timeline", "", "backgroundColor", "--kron-card"],
            ["the card's edge", "#scenario-timeline", "", "borderTopColor", "--kron-hairline"],
            ["the header", "#scenario-timeline > .timeline-header", "", "color", "--kron-label"],
            ["a group's label", "#scenario-timeline .tl-seg-label", "", "color", "--kron-label"],
            ["the pressed segment's ground", "#scenario-timeline .tl-seg-btn[aria-pressed='true']", "", "backgroundColor", "--kron-selected"],
            ["the pressed segment's text", "#scenario-timeline .tl-seg-btn[aria-pressed='true']", "", "color", "--kron-selected-ink"],
            ["the pressed segment's edge", "#scenario-timeline .tl-seg-btn[aria-pressed='true']", "", "borderTopColor", "--kron-selected-edge"],
            ["a segment's ground", "#scenario-timeline .tl-seg-btn[aria-pressed='false']", "", "backgroundColor", "--kron-card"],
            ["a segment's text", "#scenario-timeline .tl-seg-btn[aria-pressed='false']", "", "color", "--kron-ink-soft"],
            ["a segment's edge", "#scenario-timeline .tl-seg-btn[aria-pressed='false']", "", "borderTopColor", "--kron-control-border"],
            ["a row's name", "#scenario-timeline .timeline-label", "", "color", "--kron-ink-soft"],
            ["a row's time", "#scenario-timeline .timeline-duration", "", "color", "--kron-faint"],
            ["the failed row's cross", "#scenario-timeline .timeline-row-failed .timeline-label", "::before", "color", "--kron-fail"]
        ]));
        // a failed row is told by more than its colour: its name opens with the cross a failed scenario's heading shows
        Assert.Equal("\"✗\"", await Page.EvaluateAsync<string?>(
            "() => { const l = document.querySelector('#scenario-timeline .timeline-row-failed .timeline-label'); return l ? getComputedStyle(l, '::before').content : null; }"));

        await ShowOnlyAsync("Failed");

        await Expect(Note).ToBeVisibleAsync();
        AssertPainted(theme, await PaintedAsync(
        [
            ["the note's ground", "#scenario-timeline > .filtered-note", "", "backgroundColor", "--kron-warn-soft"],
            ["the note's text", "#scenario-timeline > .filtered-note", "", "color", "--kron-warn"],
            ["the note's edge", "#scenario-timeline > .filtered-note", "", "borderTopColor", "--kron-warn-edge"]
        ]));
    }
}
