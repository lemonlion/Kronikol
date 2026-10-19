# -*- coding: utf-8 -*-
# S11f: the History section's run trend (plans/OPTION_C_PLAN.md S11; the owner's round 17 items 29 and 30). The pass rate
# becomes a ring per run, drawn by the run band's own ring code (ReportGenerator.RingSvg, factored out of
# GeneratePieChartSvg, with RingRate holding the "never 100% unless every scenario passed" rule); total duration becomes
# HTML bars, each carrying its time as the run band writes a duration (ReportGenerator.FormatDuration, made internal and
# given an hour form: "850ms", "44s", "2m 26s", "1h 5m"). Both are written by HistoryHtml as markup, so the labs page,
# which runs no script, draws them too. history-styles.css moves to the tokens (the card look of the preview's
# c-round2.css 89-116, with rounds 4, 7 and 17), every var() with its light value as a fallback for the labs page, which
# has no token sheet until S13. Not ported: D47's hatch (round 17 hid the chart it hatched), .history-verdict-faster.
#
# usage: python scripts-s11f.py <root>      root: a checkout of the v5 branch, e.g. C:/Code/Kronikol-v5-s11a
# Every anchor is checked to occur exactly once in every file before any file is written; nothing is written otherwise.
import re
import sys

if len(sys.argv) != 2:
    sys.exit('usage: python scripts-s11f.py <root>')
ROOT = sys.argv[1].replace(chr(92), '/').rstrip('/') + '/'

HTML_CS = 'src/Kronikol/History/HistoryHtml.cs'
RG_CS = 'src/Kronikol/Reports/ReportGenerator.cs'
CSS = 'src/Kronikol/Reports/history-styles.css'

# ---------------------------------------------------------------------------------------------------------------------
# HistoryHtml.cs: the two charts, from bars in a stretched svg to rings and HTML bars
# ---------------------------------------------------------------------------------------------------------------------
HTML_OLD = r'''    private static string PassRateChart(IReadOnlyList<RunPoint> runs) =>
        Chart("Pass rate, oldest run first", runs,
            r => r.Passed + r.Failed == 0 ? 0 : (double)r.Passed / (r.Passed + r.Failed),
            1.0,
            r => r.Failed == 0 ? "history-bar-pass" : "history-bar-fail",
            r => $"{r.RunId} · {r.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}{(r.Commit is { } c ? " · " + (c.Length > 7 ? c[..7] : c) : "")} · {r.Passed.ToString(CultureInfo.InvariantCulture)} passed, {r.Failed.ToString(CultureInfo.InvariantCulture)} failed of {r.Total.ToString(CultureInfo.InvariantCulture)}{(r.Partial ? " · partial" : "")}{DegradedTail(r)}");

    private static string DurationChart(IReadOnlyList<RunPoint> runs)
    {
        var max = runs.Max(r => r.DurationMs ?? 0);
        return Chart("Total duration, oldest run first", runs,
            r => r.DurationMs ?? 0,
            max == 0 ? 1 : max,
            _ => "history-bar-duration",
            r => $"{r.RunId} · {r.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {FormatMs(r.DurationMs)}{(r.Partial ? " · partial" : "")}{DegradedTail(r)}");
    }

    private static string DegradedTail(RunPoint run) => run is { Degraded: true, Pace: { } pace } ? $" · degraded {HistorySummary.Times(pace)}" : "";

    /// <summary>A bar per run in one small SVG: a handful of nodes for the whole report, sized by CSS.</summary>
    private static string Chart(string title, IReadOnlyList<RunPoint> runs, Func<RunPoint, double> value, double max,
        Func<RunPoint, string> barClass, Func<RunPoint, string> tooltip)
    {
        const int barWidth = 5;
        const int gap = 1;
        const int height = 40;
        var width = runs.Count * (barWidth + gap);
        var sb = new StringBuilder();
        sb.Append($"<div class=\"history-chart\"><div class=\"history-chart-title\">{HtmlEncode(title)}</div>");
        sb.Append($"<svg class=\"history-chart-svg\" viewBox=\"0 0 {width.ToString(CultureInfo.InvariantCulture)} {height.ToString(CultureInfo.InvariantCulture)}\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"{HtmlEncode(title)}\">");
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var share = max <= 0 ? 0 : Math.Clamp(value(run) / max, 0, 1);
            var barHeight = Math.Max(1.0, share * (height - 2));
            var x = (i * (barWidth + gap)).ToString(CultureInfo.InvariantCulture);
            var y = (height - 1 - barHeight).ToString("0.##", CultureInfo.InvariantCulture);
            var h = barHeight.ToString("0.##", CultureInfo.InvariantCulture);
            sb.Append($"<rect class=\"history-bar {barClass(run)}{(run.Partial ? " history-bar-partial" : "")}\" x=\"{x}\" y=\"{y}\" width=\"{barWidth.ToString(CultureInfo.InvariantCulture)}\" height=\"{h}\"><title>{HtmlEncode(tooltip(run))}</title></rect>");
        }
        sb.Append("</svg></div>");
        return sb.ToString();
    }
'''

