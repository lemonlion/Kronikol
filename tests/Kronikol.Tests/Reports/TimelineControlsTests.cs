using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The Timeline tab's controls as the generator writes them (5.0.0, plans/OPTION_C_PLAN.md S11, D27): in the timeline's
/// header line, Show (Filtered pressed, or Full), and where a row paid a first-call warm-up, Warm-up (Shown or Hidden) and
/// Order (With or Without warm-up), each button titled with what it shows; under the header, the note Filtered fills,
/// empty and hidden; on each row the scenario it draws, by stable id (data-sid), and its time and warm-up in whole
/// milliseconds as the scenario's element writes them, so report-timeline-script.js reads nothing back from a title or a
/// name; a failed scenario's row marked by class; the script only in a page with a timeline; and the timeline's rules on
/// the theme's tokens.
/// </summary>
public class TimelineControlsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-timeline-controls").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Html(Feature[] features, List<RequestResponseLog>? logs = null)
    {
        var path = ReportGenerator.GenerateHtmlReportCore([], features, T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1), null,
            Path.Combine(_directory, $"Timeline_{Guid.NewGuid():N}.html"), "Timeline", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, trackedLogs: logs?.ToArray());
        return File.ReadAllText(path);
    }

    private IDocument Page(Feature[] features, List<RequestResponseLog>? logs = null) => Parse(Html(features, logs));

    private static IDocument Parse(string html) =>
        BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();

    private static Scenario Timed(string id, string name, double ms, ExecutionResult result = ExecutionResult.Passed) =>
        new() { Id = id, DisplayName = name, Result = result, Duration = TimeSpan.FromMilliseconds(ms) };

    /// <summary>One feature with a scenario of each result, none of which paid a warm-up.</summary>
    private static Feature[] EveryResult() =>
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

    /// <summary>A group's buttons as "value pressed text: title (type)", the value read from <paramref name="key"/>.</summary>
    private static string[] Buttons(IElement group, string key) =>
        group.QuerySelectorAll(":scope > button.tl-seg-btn")
            .Select(b => $"{b.GetAttribute(key)} {b.GetAttribute("aria-pressed")} {b.TextContent}: {b.GetAttribute("title")} ({b.GetAttribute("type")})")
            .ToArray();

    [Fact]
    public void The_header_line_holds_the_show_control_and_the_note_follows_it()
    {
        var timeline = Page(EveryResult()).QuerySelector("#scenario-timeline")!;
        var header = timeline.QuerySelector(":scope > .timeline-header")!;

        var show = Assert.Single(header.QuerySelectorAll(":scope > .timeline-controls > .tl-seg"));
        Assert.Equal("span", show.LocalName);
        Assert.Equal("group", show.GetAttribute("role"));
        Assert.Equal("Show", show.GetAttribute("aria-label"));
        Assert.Equal("Show", show.QuerySelector(":scope > .tl-seg-label")!.TextContent);
        Assert.Equal(
            ["filtered true Filtered: Only the scenarios the filters show (button)",
             "full false Full: Every scenario in the run, whatever the filters (button)"],
            Buttons(show, "data-view"));

        var note = header.NextElementSibling!;
        Assert.Equal("div", note.LocalName);
        Assert.Contains("filtered-note", note.ClassList);
        Assert.Equal("status", note.GetAttribute("role"));
        Assert.True(note.HasAttribute("hidden"), "the note starts hidden");
        Assert.Equal("", note.TextContent);
        Assert.Contains("timeline-row", note.NextElementSibling!.ClassList);
    }

    [Fact]
    public void A_page_where_a_row_paid_a_warm_up_has_the_warm_up_and_order_controls()
    {
        var (features, logs) = WarmUpHtmlTests.Marked();
        var controls = Page(features, logs).QuerySelector("#scenario-timeline > .timeline-header > .timeline-controls");

        Assert.NotNull(controls);
        var groups = controls.QuerySelectorAll(":scope > .tl-seg");
        Assert.Equal(["Show", "Warm-up", "Order"], groups.Select(g => g.GetAttribute("aria-label")));
        Assert.Equal(["Show", "Warm-up", "Order"], groups.Select(g => g.QuerySelector(":scope > .tl-seg-label")!.TextContent));
        Assert.Equal(
            ["shown true Shown: Bars and times include the first-call warm-up each scenario paid, marked in its bar (button)",
             "hidden false Hidden: Bars and times leave the warm-up out: each scenario's own time (button)"],
            Buttons(groups[1], "data-warm"));
        Assert.Equal(
            ["with true With warm-up: Slowest first, counting the warm-up (button)",
             "without false Without warm-up: Slowest first by each scenario's own time, warm-up left out (button)"],
            Buttons(groups[2], "data-order"));
    }

    [Fact]
    public void Without_a_warm_up_there_is_no_warm_up_or_order_control()
    {
        // the calls are read and none is marked: a 5 ms first call is no warm-up
        var (features, logs) = WarmUpHtmlTests.Marked(firstMs: 5);
        var timeline = Page(features, logs).QuerySelector("#scenario-timeline")!;

        Assert.Single(timeline.QuerySelectorAll(".timeline-controls > .tl-seg"));
        Assert.Empty(timeline.QuerySelectorAll(".tl-seg-btn[data-warm], .tl-seg-btn[data-order]"));
    }

    [Fact]
    public void Each_row_names_its_scenario_and_carries_its_times_as_the_scenarios_element_writes_them()
    {
        var (features, logs) = WarmUpHtmlTests.Marked();
        // a fraction of a millisecond: written whole, as the scenario's own attribute writes it
        features[0].Scenarios[2].Duration = TimeSpan.FromTicks(19_104_000);
        var page = Page(features, logs);

        var scenarios = page.QuerySelectorAll("details.scenario[data-stable-id]").ToArray();
        var rows = page.QuerySelectorAll("#scenario-timeline > .timeline-row").ToArray();
        Assert.Equal(3, rows.Length);
        foreach (var row in rows)
        {
            var name = row.QuerySelector(".timeline-label")!.TextContent;
            var scenario = scenarios.Single(s => s.QuerySelector(":scope > summary .copy-scenario-name")!.GetAttribute("data-scenario-name") == name);
            Assert.Equal(scenario.GetAttribute("data-stable-id"), row.GetAttribute("data-sid"));
            Assert.Equal(scenario.GetAttribute("data-duration-ms"), row.GetAttribute("data-duration-ms"));
            Assert.Equal(scenario.GetAttribute("data-warmup-ms"), row.GetAttribute("data-warmup-ms"));
            // the scenario's element is the one a #sid- link resolves to
            Assert.False(row.HasAttribute("data-stable-id"), $"the row of {name} carries data-stable-id");
        }
        Assert.Equal(["2000", "2000", "1910"], rows.Select(r => r.GetAttribute("data-duration-ms")));
        Assert.Equal(["600", "590", null], rows.Select(r => r.GetAttribute("data-warmup-ms")));
    }

    [Fact]
    public void An_examples_row_names_its_own_example()
    {
        var (features, logs) = WarmUpHtmlTests.Marked();
        foreach (var (scenario, item) in features[0].Scenarios.Zip(new[] { "Widget", "Gadget", "Gizmo" }))
        {
            scenario.OutlineId = "Place an order";
            scenario.ExampleValues = new Dictionary<string, string> { ["item"] = item };
        }
        var page = Page(features, logs);

        var examples = page.QuerySelectorAll("details.scenario-parameterized tr[data-stable-id]")
            .Select(r => r.GetAttribute("data-stable-id")).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(3, examples.Length);
        Assert.Equal(examples, page.QuerySelectorAll("#scenario-timeline > .timeline-row")
            .Select(r => r.GetAttribute("data-sid")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Two_scenarios_of_one_name_in_two_features_are_two_rows_each_with_its_own_id()
    {
        Feature[] features =
        [
            new() { DisplayName = "Checkout", Scenarios = [Timed("c1", "Pay", 2000)] },
            new() { DisplayName = "Refunds", Scenarios = [Timed("r1", "Pay", 1000, ExecutionResult.Failed)] }
        ];
        var page = Page(features);

        var ids = page.QuerySelectorAll("details.scenario[data-stable-id]").Select(s => s.GetAttribute("data-stable-id")).ToArray();
        Assert.Equal(2, ids.Distinct().Count());
        Assert.Equal(ids, page.QuerySelectorAll("#scenario-timeline > .timeline-row").Select(r => r.GetAttribute("data-sid")));
    }

    [Fact]
    public void A_failed_scenarios_row_is_marked_failed()
    {
        var failed = Page(EveryResult()).QuerySelectorAll("#scenario-timeline > .timeline-row")
            .ToDictionary(r => r.QuerySelector(".timeline-label")!.TextContent, r => r.ClassList.Contains("timeline-row-failed"));

        Assert.Equal(new Dictionary<string, bool> { ["Passes"] = false, ["Fails"] = true, ["Skips"] = false, ["Bypasses"] = false }, failed);
    }

    [Fact]
    public void The_script_is_in_a_page_with_a_timeline_and_in_no_other()
    {
        var html = Html(EveryResult());
        var at = html.IndexOf("<script id=\"kronikol-timeline\">", StringComparison.Ordinal);

        Assert.True(at >= 0, "the timeline's script is in the page");
        var script = html[at..html.IndexOf("</script>", at, StringComparison.Ordinal)];
        Assert.Contains("'kron-filter-change'", script);
        Assert.Contains("'data-sid'", script);
        Assert.Contains("window._kronFormatDuration", script);

        Feature[] untimed = [new() { DisplayName = "Untimed", Scenarios = [new Scenario { Id = "u", DisplayName = "No time", Result = ExecutionResult.Passed }] }];
        Assert.DoesNotContain("kronikol-timeline", Html(untimed));
    }

    [Fact]
    public void The_timelines_rules_paint_with_the_themes_tokens()
    {
        var css = Stylesheets.HtmlReportStyleSheet;
        var rules = CssRules.Parse(css).Where(r => r.Selectors.Any(s => s.Contains("timeline", StringComparison.Ordinal)
            || s.Contains("tl-seg", StringComparison.Ordinal) || s.Contains("filtered-note", StringComparison.Ordinal))).ToList();
        var literal = new Regex(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(");

        Assert.NotEmpty(rules);
        foreach (var rule in rules)
            foreach (var (property, value) in rule.Declarations)
                Assert.False(literal.IsMatch(value), $"{string.Join(", ", rule.Selectors)} {{ {property}: {value} }} paints a colour of its own");
        Assert.Equal("var(--kron-pass-ring)", CssRules.Value(css, ".timeline-bar-passed", "background"));
        Assert.Equal("var(--kron-fail)", CssRules.Value(css, ".timeline-bar-failed", "background"));
        // skipped and bypassed as the run's ring draws them (owner question 1 of S11d)
        Assert.Equal("var(--kron-dot)", CssRules.Value(css, ".timeline-bar-skipped", "background"));
        Assert.Equal("var(--kron-bypass)", CssRules.Value(css, ".timeline-bar-bypassed", "background"));
        Assert.Equal("'\\2717'", CssRules.Value(css, ".timeline-row-failed .timeline-label::before", "content"));
        Assert.Equal("var(--kron-fail)", CssRules.Value(css, ".timeline-row-failed .timeline-label::before", "color"));
    }
}
