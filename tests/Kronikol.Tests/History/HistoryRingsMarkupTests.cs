using System.Net;
using System.Text.RegularExpressions;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tests.Reports;

namespace Kronikol.Tests.History;

/// <summary>
/// The History section's run trend as HistoryHtml writes it, for the report's History tab and the labs page alike (5.0.0,
/// plans/OPTION_C_PLAN.md S11; the owner's round 17 items 29 and 30). A ring per run, drawn by the run band's ring code,
/// which reads 100% only when every scenario of its run passed and has a fail arc only when the run failed; a bar per run
/// carrying its time in whole units, cut as the run band cuts a duration, and marked short where the time cannot sit inside
/// it; each ring and bar keeping its run's 4.x tooltip; and the section's colours, the verdict pills' among them, taken from
/// the tokens, with no fallback: since S13 the labs page carries the token sheet too. In 4.x both charts were bars in a
/// stretched svg.
/// </summary>
public class HistoryRingsMarkupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-history-rings").FullName;
    private static readonly DateTimeOffset At = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static RunPoint Run(int n, int passed, int failed, int total, long? durationMs = 1000, bool partial = false, string? commit = null) =>
        new($"t:{n}:1", At.AddHours(n), commit, passed, failed, total, durationMs, partial);

    private static HistoryVerdicts Verdicts(RunPoint[] runs) => new()
    {
        Suite = "Rings", Stream = "local", RunId = runs[^1].RunId, RunsRecorded = runs.Length - 1, MinRuns = 5,
        ColdStart = false, ColdStartMessage = null, Partial = false, Scenarios = [], Absent = [],
        Counts = new Dictionary<HistoryVerdictKind, int>(), Runs = runs, NewDependencies = [], FirstSeen = 0, Compare = null
    };

    /// <summary>The section as the labs page draws it; the report's History tab draws the same charts.</summary>
    private static string Section(params RunPoint[] runs) => HistoryHtml.Section(Verdicts(runs), null);

    private sealed record Ring(string Class, string Title, string Svg);

    private sealed record Column(string Class, string Title, string BarClass, string Height, string Time);

    private const string RingPattern = "<div class=\"(history-ring[^\"]*)\" role=\"listitem\" title=\"([^\"]*)\">(<svg .*?</svg>)</div>";

    private const string ColumnPattern = "<div class=\"(history-col[^\"]*)\" role=\"listitem\" title=\"([^\"]*)\">"
        + "(?:<div class=\"(history-bar[^\"]*)\" style=\"height:([^%\"]*)%\"><span class=\"history-time\">([^<]*)</span></div>)?</div>";

    /// <summary>Each ring in run order, read from the section with its entities decoded.</summary>
    private static Ring[] Rings(string html) =>
        Regex.Matches(WebUtility.HtmlDecode(html), RingPattern, RegexOptions.Singleline)
            .Select(m => new Ring(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).ToArray();

    /// <summary>Each bar's column in run order: its tooltip, and its bar's class, height and time (empty where it has no bar).</summary>
    private static Column[] Columns(string html) =>
        Regex.Matches(WebUtility.HtmlDecode(html), ColumnPattern)
            .Select(m => new Column(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value)).ToArray();

    [Fact]
    public void Each_run_has_one_ring_and_one_bar_in_run_order_each_with_its_runs_4x_tooltip()
    {
        var html = Section(Run(1, 3, 1, 4, durationMs: 290, commit: "c0000001"), Run(2, 4, 0, 4, durationMs: 310), Run(3, 4, 0, 4, durationMs: 300, partial: true));

        var rings = Rings(html);
        Assert.Equal(3, rings.Length);
        Assert.Equal("t:1:1 · 2026-10-19 13:00 · c000000 · 3 passed, 1 failed of 4", rings[0].Title);
        Assert.Equal("t:2:1 · 2026-10-19 14:00 · 4 passed, 0 failed of 4", rings[1].Title);
        Assert.Equal("t:3:1 · 2026-10-19 15:00 · 4 passed, 0 failed of 4 · partial", rings[2].Title);

        var columns = Columns(html);
        Assert.Equal(3, columns.Length);
        Assert.Equal("t:1:1 · 2026-10-19 13:00 · 290 ms", columns[0].Title);
        Assert.Equal("t:3:1 · 2026-10-19 15:00 · 300 ms · partial", columns[2].Title);

        // a failed run is marked on its ring, a partial one on its ring and its bar's column
        Assert.Equal(["history-ring history-ring-failed", "history-ring", "history-ring history-run-partial"], rings.Select(r => r.Class).ToArray());
        Assert.Equal(["history-col", "history-col", "history-col history-run-partial"], columns.Select(c => c.Class).ToArray());
    }

    /// <summary>
    /// The run band's rule (ReportGenerator.RingRate): rounded down and at most 99 while any scenario of the run did not
    /// pass, so one failure in 214 reads 99%, where rounding read 100. A run that only skipped a scenario reads 99% too,
    /// since the rule counts every scenario of the run: the rate's denominator is a question for the owner (S11f, Q1).
    /// </summary>
    [Theory]
    [InlineData(214, 0, 214, "100%")]
    [InlineData(213, 1, 214, "99%")]
    [InlineData(212, 2, 214, "99%")]
    [InlineData(199, 0, 200, "99%")]
    [InlineData(1, 1, 2, "50%")]
    [InlineData(2, 1, 3, "66%")]
    [InlineData(1, 999, 1000, "0%")]
    public void A_ring_reads_100_percent_only_when_every_scenario_of_its_run_passed(int passed, int failed, int total, string rate)
    {
        var rings = Rings(Section(Run(1, 1, 0, 1), Run(2, passed, failed, total)));

        Assert.Equal(2, rings.Length);
        Assert.Contains($">{rate}</text>", rings[1].Svg);
        Assert.Contains($"aria-label=\"{rate} passed\"", rings[1].Svg);
    }

    [Fact]
    public void A_fail_arc_is_drawn_only_on_a_run_that_failed_over_a_track_for_what_neither_arc_counts()
    {
        var rings = Rings(Section(Run(1, 4, 0, 4), Run(2, 3, 1, 4), Run(3, 3, 0, 4), Run(4, 0, 4, 4)));

        Assert.Equal(4, rings.Length);
        Assert.Equal([false, true, false, true], rings.Select(r => r.Svg.Contains("<circle class=\"ring-fail\"", StringComparison.Ordinal)).ToArray());
        Assert.Equal([true, true, true, false], rings.Select(r => r.Svg.Contains("<circle class=\"ring-pass\"", StringComparison.Ordinal)).ToArray());
        Assert.All(rings, r => Assert.Contains("<circle class=\"ring-track\"", r.Svg));
        // each arc is its share of the whole run: three of four passed in the third run, and the fourth scenario's quarter
        // is left to the track
        Assert.Contains("<circle class=\"ring-pass\" cx=\"50\" cy=\"50\" r=\"40.0\" fill=\"none\" stroke-width=\"12\" stroke-dasharray=\"188.50 62.83\"", rings[2].Svg);
        Assert.Contains("<circle class=\"ring-fail\" cx=\"50\" cy=\"50\" r=\"40.0\" fill=\"none\" stroke-width=\"12\" stroke-dasharray=\"62.83 188.50\" stroke-dashoffset=\"-188.50\"", rings[1].Svg);
        // no arc has a title of its own, so the run's tooltip shows wherever its ring is pointed at
        Assert.All(rings, r => Assert.DoesNotContain("<title>", r.Svg));
    }

    [Fact]
    public void A_run_of_no_scenarios_is_drawn_as_the_track_alone_with_no_rate()
    {
        var html = Section(Run(1, 2, 0, 2), Run(2, 0, 0, 0));

        var empty = Regex.Match(html, "<div class=\"history-ring\" role=\"listitem\" title=\"t:2:1 [^\"]*\">(<svg .*?</svg>)</div>", RegexOptions.Singleline);
        Assert.True(empty.Success, "no ring for the run of no scenarios");
        Assert.Contains("aria-label=\"no scenarios\"", empty.Groups[1].Value);
        Assert.Contains(">&#8211;</text>", empty.Groups[1].Value);
        Assert.Contains("<circle class=\"ring-track\"", empty.Groups[1].Value);
        Assert.DoesNotContain("<circle class=\"ring-pass\"", empty.Groups[1].Value);
    }

    [Theory]
    [InlineData(850L, "850ms")]
    [InlineData(999L, "999ms")]
    [InlineData(44_000L, "44s")]
    [InlineData(44_999L, "44s")]
    [InlineData(146_000L, "2m 26s")]
    [InlineData(3_599_999L, "59m 59s")]
    [InlineData(3_900_000L, "1h 5m")]
    [InlineData(7_199_999L, "1h 59m")]
    public void A_bar_carries_its_runs_time_in_whole_units_cut_and_never_rounded_up(long ms, string time)
    {
        var columns = Columns(Section(Run(1, 1, 0, 1, durationMs: 1000), Run(2, 1, 0, 1, durationMs: ms)));

        Assert.Equal(2, columns.Length);
        Assert.Equal(time, columns[1].Time);
    }

    [Fact]
    public void A_bar_too_short_to_hold_its_time_is_marked_and_a_run_with_no_duration_has_no_bar()
    {
        var columns = Columns(Section(
            Run(1, 1, 0, 1, durationMs: 100_000), Run(2, 1, 0, 1, durationMs: 29_000), Run(3, 1, 0, 1, durationMs: 30_000),
            Run(4, 1, 0, 1, durationMs: null), Run(5, 1, 0, 1, durationMs: 1)));

        Assert.Equal(5, columns.Length);
        Assert.Equal(("history-bar", "100", "1m 40s"), (columns[0].BarClass, columns[0].Height, columns[0].Time));
        Assert.Equal(("history-bar history-bar-short", "29"), (columns[1].BarClass, columns[1].Height));
        Assert.Equal(("history-bar", "30"), (columns[2].BarClass, columns[2].Height));
        // no bar and no time, and the tooltip says why
        Assert.Equal(("", ""), (columns[3].BarClass, columns[3].Time));
        Assert.EndsWith(" · no duration", columns[3].Title);
        // the least a bar is drawn at, so a run of a millisecond still shows
        Assert.Equal(("history-bar history-bar-short", "8", "1ms"), (columns[4].BarClass, columns[4].Height, columns[4].Time));
    }

    [Fact]
    public void No_stretched_svg_chart_is_left_and_both_charts_keep_their_titles()
    {
        var html = Section(Run(1, 3, 1, 4), Run(2, 4, 0, 4));

        Assert.DoesNotContain("history-chart-svg", html);
        Assert.DoesNotContain("<rect", html);
        Assert.DoesNotContain("preserveAspectRatio", html);
        Assert.Equal(2, Regex.Matches(html, "<div class=\"history-chart\"><div class=\"history-chart-title\">").Count);
        Assert.Contains(">Pass rate, oldest run first</div>", html);
        Assert.Contains(">Total duration, oldest run first</div>", html);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void The_rings_and_bars_write_numbers_a_browser_reads(string culture)
    {
        var html = CultureRun.Under(culture, () => Section(Run(1, 2, 1, 4, durationMs: 1000), Run(2, 4, 0, 4, durationMs: 333)));

        Assert.Contains("r=\"40.0\"", html);
        Assert.Contains("stroke-dasharray=\"125.66 125.66\"", html); // two of four passed in the first run
        Assert.Contains("stroke-dashoffset=\"-125.66\"", html);      // and its failure follows them
        Assert.Contains("style=\"height:33.3%\"", html);
        Assert.Contains(">50%</text>", html);
    }

    /// <summary>The run band and the History bars write a duration one way (ReportGenerator.FormatDuration), which has an
    /// hour form from 5.0.0: a run of an hour and five minutes read "65m 30s" in the band.</summary>
    [Fact]
    public void The_run_band_writes_an_hour_long_run_in_hours_as_the_bars_do()
    {
        var start = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);
        Feature[] features = [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "s0", DisplayName = "Scenario 0", Result = ExecutionResult.Passed }] }];

        var path = ReportGenerator.GenerateHtmlReport([], features, start, start.AddMinutes(65).AddSeconds(30), null,
            Path.Combine(_dir, "HourLong.html"), "Hour", includeTestRunData: true);

        Assert.Contains("<dt>Duration</dt><dd>1h 5m</dd>", ReportHtml.Read(path));
    }

    /// <summary>A guard, passing before S11f too: the run band's ring, which now shares its drawing with the History rings,
    /// keeps an arc titled with its count for each outcome, draws no track, and draws nothing for a run of no scenarios.</summary>
    [Fact]
    public void The_run_bands_ring_keeps_its_titled_arcs_and_draws_no_track()
    {
        var svg = ReportGenerator.GeneratePieChartSvg(2, 1, 1, 1);

        Assert.DoesNotContain("ring-track", svg);
        Assert.Equal(["Passed: 2 (40%)", "Failed: 1 (20%)", "Skipped: 1 (20%)", "Bypassed: 1 (20%)"],
            Regex.Matches(svg, "<title>([^<]*)</title>").Select(m => m.Groups[1].Value).ToArray());
        Assert.Equal("", ReportGenerator.GeneratePieChartSvg(0, 0, 0, 0));
    }

    /// <summary>
    /// The verdict pills and the card take their colours from the tokens, with no fallback: since S13 every page that
    /// carries this sheet carries the token sheet, the labs page too, so a fallback would only hide a missing token. Every
    /// kind keeps a look of its own, where the preview's card rule drew six kinds as the quiet pill (stylesheet inventory
    /// 3.8 item 4), and always-failing keeps the failure's look.
    /// </summary>
    [Fact]
    public void Every_verdict_kind_keeps_its_own_look_and_every_colour_is_a_token()
    {
        var css = Stylesheets.HistoryStyleSheet;
        string Look(string kind) => string.Join(" | ", new[] { "background", "color", "border-color" }.Select(p => CssRules.Value(css, ".history-verdict-" + kind, p) ?? ""));

        Assert.Equal(Look("broke"), Look("always-failing"));
        string[] kinds = ["broke", "fixed", "flaky", "new", "slower", "behaviour-changed", "quarantined"];
        Assert.Equal(kinds.Length, kinds.Select(Look).Distinct().Count());
        foreach (var kind in new[] { "reordered", "unstable-shape", "alternating" })
            Assert.Equal(Look("behaviour-changed"), Look(kind));
        Assert.Empty(CssRules.For(css, ".history-verdict-faster"));

        // every rule of the section that paints a colour (the sparkline's, drawn in scenario headers, is not S11f's)
        string[] painted = [".history-verdict", ".history-section", ".history-summary-line", ".history-meta", ".history-note", ".history-compare",
            ".history-chart", ".history-ring", ".history-bar", ".history-time", ".history-list", ".history-count", ".history-link",
            ".history-evidence", ".history-series", ".history-more"];
        foreach (var rule in CssRules.Parse(css).Where(r => r.Selectors.Any(s => painted.Any(p => s.StartsWith(p, StringComparison.Ordinal)))))
            foreach (var (property, value) in rule.Declarations.Where(d => d.Key is "background" or "color" or "border" or "border-color" or "border-top" or "fill" or "stroke"))
                Assert.True(value.Contains("var(--", StringComparison.Ordinal) || value == "transparent", $"{rule.Selectors[0]} {{ {property}: {value} }} is not a token");
        Assert.DoesNotMatch(@"var\(--[\w-]+\s*,", css);
    }
}