HTML_NEW = r'''    /// <summary>
    /// The pass rate, a ring per run, oldest first (5.0.0, plans/OPTION_C_PLAN.md S11; the owner's round 17 item 29): the
    /// ring the run band draws (<see cref="Reports.ReportGenerator.RingSvg"/>), a pass arc and a fail arc over a track that
    /// shows what neither counts, with the rate inside, which reads 100% only when every scenario of the run passed. A ring is
    /// marked when its run failed (<c>history-ring-failed</c>) and when it was partial (<c>history-run-partial</c>), and
    /// carries its run's tooltip, as the 4.x bar did. Markup, not script: the labs page, which runs none, draws it too.
    /// </summary>
    private static string PassRateChart(IReadOnlyList<RunPoint> runs)
    {
        const string title = "Pass rate, oldest run first";
        var sb = new StringBuilder();
        sb.Append("<div class=\"history-chart\"><div class=\"history-chart-title\">").Append(title).Append("</div>");
        sb.Append("<div class=\"history-rings\" role=\"list\" aria-label=\"").Append(title).Append("\">");
        foreach (var run in runs)
        {
            sb.Append("<div class=\"history-ring").Append(run.Failed > 0 ? " history-ring-failed" : "").Append(run.Partial ? " history-run-partial" : "")
                .Append("\" role=\"listitem\" title=\"").Append(HtmlEncode(PassRateTooltip(run))).Append("\">")
                .Append(Reports.ReportGenerator.RingSvg(run.Passed, run.Failed, 0, 0, run.Total, track: true, arcTitles: false))
                .Append("</div>");
        }
        sb.Append("</div></div>");
        return sb.ToString();
    }

    private static string PassRateTooltip(RunPoint r) =>
        $"{r.RunId} · {r.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}{(r.Commit is { } c ? " · " + (c.Length > 7 ? c[..7] : c) : "")} · {r.Passed.ToString(CultureInfo.InvariantCulture)} passed, {r.Failed.ToString(CultureInfo.InvariantCulture)} failed of {r.Total.ToString(CultureInfo.InvariantCulture)}{(r.Partial ? " · partial" : "")}{DegradedTail(r)}";

    /// <summary>
    /// Total duration, a bar per run, oldest first (5.0.0; the owner's round 17 item 30): bars drawn as HTML, so each carries
    /// its time unstretched, written as the run band writes a duration (<see cref="Reports.ReportGenerator.FormatDuration"/>:
    /// "850ms", "44s", "2m 26s", "1h 5m"), in the middle of its bar, or just above a bar under <see cref="ShortBarPercent"/>
    /// of the chart's height. A run with no recorded duration keeps its column and its tooltip but has no bar. A column is
    /// wide enough for its time, and a row of more runs than its card holds scrolls inside the card, opening on the newest
    /// (history-styles.css).
    /// </summary>
    private static string DurationChart(IReadOnlyList<RunPoint> runs)
    {
        const string title = "Total duration, oldest run first";
        var max = runs.Max(r => r.DurationMs ?? 0);
        var sb = new StringBuilder();
        sb.Append("<div class=\"history-chart\"><div class=\"history-chart-title\">").Append(title).Append("</div>");
        sb.Append("<div class=\"history-bars-scroll\"><div class=\"history-bars\" role=\"list\" aria-label=\"").Append(title).Append("\">");
        foreach (var run in runs)
        {
            sb.Append("<div class=\"history-col").Append(run.Partial ? " history-run-partial" : "")
                .Append("\" role=\"listitem\" title=\"").Append(HtmlEncode(DurationTooltip(run))).Append("\">");
            if (run.DurationMs is { } ms)
            {
                var height = max <= 0 ? MinBarPercent : Math.Clamp(100.0 * ms / max, MinBarPercent, 100.0);
                sb.Append("<div class=\"history-bar").Append(height < ShortBarPercent ? " history-bar-short" : "")
                    .Append("\" style=\"height:").Append(height.ToString("0.#", CultureInfo.InvariantCulture)).Append("%\">")
                    .Append("<span class=\"history-time\">").Append(Reports.ReportGenerator.FormatDuration(TimeSpan.FromMilliseconds(ms)))
                    .Append("</span></div>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div></div></div>");
        return sb.ToString();
    }

    private static string DurationTooltip(RunPoint r) =>
        $"{r.RunId} · {r.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {FormatMs(r.DurationMs)}{(r.Partial ? " · partial" : "")}{DegradedTail(r)}";

    private static string DegradedTail(RunPoint run) => run is { Degraded: true, Pace: { } pace } ? $" · degraded {HistorySummary.Times(pace)}" : "";

    /// <summary>The least a bar is drawn at, as a share of the chart's height, so a run of a few milliseconds still shows.</summary>
    private const double MinBarPercent = 8;

    /// <summary>
    /// Under this share of the chart's height a bar is too short to hold its time, which is drawn just above it instead: the
    /// chart is 56 px high and the time 11.5 px (history-styles.css), so a bar of 30% (16.8 px) holds it with 2.6 px above
    /// and below. Move it with either.
    /// </summary>
    private const double ShortBarPercent = 30;
'''

