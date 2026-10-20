using System.Globalization;
using System.Text;
using static System.Net.WebUtility;

namespace Kronikol.History;

/// <summary>
/// Renders cross-run history as HTML (plans/CROSS_RUN_HISTORY_PLAN.md §8.1): per scenario a verdict attribute
/// the search box filters on (<c>$flaky</c>, <c>$broke</c>), a sparkline of the last runs, and a verdict pill;
/// and the History section, with the run trend and the lists of what changed. The labs page beside the report
/// (<see cref="Reports.LabsReportGenerator"/>) draws them all; the report draws the section as its History tab
/// (<see cref="ReportConfigurationOptions.ShowHistorySection"/>, on by default since 5.0.0) and the scenarios'
/// own history only where <see cref="ReportConfigurationOptions.ShowScenarioHistory"/> asks, through these same
/// renderers, so the two cannot drift (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md).
///
/// <para>Everything is rendered here, on the generation side, and the sparkline is ONE element whose
/// background gradient carries a hard colour stop per run: two hundred scenarios cost two hundred nodes,
/// not the two thousand an inline SVG per scenario would, and the report's filter and toggle budgets
/// (which walk scenario elements) stay where they are. The section links each scenario by the same
/// <c>#sid-</c> anchor <c>kronikol query</c> and <c>Failures.md</c> print: inside the report, or into it from
/// the labs page. In a report, sparklines live inside the scenario elements, so the filtered-HTML export
/// carries them; the section lives beside the timeline, which the export leaves behind on purpose.</para>
/// </summary>
internal static class HistoryHtml
{
    private const int ListCap = 50;

    /// <summary>
    /// The entry for a scenario, taking the next slot of an id the run holds more than once so two
    /// scenarios sharing a stable id each read their own line.
    /// </summary>
    public static ScenarioHistory? Entry(HistoryVerdicts history, Dictionary<string, int> slots, string stableId)
    {
        var slot = slots.TryGetValue(stableId, out var seen) ? seen : 0;
        slots[stableId] = slot + 1;
        return history.Find(stableId, slot) ?? (slot == 0 ? null : history.Find(stableId));
    }

    /// <summary>The <c>data-history-verdicts</c> attribute: every verdict, primary first.</summary>
    public static string VerdictAttribute(ScenarioHistory entry) => VerdictAttribute([entry]);

    /// <summary>The attribute for a group of rows: the union of their verdicts.</summary>
    public static string VerdictAttribute(IEnumerable<ScenarioHistory> entries)
    {
        var kinds = new HashSet<HistoryVerdictKind>();
        foreach (var entry in entries)
            kinds.UnionWith(entry.Verdicts);
        if (kinds.Count == 0)
            return "";
        return $" data-history-verdicts=\"{HistoryVerdictNames.Join(kinds)}\"";
    }

    /// <summary>
    /// The sparkline: one span, the last runs oldest first as hard colour stops of its background, the
    /// verdict and each run in its tooltip.
    /// </summary>
    public static string Sparkline(ScenarioHistory entry)
    {
        var series = entry.Series;
        if (string.IsNullOrEmpty(series))
            return "";

        var stops = new StringBuilder();
        var n = series.Length;
        for (var i = 0; i < n; i++)
        {
            var colour = Colour(series[i]);
            var from = (i * 100.0 / n).ToString("0.##", CultureInfo.InvariantCulture);
            var to = ((i + 1) * 100.0 / n).ToString("0.##", CultureInfo.InvariantCulture);
            if (i > 0) stops.Append(',');
            stops.Append(colour).Append(' ').Append(from).Append("%,").Append(colour).Append(' ').Append(to).Append('%');
        }

        return $" <span class=\"history-sparkline\" role=\"img\" aria-label=\"{HtmlEncode(Summary(entry))}\" title=\"{HtmlEncode(Tooltip(entry))}\" style=\"background:linear-gradient(90deg,{stops})\"></span>";
    }

    /// <summary>The verdict pill: the primary verdict when it is not stable, and quarantined when that applies on top.</summary>
    public static string Pill(ScenarioHistory entry)
    {
        var html = new StringBuilder();
        if (entry.Primary != HistoryVerdictKind.Stable)
            html.Append(PillHtml(entry.Primary, entry.Evidence));
        if (entry.Has(HistoryVerdictKind.Quarantined) && entry.Primary != HistoryVerdictKind.Quarantined)
            html.Append(PillHtml(HistoryVerdictKind.Quarantined, entry.Quarantine?.Reason ?? "quarantined"));
        return html.ToString();
    }