# ---------------------------------------------------------------------------------------------------------------------
# ReportGenerator.cs: the run band's ring shared with History's rings; the duration formatter shared with History's bars
# ---------------------------------------------------------------------------------------------------------------------
RING_OLD = r'''    internal static string GeneratePieChartSvg(int passed, int failed, int skipped, int bypassed)
    {
        var total = passed + failed + skipped + bypassed;
        if (total == 0) return "";

        // A run with any scenario that did not pass never reads 100%: rounding put 213 of 214 at 100 until 5.0.0.
        var passRate = passed == total ? 100 : (int)Math.Min(99, Math.Floor(100.0 * passed / total));
        var segments = new List<(double pct, string kind, string label, int count)>();
        if (passed > 0) segments.Add((100.0 * passed / total, "pass", "Passed", passed));
        if (failed > 0) segments.Add((100.0 * failed / total, "fail", "Failed", failed));
        if (skipped > 0) segments.Add((100.0 * skipped / total, "skip", "Skipped", skipped));
        if (bypassed > 0) segments.Add((100.0 * bypassed / total, "bypass", "Bypassed", bypassed));

        const double radius = 40;
        const double circumference = 2 * Math.PI * radius;
        var sb = new StringBuilder();
        // The arcs and the rate take their colours from the page's tokens through these classes (5.0.0).
        sb.Append($"<svg viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"{passRate}% passed\">");

        var offset = 0.0;
        foreach (var (pct, kind, label, count) in segments)
        {
            var dash = circumference * pct / 100.0;
            var gap = circumference - dash;
            var dashOffset = -offset * circumference / 100.0;
            sb.Append(CultureInfo.InvariantCulture,
                $"<circle class=\"ring-{kind}\" cx=\"50\" cy=\"50\" r=\"{radius:F1}\" fill=\"none\" stroke-width=\"12\" " +
                $"stroke-dasharray=\"{dash:F2} {gap:F2}\" stroke-dashoffset=\"{dashOffset:F2}\" transform=\"rotate(-90 50 50)\">" +
                $"<title>{label}: {count} ({pct:F0}%)</title></circle>");
            offset += pct;
        }

        sb.Append($"<text class=\"ring-rate\" x=\"50\" y=\"50\" text-anchor=\"middle\" dominant-baseline=\"central\">{passRate}%</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
'''

RING_NEW = r'''    internal static string GeneratePieChartSvg(int passed, int failed, int skipped, int bypassed) =>
        RingSvg(passed, failed, skipped, bypassed, passed + failed + skipped + bypassed, track: false, arcTitles: true);

    /// <summary>
    /// The rate a run's ring reads (5.0.0): 100 only when every scenario passed, otherwise the share that passed, rounded down
    /// and at most 99, so a run with any scenario that did not pass never reads 100% (rounding put 213 of 214 at 100 until
    /// 5.0.0). 0 for a run of no scenarios.
    /// </summary>
    internal static int RingRate(int passed, int total) =>
        total <= 0 ? 0 : passed >= total ? 100 : (int)Math.Min(99, Math.Floor(100.0 * passed / total));

    /// <summary>
    /// A run's ring (5.0.0): an arc for each outcome the run had, passed, failed, skipped and bypassed in that order, each its
    /// share of <paramref name="total"/>, with the rate inside (<see cref="RingRate"/>); the arcs and the rate take their
    /// colours from the page's tokens through their classes. The run band draws the run's own (<see cref="GeneratePieChartSvg"/>),
    /// each arc titled with its count, and nothing for a run of no scenarios. The History section draws one per run of its
    /// window (<c>HistoryHtml</c>, plans/OPTION_C_PLAN.md S11) over a track (<paramref name="track"/>), which shows the
    /// scenarios no arc counts, with untitled arcs, so the run's own tooltip shows wherever the ring is pointed at; there a run
    /// of no scenarios is the track alone, with a dash for its rate.
    /// </summary>
    internal static string RingSvg(int passed, int failed, int skipped, int bypassed, int total, bool track, bool arcTitles)
    {
        total = Math.Max(total, passed + failed + skipped + bypassed);
        if (total == 0 && !track) return "";

        var passRate = RingRate(passed, total);
        var segments = new List<(double pct, string kind, string label, int count)>();
        if (passed > 0) segments.Add((100.0 * passed / total, "pass", "Passed", passed));
        if (failed > 0) segments.Add((100.0 * failed / total, "fail", "Failed", failed));
        if (skipped > 0) segments.Add((100.0 * skipped / total, "skip", "Skipped", skipped));
        if (bypassed > 0) segments.Add((100.0 * bypassed / total, "bypass", "Bypassed", bypassed));

        const double radius = 40;
        const double circumference = 2 * Math.PI * radius;
        var sb = new StringBuilder();
        // The arcs and the rate take their colours from the page's tokens through these classes (5.0.0).
        sb.Append(total == 0
            ? "<svg viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"no scenarios\">"
            : $"<svg viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"{passRate}% passed\">");
        if (track)
            sb.Append(CultureInfo.InvariantCulture, $"<circle class=\"ring-track\" cx=\"50\" cy=\"50\" r=\"{radius:F1}\" fill=\"none\" stroke-width=\"12\"></circle>");

        var offset = 0.0;
        foreach (var (pct, kind, label, count) in segments)
        {
            var dash = circumference * pct / 100.0;
            var gap = circumference - dash;
            var dashOffset = -offset * circumference / 100.0;
            sb.Append(CultureInfo.InvariantCulture,
                $"<circle class=\"ring-{kind}\" cx=\"50\" cy=\"50\" r=\"{radius:F1}\" fill=\"none\" stroke-width=\"12\" " +
                $"stroke-dasharray=\"{dash:F2} {gap:F2}\" stroke-dashoffset=\"{dashOffset:F2}\" transform=\"rotate(-90 50 50)\">");
            if (arcTitles)
                sb.Append(CultureInfo.InvariantCulture, $"<title>{label}: {count} ({pct:F0}%)</title>");
            sb.Append("</circle>");
            offset += pct;
        }

        sb.Append(total == 0
            ? "<text class=\"ring-rate\" x=\"50\" y=\"50\" text-anchor=\"middle\" dominant-baseline=\"central\">&#8211;</text>"
            : $"<text class=\"ring-rate\" x=\"50\" y=\"50\" text-anchor=\"middle\" dominant-baseline=\"central\">{passRate}%</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
'''

DURATION_OLD = r'''    private static string FormatDuration(TimeSpan duration)
    {
        var total = duration.Duration();
        if (total.TotalSeconds < 1)
            return $"{total.Milliseconds}ms";
        if (total.TotalMinutes < 1)
            return $"{total.Seconds}s";
        return $"{(int)total.TotalMinutes}m {total.Seconds}s";
    }
'''

DURATION_NEW = r'''    /// <summary>
    /// A duration in whole units, cut and never rounded up, as <see cref="FormatDurationBadge"/> cuts: "850ms" under a second,
    /// "44s" under a minute, "2m 26s" under an hour and "1h 5m" from an hour (5.0.0; it wrote "65m 0s" until then). The run
    /// band's duration, the features table and the History section's bars write it (plans/OPTION_C_PLAN.md S11, the owner's
    /// round 17 item 30); a scenario's badge and the timeline keep <see cref="FormatDurationBadge"/>'s tenth of a second.
    /// </summary>
    internal static string FormatDuration(TimeSpan duration)
    {
        var total = duration.Duration();
        if (total.TotalSeconds < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{total.Milliseconds}ms");
        if (total.TotalMinutes < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{total.Seconds}s");
        if (total.TotalHours < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{total.Minutes}m {total.Seconds}s");
        return string.Create(CultureInfo.InvariantCulture, $"{(long)total.TotalHours}h {total.Minutes}m");
    }
'''