    /// <summary>One pill for a group of rows: the verdict of highest precedence among them, with how many rows carry it.</summary>
    public static string GroupPill(IEnumerable<ScenarioHistory> entries)
    {
        var telling = entries.Where(e => e.Primary != HistoryVerdictKind.Stable).OrderBy(e => HistoryAnalyzer.Precedence(e.Primary)).ToList();
        if (telling.Count == 0)
            return "";
        var kind = telling[0].Primary;
        var count = telling.Count(e => e.Primary == kind);
        return PillHtml(kind, count == 1 ? telling[0].Evidence : $"{count} rows: {telling[0].Evidence}", count > 1 ? $" ×{count}" : null);
    }

    /// <summary>
    /// What the History section shows (5.0.0, plans/OPTION_C_PLAN.md D49): the History tab's title, and its heading's in
    /// the tab, where the heading is nothing to click.
    /// </summary>
    internal const string Description = "This run against the earlier runs on the same branch: what is failing, flaky, fixed, slower, new or gone";

    /// <summary>The heading's title where the section is a disclosure, on the labs page and in <see cref="Section(HistoryVerdicts)"/>.</summary>
    private const string DisclosureTitle = "This run against the earlier runs on the same branch: click to show or hide the details";

    /// <summary>
    /// The report's History section: the run's summary line, the trend of the last runs, and the lists of what
    /// changed, each scenario linked in the page by its stable id. Open when there is something to say.
    /// </summary>
    public static string Section(HistoryVerdicts history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return Section(history, InPageLink, history.HasAnything);
    }

    /// <summary>
    /// The report's History section in its tab (5.0.0, plans/OPTION_C_PLAN.md S3): always open, since the tab is its
    /// disclosure, with its summary the section's heading, out of the tab order.
    /// </summary>
    internal static string InTab(HistoryVerdicts history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return Section(history, InPageLink, open: true, inTab: true);
    }

    /// <summary>
    /// The labs page's History section: always open, since the page is where history is read, and each scenario
    /// linked into the report by <c><paramref name="reportHref"/>#sid-&lt;id&gt;</c>, or named as text when no
    /// report is written beside the page (<paramref name="reportHref"/> null).
    /// </summary>
    public static string Section(HistoryVerdicts history, string? reportHref)
    {
        ArgumentNullException.ThrowIfNull(history);
        return Section(history, entry => ScenarioLink(entry.StableId, $"{HtmlEncode(entry.Feature)} &rsaquo; {HtmlEncode(entry.Name)}", reportHref), open: true);
    }

    /// <summary>
    /// A scenario named on the labs page: a link into the report by its stable id, the address the report resolves
    /// on load, or text when there is no report to link to. <paramref name="html"/> is already encoded.
    /// </summary>
    public static string ScenarioLink(string stableId, string html, string? reportHref) =>
        reportHref is null
            ? $"<span class=\"history-name\">{html}</span>"
            : $"<a class=\"history-link\" href=\"{HtmlEncode(Uri.EscapeDataString(reportHref))}#sid-{HtmlEncode(stableId)}\" title=\"Open this scenario in the report\">{html}</a>";

    // Inside the report: the hash handler opens the scenario, and the address bar takes its anchor.
    private static string InPageLink(ScenarioHistory entry)
    {
        var id = entry.StableId;
        return $"<a class=\"history-link\" href=\"#sid-{id}\" onclick=\"event.preventDefault();if(window.reveal_url_anchor){{reveal_url_anchor('sid-{id}');}}history.replaceState(null,'',location.pathname+location.search+'#sid-{id}');\" title=\"Go to this scenario on the Features tab\">{HtmlEncode(entry.Feature)} &rsaquo; {HtmlEncode(entry.Name)}</a>";
    }