# ---------------------------------------------------------------------------------------------------------------------
# history-styles.css: the pills, the card, the charts and the lists on the tokens (lines 12-136 of the 4.x sheet)
# ---------------------------------------------------------------------------------------------------------------------
CSS_OLD = r'''        .history-verdict {
            font-size: 0.72em;
            font-weight: normal;
            padding: 0.1em 0.45em;
            border-radius: 0.3em;
            margin-left: 0.4em;
            display: inline-block;
            white-space: nowrap;
            background: rgb(232, 232, 232);
            color: rgb(60, 60, 60);
        }
        .history-verdict-broke, .history-verdict-failing, .history-verdict-always-failing {
            background: rgb(255, 220, 220);
            color: rgb(191, 0, 0);
        }
        .history-verdict-fixed {
            background: rgb(220, 245, 220);
            color: rgb(30, 100, 30);
        }
        .history-verdict-flaky {
            background: rgb(255, 240, 200);
            color: rgb(150, 90, 0);
        }
        .history-verdict-new {
            background: rgb(220, 230, 245);
            color: rgb(30, 60, 140);
        }
        .history-verdict-slower {
            background: rgb(240, 225, 250);
            color: rgb(100, 40, 140);
        }
        .history-verdict-behaviour-changed, .history-verdict-reordered, .history-verdict-unstable-shape, .history-verdict-alternating {
            background: rgb(215, 240, 240);
            color: rgb(0, 100, 100);
        }
        .history-verdict-quarantined {
            background: repeating-linear-gradient(45deg, rgb(232, 232, 232) 0 4px, rgb(248, 248, 248) 4px 8px);
            color: rgb(90, 90, 90);
        }
        .history-section {
            margin: 1em 0;
            border: 1px solid rgb(200, 200, 200);
            border-radius: 0.8em;
            padding: 0.4em 1em;
            background: rgb(250, 250, 250);
        }
        .history-section > summary {
            cursor: pointer;
        }
        .history-summary-line {
            font-weight: normal;
            font-size: 0.8em;
            margin-left: 0.6em;
            color: rgb(90, 90, 90);
        }
        .history-meta, .history-note, .history-compare {
            font-size: 0.85em;
            color: rgb(90, 90, 90);
            margin: 0.4em 0;
        }
        .history-charts {
            display: flex;
            gap: 1.5em;
            flex-wrap: wrap;
            margin: 0.6em 0;
        }
        .history-chart {
            flex: 1 1 260px;
            min-width: 200px;
        }
        .history-chart-title {
            font-size: 0.8em;
            color: rgb(90, 90, 90);
            margin-bottom: 0.2em;
        }
        .history-chart-svg {
            width: 100%;
            height: 40px;
            display: block;
            background: rgb(244, 244, 244);
            border-radius: 0.3em;
        }
        .history-bar-pass {
            fill: rgb(34, 139, 34);
        }
        .history-bar-fail {
            fill: rgb(191, 0, 0);
        }
        .history-bar-duration {
            fill: rgb(100, 150, 255);
        }
        .history-bar-partial {
            opacity: 0.45;
        }
        .history-list h4 {
            margin: 0.6em 0 0.2em;
            font-size: 0.95em;
        }
        .history-count {
            font-weight: normal;
            font-size: 0.85em;
            color: rgb(120, 120, 120);
        }
        .history-list ul {
            margin: 0;
            padding-left: 1.2em;
        }
        .history-list li {
            margin: 0.15em 0;
            font-size: 0.9em;
        }
        .history-evidence {
            color: rgb(90, 90, 90);
            margin-left: 0.3em;
        }
        .history-series {
            font-family: Consolas, "Courier New", monospace;
            font-size: 0.85em;
            margin-left: 0.3em;
            letter-spacing: 0.1em;
        }
        .history-more {
            color: rgb(120, 120, 120);
            list-style: none;
        }
'''

CSS_NEW = r'''        /* The verdict pill: each kind's colours from the report's tokens (5.0.0, plans/OPTION_C_PLAN.md S11), every kind drawn
           apart as 4.x drew them, where the preview's one card rule drew six kinds as the quiet pill (stylesheet inventory 3.8
           item 4). Every token in this sheet falls back to its light value: the labs page has no token sheet until S13. */
        .history-verdict {
            font-size: 11px;
            font-weight: 600;
            padding: 1px 8px;
            border-radius: 999px;
            margin-left: 0.4em;
            display: inline-block;
            white-space: nowrap;
            background: var(--kron-quiet, #f7f9fc);
            color: var(--kron-ink-quiet, #545c6a);
            border: 1px solid var(--kron-quiet-edge, #d3d9e3);
        }
        .history-verdict-broke, .history-verdict-failing, .history-verdict-always-failing {
            background: var(--kron-fail-soft, #fbecea);
            color: var(--kron-fail, #b3372e);
            border-color: var(--kron-fail-edge, #efc7c2);
        }
        .history-verdict-fixed {
            background: var(--kron-pass-soft, #e6f3ec);
            color: var(--kron-pass, #276c44);
            border-color: transparent;
        }
        .history-verdict-flaky {
            background: var(--kron-warn-soft, #fbf3e7);
            color: var(--kron-warn, #8a5a17);
            border-color: var(--kron-warn-edge, #ecd3ae);
        }
        .history-verdict-new {
            background: var(--kron-selected, #dce8fb);
            color: var(--kron-selected-ink, #1d4fb0);
            border-color: var(--kron-selected-edge, #6a8fd9);
        }
        .history-verdict-slower {
            background: color-mix(in srgb, var(--kron-prop-key, #7d3c98) 12%, var(--kron-card, #ffffff));
            color: var(--kron-prop-key, #7d3c98);
            border-color: color-mix(in srgb, var(--kron-prop-key, #7d3c98) 35%, var(--kron-card, #ffffff));
        }
        .history-verdict-behaviour-changed, .history-verdict-reordered, .history-verdict-unstable-shape, .history-verdict-alternating {
            background: color-mix(in srgb, var(--dg-db, #0e7c86) 12%, var(--kron-card, #ffffff));
            color: color-mix(in srgb, var(--dg-db, #0e7c86) 75%, var(--kron-ink, #242b36));
            border-color: color-mix(in srgb, var(--dg-db, #0e7c86) 35%, var(--kron-card, #ffffff));
        }
        .history-verdict-quarantined {
            background: repeating-linear-gradient(45deg, var(--kron-skip-soft, #eef0f3) 0 4px, var(--kron-card, #ffffff) 4px 8px);
            color: var(--kron-ink-quiet, #545c6a);
        }

        /* The History section as a card (5.0.0, plans/OPTION_C_PLAN.md S11; the preview's c-round2.css 89-116, with its type
           from c-round7.css and its shadow from c-round4.css): the heading over a hairline, then the body. It sets its own
           font, for the labs page, where the base sheet's font token resolves to nothing. */
        .history-section {
            margin: 1em 0;
            padding: 0;
            border: 1px solid var(--kron-hairline, #d0d7e2);
            border-radius: 12px;
            background: var(--kron-card, #ffffff);
            box-shadow: var(--kron-card-shadow, 0 1px 2px rgba(20, 30, 60, .07), 0 2px 6px rgba(20, 30, 60, .05));
            color: var(--kron-ink, #242b36);
            font-family: var(--kron-font, -apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans", Roboto, "Helvetica Neue", Arial, sans-serif);
            overflow: hidden;
        }
        .history-section > summary {
            padding: 12px 16px;
            font-size: 14px;
            font-weight: 600;
            color: var(--kron-ink, #242b36);
        }
        .history-section[open] > summary {
            border-bottom: 1px solid var(--kron-hairline-soft, #e2e7ee);
        }
        /* in its tab the summary is the panel's heading, which the tab script keeps open; the labs page keeps the disclosure */
        .report-panel > .history-section > summary {
            list-style: none;
            cursor: default;
        }
        .report-panel > .history-section > summary::-webkit-details-marker {
            display: none;
        }
        .history-summary-line {
            font-weight: 400;
            font-size: var(--kron-fs-control, 13px);
            margin-left: 10px;
            color: var(--kron-muted, #5b6474);
        }
        .history-body {
            padding: 12px 16px 4px;
        }
        .history-meta, .history-note, .history-compare {
            font-size: var(--kron-fs-meta, 12px);
            color: var(--kron-muted, #5b6474);
            margin: 0 0 12px;
        }
        .history-meta code, .history-note code, .history-compare code, .history-evidence code {
            font-family: var(--kron-mono, ui-monospace, SFMono-Regular, "SF Mono", Menlo, "Cascadia Mono", Consolas, "Liberation Mono", monospace);
            font-size: 11.5px;
            background: var(--kron-field, #f2f5f9);
            border-radius: 4px;
            padding: 0 5px;
        }

        /* The run trend (5.0.0; the owner's round 17 items 29 and 30), written by HistoryHtml as markup, so the labs page,
           which runs no script, draws it too: a ring per run, and a bar per run carrying its time */
        .history-charts {
            display: flex;
            flex-wrap: wrap;
            gap: 14px;
            margin: 0 0 16px;
        }
        .history-chart {
            flex: 1 1 260px;
            min-width: 0;
            padding: 10px 12px;
            border: 1px solid var(--kron-hairline, #d0d7e2);
            border-radius: 8px;
        }
        .history-chart-title {
            margin-bottom: 8px;
            font-size: var(--kron-fs-label, 11px);
            font-weight: 600;
            letter-spacing: var(--kron-label-track, .06em);
            text-transform: uppercase;
            color: var(--kron-label, #5f6878);
        }
        /* rings: a few runs spread across the card, more in rows, oldest first */
        .history-rings {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(56px, 1fr));
            gap: 10px;
            justify-items: center;
            align-items: center;
        }
        .history-ring {
            width: 56px;
            height: 56px;
        }
        .history-ring svg {
            display: block;
            width: 56px;
            height: 56px;
        }
        .history-ring .ring-track { stroke: var(--kron-track, #edf0f5); }
        .history-ring .ring-pass { stroke: var(--kron-pass-ring, #3f9a62); }
        .history-ring .ring-fail { stroke: var(--kron-fail, #b3372e); }
        .history-ring .ring-rate {
            fill: var(--kron-ink, #242b36);
            font-size: 21px;
            font-weight: 650;
            letter-spacing: -.02em;
        }
        .history-ring-failed .ring-rate { fill: var(--kron-fail-ink, #7c2a24); }
        /* bars: a column wide enough for its time; a row of more runs than the card holds scrolls inside it and opens on the
           newest run, as the scroller runs right to left (so it starts at its right end) and the row inside it left to right */
        .history-bars-scroll {
            overflow-x: auto;
            overflow-y: hidden;
            direction: rtl;
        }
        .history-bars {
            direction: ltr;
            display: flex;
            align-items: flex-end;
            gap: 6px;
            width: max-content;
            min-width: 100%;
            height: 56px;
            background: var(--kron-strip, #f8fafd);
        }
        .history-col {
            flex: 1 0 auto;
            width: 48px;
            height: 100%;
            display: flex;
            align-items: flex-end;
        }
        .history-bar {
            position: relative;
            width: 100%;
            min-height: 8px;
            border-radius: 3px 3px 0 0;
            background: color-mix(in srgb, var(--kron-accent, #2559c7) 26%, var(--kron-card, #ffffff));
            box-shadow: inset 0 2px 0 var(--kron-accent, #2559c7);
        }
        .history-time {
            position: absolute;
            left: 0;
            right: 0;
            top: 50%;
            transform: translateY(-50%);
            text-align: center;
            font-size: 11.5px;
            font-weight: 600;
            line-height: 1;
            font-variant-numeric: tabular-nums;
            white-space: nowrap;
            color: var(--kron-ink, #242b36);
        }
        /* a bar too short to hold its time (HistoryHtml.ShortBarPercent) has it just above */
        .history-bar-short .history-time {
            top: auto;
            bottom: 100%;
            transform: none;
            margin-bottom: 2px;
            color: var(--kron-ink-soft, #4a5361);
        }
        /* a partial run, a filtered re-run, is drawn paler, as 4.x drew its bar; its rate and its time keep their ink */
        .history-run-partial .ring-pass, .history-run-partial .ring-fail {
            opacity: .45;
        }
        .history-run-partial .history-bar {
            background: color-mix(in srgb, var(--kron-accent, #2559c7) 10%, var(--kron-card, #ffffff));
            box-shadow: inset 0 2px 0 color-mix(in srgb, var(--kron-accent, #2559c7) 45%, var(--kron-card, #ffffff));
        }

        /* What changed, one list to a kind */
        .history-list {
            margin: 0 0 14px;
        }
        .history-list h4 {
            display: flex;
            gap: 8px;
            align-items: baseline;
            margin: 0 0 4px;
            font-size: var(--kron-fs-label, 11px);
            font-weight: 600;
            letter-spacing: var(--kron-label-track, .06em);
            text-transform: uppercase;
            color: var(--kron-label, #5f6878);
        }
        .history-count {
            font-weight: normal;
            font-size: 11px;
            letter-spacing: 0;
            font-variant-numeric: tabular-nums;
            color: var(--kron-faint, #626b7a);
        }
        .history-list ul {
            list-style: none;
            margin: 0;
            padding: 0;
        }
        .history-list li {
            display: flex;
            flex-wrap: wrap;
            align-items: baseline;
            gap: 2px 10px;
            padding: 7px 0;
            border-top: 1px solid var(--kron-hairline-soft, #e2e7ee);
            font-size: 13px;
        }
        .history-link {
            flex: 1 1 360px;
            min-width: 0;
            color: var(--kron-ink, #242b36);
            text-decoration: none;
        }
        .history-link:hover {
            color: var(--kron-accent, #2559c7);
            text-decoration: underline;
        }
        .history-evidence {
            flex: 2 1 300px;
            margin-left: 0.3em;
            font-size: 12px;
            color: var(--kron-muted, #5b6474);
        }
        .history-series {
            margin-left: 0.3em;
            font-family: var(--kron-mono, ui-monospace, SFMono-Regular, "SF Mono", Menlo, "Cascadia Mono", Consolas, "Liberation Mono", monospace);
            font-size: var(--kron-fs-meta, 12px);
            letter-spacing: .12em;
            color: var(--kron-muted, #5b6474);
        }
        .history-more {
            color: var(--kron-faint, #626b7a);
            list-style: none;
        }
'''