    private static string Section(HistoryVerdicts history, Func<ScenarioHistory, string> link, bool open, bool inTab = false)
    {
        var sb = new StringBuilder();
        sb.Append($"<details id=\"history-section\" class=\"history-section\"{(open ? " open" : "")}>");
        sb.Append($"<summary class=\"h2\"{(inTab ? " tabindex=\"-1\"" : "")} title=\"{(inTab ? Description : DisclosureTitle)}\">History <span class=\"history-summary-line\">{HtmlEncode(HistorySummary.Line(history))}</span></summary>");
        sb.Append("<div class=\"history-body\">");
        sb.Append($"<p class=\"history-meta\">stream <code>{HtmlEncode(history.Stream)}</code> · run <code>{HtmlEncode(history.RunId)}</code> · {history.RunsRecorded.ToString(CultureInfo.InvariantCulture)} earlier run{(history.RunsRecorded == 1 ? "" : "s")} in the window");
        if (history.Partial)
            sb.Append(" · this run is partial, so nothing is reported absent from it");
        // Said once, here, and not on every scenario: measured in a contended run, a third of the rows
        // cleared any test a per-row note could be given.
        if (history.Runs.Count > 0 && history.Runs[^1] is { Degraded: true, Pace: { } pace })
            sb.Append(HtmlEncode($" · this run is degraded: its passing scenarios took {HistorySummary.Times(pace)} their usual, so no scenario is read slower in it"));
        sb.Append("</p>");
        if (history.ColdStart && history.ColdStartMessage is { } cold)
            sb.Append($"<p class=\"history-note\">{HtmlEncode(cold)}</p>");
        if (history.Compare is { } compare)
            sb.Append($"<p class=\"history-compare\">on <code>{HtmlEncode(compare.Stream)}</code>: {HtmlEncode(HistorySummary.Line(compare))}</p>");

        if (history.Runs.Count >= 2)
        {
            sb.Append("<div class=\"history-charts\">");
            sb.Append(PassRateChart(history.Runs));
            if (history.Runs.Any(r => r.DurationMs is not null))
                sb.Append(DurationChart(history.Runs));
            sb.Append("</div>");
        }

        var scenarios = history.Scenarios;
        List(sb, "New failures", scenarios.Where(IsNewFailure), link);
        List(sb, "Failing since", scenarios.Where(s => s.Primary is HistoryVerdictKind.Failing or HistoryVerdictKind.AlwaysFailing), link);
        List(sb, "Flaky", scenarios.Where(s => s.Has(HistoryVerdictKind.Flaky)), link);
        List(sb, "Newly fixed", scenarios.Where(s => s.Primary == HistoryVerdictKind.Fixed), link);
        List(sb, "Slower", scenarios.Where(s => s.Has(HistoryVerdictKind.Slower)), link);
        List(sb, "Behaviour changed", scenarios.Where(s => s.Has(HistoryVerdictKind.BehaviourChanged) || s.Has(HistoryVerdictKind.Reordered)), link);
        List(sb, "New scenarios", scenarios.Where(s => s.Has(HistoryVerdictKind.New) && !IsNewFailure(s)), link);
        List(sb, "Quarantined", scenarios.Where(s => s.Has(HistoryVerdictKind.Quarantined)), link);

        if (history.Absent.Count > 0)
        {
            sb.Append($"<div class=\"history-list\"><h4>Absent since the previous run <span class=\"history-count\">{history.Absent.Count.ToString(CultureInfo.InvariantCulture)}</span></h4><ul>");
            foreach (var absent in history.Absent.Take(ListCap))
                sb.Append($"<li>{HtmlEncode(absent.Feature)} &rsaquo; {HtmlEncode(absent.Name)} <span class=\"history-evidence\">last seen in {HtmlEncode(absent.LastRunId)}</span></li>");
            More(sb, history.Absent.Count);
            sb.Append("</ul></div>");
        }

        if (history.NewDependencies.Count > 0)
        {
            sb.Append($"<div class=\"history-list\"><h4>New dependencies <span class=\"history-count\">{history.NewDependencies.Count.ToString(CultureInfo.InvariantCulture)}</span></h4><ul>");
            foreach (var pair in history.NewDependencies.Take(ListCap))
                sb.Append($"<li><code>{HtmlEncode(pair)}</code> <span class=\"history-evidence\">no earlier run in the window made this call</span></li>");
            More(sb, history.NewDependencies.Count);
            sb.Append("</ul></div>");
        }

        sb.Append("</div></details>");
        return sb.ToString();
    }

    private static bool IsNewFailure(ScenarioHistory s) =>
        s.Current == HistoryFormat.Failed && !s.Has(HistoryVerdictKind.Flaky) && !s.Has(HistoryVerdictKind.Quarantined)
        && (s.Has(HistoryVerdictKind.Broke) || s.Has(HistoryVerdictKind.New) || s.Has(HistoryVerdictKind.Unknown));

    private static void List(StringBuilder sb, string heading, IEnumerable<ScenarioHistory> items, Func<ScenarioHistory, string> link)
    {
        var list = items.OrderBy(e => HistoryAnalyzer.Precedence(e.Primary)).ThenBy(e => e.Feature, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
        if (list.Count == 0)
            return;
        sb.Append($"<div class=\"history-list\"><h4>{heading} <span class=\"history-count\">{list.Count.ToString(CultureInfo.InvariantCulture)}</span></h4><ul>");
        foreach (var entry in list.Take(ListCap))
        {
            sb.Append("<li>").Append(link(entry));
            sb.Append(PillHtml(entry.Primary, entry.Evidence));
            sb.Append($" <span class=\"history-evidence\">{HtmlEncode(entry.Evidence)}</span> <code class=\"history-series\" title=\"the last runs, oldest first\">{entry.Series}</code></li>");
        }
        More(sb, list.Count);
        sb.Append("</ul></div>");
    }

    private static void More(StringBuilder sb, int count)
    {
        if (count > ListCap)
            sb.Append($"<li class=\"history-more\">… and {(count - ListCap).ToString(CultureInfo.InvariantCulture)} more</li>");
    }

    /// <summary>
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

    private static string PillHtml(HistoryVerdictKind kind, string title, string? suffix = null)
    {
        var name = HistoryVerdictNames.Name(kind);
        return $" <span class=\"history-verdict history-verdict-{name}\" title=\"{HtmlEncode(title)}\">{name}{suffix}</span>";
    }

    private static string Summary(ScenarioHistory entry) => $"{HistoryVerdictNames.Name(entry.Primary)}: {entry.Evidence}";

    private static string Tooltip(ScenarioHistory entry)
    {
        var sb = new StringBuilder();
        sb.Append("Last ").Append(entry.Series.Length.ToString(CultureInfo.InvariantCulture)).Append(" runs, oldest first: ").Append(entry.Series).Append('\n');
        sb.Append(Summary(entry));
        var points = entry.Points;
        var start = Math.Max(0, points.Count - entry.Series.Length);
        for (var i = start; i < points.Count; i++)
        {
            var p = points[i];
            sb.Append('\n').Append(p.RunId).Append(" · ").Append(p.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            if (p.Commit is { } commit)
                sb.Append(" · ").Append(commit.Length > 7 ? commit[..7] : commit);
            sb.Append(" · ").Append(ResultWord(p.Result));
            if (p.Attempt is > 1)
                sb.Append(" on attempt ").Append(p.Attempt.Value.ToString(CultureInfo.InvariantCulture));
            if (p.Partial)
                sb.Append(" · partial run");
            if (p.RunDegraded)
                sb.Append(" · degraded run");
            if (p.DurationMs is { } ms)
                sb.Append(" · ").Append(FormatMs(ms));
            // An error on a pass is the earlier attempt's, and is said to be: a retry overlaid on its run.
            if (p.Error is { } error)
                sb.Append(" · ").Append(p.Result == HistoryFormat.Passed ? "an earlier attempt failed: " : "").Append(error.Length > 80 ? error[..80] + "…" : error);
        }
        return sb.ToString();
    }

    private static string FormatMs(long? ms) =>
        ms is null ? "no duration" : ms < 1000 ? $"{ms.Value.ToString(CultureInfo.InvariantCulture)} ms" : $"{(ms.Value / 1000.0).ToString("0.##", CultureInfo.InvariantCulture)} s";

    private static string ResultWord(char result) => result switch
    {
        HistoryFormat.Passed => "passed",
        HistoryFormat.Failed => "failed",
        HistoryFormat.Skipped => "skipped",
        HistoryFormat.SkippedAfterFailure => "skipped after failure",
        HistoryFormat.Absent => "absent",
        HistoryFormat.Unknown => "no verdict",
        HistoryFormat.NotATest => "not a test",
        _ => "bypassed"
    };

    /// <summary>The colours the timeline bars use, so the sparkline reads like the rest of the report.</summary>
    private static string Colour(char result) => result switch
    {
        HistoryFormat.Passed => "var(--sp)",
        HistoryFormat.Failed => "var(--sf)",
        HistoryFormat.Skipped or HistoryFormat.SkippedAfterFailure => "var(--ss)",
        HistoryFormat.Absent => "var(--sa)",
        HistoryFormat.Unknown => "var(--su)",
        HistoryFormat.NotATest => "var(--sn)",
        _ => "var(--so)"
    };
}