EDITS = {
    HTML_CS: [(HTML_OLD, HTML_NEW)],
    RG_CS: [(RING_OLD, RING_NEW), (DURATION_OLD, DURATION_NEW)],
    CSS: [(CSS_OLD, CSS_NEW)],
}

# Not applied already: each file still holds the 4.x form.
NOT_YET = {
    HTML_CS: ['history-ring', 'history-bars-scroll'],
    RG_CS: ['RingSvg(', 'RingRate('],
    CSS: ['.history-ring', 'var(--kron-'],
}


def read(rel):
    with open(ROOT + rel, encoding='utf-8', newline='') as f:
        return f.read()


# Pass 1: every anchor once, in every file, before anything is written.
texts = {}
boms = {}
for rel, pairs in EDITS.items():
    s = read(rel)
    boms[rel] = s.startswith('﻿')
    for marker in NOT_YET[rel]:
        if marker in s:
            sys.exit(f'{rel}: already holds {marker!r}; S11f looks applied, nothing written')
    for old, _ in pairs:
        n = s.count(old)
        if n != 1:
            sys.exit(f'{rel}: anchor found {n} times, nothing written: {old.splitlines()[0].strip()!r}')
    texts[rel] = s

# Pass 2: apply in order, each anchor still once at the moment it is replaced.
for rel, pairs in EDITS.items():
    s = texts[rel]
    for old, new in pairs:
        assert s.count(old) == 1, (rel, old.splitlines()[0])
        s = s.replace(old, new)
    texts[rel] = s

# Checks on the result, before writing: what was meant to land is there, byte for byte (backslashes included).
h, g, c = texts[HTML_CS], texts[RG_CS], texts[CSS]
checks = [
    (HTML_CS, h, 'sb.Append("<div class=\\"history-ring").Append(run.Failed > 0 ? " history-ring-failed" : "")', 1),
    (HTML_CS, h, 'Reports.ReportGenerator.RingSvg(run.Passed, run.Failed, 0, 0, run.Total, track: true, arcTitles: false)', 1),
    (HTML_CS, h, 'Reports.ReportGenerator.FormatDuration(TimeSpan.FromMilliseconds(ms))', 1),
    (HTML_CS, h, 'history-chart-svg', 0),
    (HTML_CS, h, '<rect ', 0),
    (HTML_CS, h, 'private static string Chart(', 0),
    (RG_CS, g, 'internal static string RingSvg(int passed, int failed, int skipped, int bypassed, int total, bool track, bool arcTitles)', 1),
    (RG_CS, g, 'internal static int RingRate(int passed, int total)', 1),
    (RG_CS, g, 'internal static string FormatDuration(TimeSpan duration)', 1),
    (RG_CS, g, 'private static string FormatDuration(', 0),
    (RG_CS, g, '$"<title>{label}: {count} ({pct:F0}%)</title>"', 1),
    (RG_CS, g, 'aria-label=\\"{passRate}% passed\\"', 1),
    (CSS, c, '.history-chart-svg', 0),
    (CSS, c, '.history-bar-pass', 0),
    (CSS, c, '.history-verdict-faster', 0),
    (CSS, c, 'direction: rtl;', 1),
    (CSS, c, '.history-bar-short .history-time {', 1),
]
for rel, text, needle, want in checks:
    got = text.count(needle)
    if got != want:
        sys.exit(f'{rel}: {needle!r} occurs {got} times after the edit, {want} expected; nothing written')
for rel in EDITS:
    if texts[rel].startswith('\ufeff') != boms[rel]:
        sys.exit(f'{rel}: the byte order mark changed; nothing written')
bare = re.findall(r'var\(--[\w-]+\s*\)', c)
if bare:
    sys.exit(f'{CSS}: a token with no fallback: {bare[:3]}; nothing written')
if re.search(r'rgb\(\s*\d', CSS_NEW):
    sys.exit(f'{CSS}: a fixed colour in the new rules; nothing written')
if any(ord(ch) > 127 for ch in CSS_NEW):
    sys.exit(f'{CSS}: a character outside ASCII in the new rules; nothing written')
if '\r' in h + g + c:
    sys.exit('a carriage return crept in; nothing written')

for rel in EDITS:
    with open(ROOT + rel, 'w', encoding='utf-8', newline='') as f:
        f.write(texts[rel])
    print('edited', rel)
print('S11f product edits applied to', ROOT)
