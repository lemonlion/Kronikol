using System.Globalization;
using System.Text;
using static System.Net.WebUtility;

namespace Kronikol.History;

/// <summary>
/// Renders cross-run history into the HTML report (plans/CROSS_RUN_HISTORY_PLAN.md §8.1): per scenario a
/// verdict attribute the search box filters on (<c>$flaky</c>, <c>$broke</c>), a sparkline of the last
/// runs beside the duration badge, and a verdict pill; and one History section beside the timeline with
/// the run trend and the lists of what changed.
///
/// <para>Everything is rendered here, on the generation side, and the sparkline is ONE element whose
/// background gradient carries a hard colour stop per run: two hundred scenarios cost two hundred nodes,
/// not the two thousand an inline SVG per scenario would, and the report's filter and toggle budgets
/// (which walk scenario elements) stay where they are. The section links each scenario by the same
/// <c>#sid-</c> anchor <c>kronikol query</c> and <c>Failures.md</c> print. Sparklines live inside the
/// scenario elements, so the filtered-HTML export carries them; the section lives beside the timeline,
/// which the export leaves behind on purpose.</para>
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
    /// The History section: the run's summary line, the trend of the last runs, and the lists of what
    /// changed, each scenario linked by its stable id. Open when there is something to say.
    /// </summary>
    public static string Section(HistoryVerdicts history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var sb = new StringBuilder();
        var open = history.HasAnything;
        sb.Append($"<details id=\"history-section\" class=\"history-section\"{(open ? " open" : "")}>");
        sb.Append($"<summary class=\"h2\">History <span class=\"history-summary-line\">{HtmlEncode(HistorySummary.Line(history))}</span></summary>");
        sb.Append("<div class=\"history-body\">");
        sb.Append($"<p class=\"history-meta\">stream <code>{HtmlEncode(history.Stream)}</code> · run <code>{HtmlEncode(history.RunId)}</code> · {history.RunsRecorded.ToString(CultureInfo.InvariantCulture)} earlier run{(history.RunsRecorded == 1 ? "" : "s")} in the window");
        if (history.Partial)
            sb.Append(" · this run is partial, so nothing is reported absent from it");
        sb.Append("</p>");
        if (history.ColdStart && history.ColdStartMessage is { } cold)
            sb.Append($"<p class=\"history-note\">{HtmlEncode(cold)}</p>");

        if (history.Runs.Count >= 2)
        {
            sb.Append("<div class=\"history-charts\">");
            sb.Append(PassRateChart(history.Runs));
            if (history.Runs.Any(r => r.DurationMs is not null))
                sb.Append(DurationChart(history.Runs));
            sb.Append("</div>");
        }

        var scenarios = history.Scenarios;
        List(sb, "New failures", scenarios.Where(IsNewFailure));
        List(sb, "Failing since", scenarios.Where(s => s.Primary is HistoryVerdictKind.Failing or HistoryVerdictKind.AlwaysFailing));
        List(sb, "Flaky", scenarios.Where(s => s.Has(HistoryVerdictKind.Flaky)));
        List(sb, "Newly fixed", scenarios.Where(s => s.Primary == HistoryVerdictKind.Fixed));
        List(sb, "Slower", scenarios.Where(s => s.Has(HistoryVerdictKind.Slower)));
        List(sb, "Behaviour changed", scenarios.Where(s => s.Has(HistoryVerdictKind.BehaviourChanged) || s.Has(HistoryVerdictKind.Reordered)));
        List(sb, "New scenarios", scenarios.Where(s => s.Has(HistoryVerdictKind.New) && !IsNewFailure(s)));
        List(sb, "Quarantined", scenarios.Where(s => s.Has(HistoryVerdictKind.Quarantined)));

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

    private static void List(StringBuilder sb, string heading, IEnumerable<ScenarioHistory> items)
    {
        var list = items.OrderBy(e => HistoryAnalyzer.Precedence(e.Primary)).ThenBy(e => e.Feature, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
        if (list.Count == 0)
            return;
        sb.Append($"<div class=\"history-list\"><h4>{heading} <span class=\"history-count\">{list.Count.ToString(CultureInfo.InvariantCulture)}</span></h4><ul>");
        foreach (var entry in list.Take(ListCap))
        {
            var id = entry.StableId;
            sb.Append($"<li><a class=\"history-link\" href=\"#sid-{id}\" onclick=\"event.preventDefault();if(window.reveal_url_anchor){{reveal_url_anchor('sid-{id}');}}history.replaceState(null,'',location.pathname+location.search+'#sid-{id}');\">{HtmlEncode(entry.Feature)} &rsaquo; {HtmlEncode(entry.Name)}</a>");
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

    private static string PassRateChart(IReadOnlyList<RunPoint> runs) =>
        Chart("Pass rate, oldest run first", runs,
            r => r.Passed + r.Failed == 0 ? 0 : (double)r.Passed / (r.Passed + r.Failed),
            1.0,
            r => r.Failed == 0 ? "history-bar-pass" : "history-bar-fail",
            r => $"{r.RunId} · {r.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}{(r.Commit is { } c ? " · " + (c.Length > 7 ? c[..7] : c) : "")} · {r.Passed.ToString(CultureInfo.InvariantCulture)} passed, {r.Failed.ToString(CultureInfo.InvariantCulture)} failed of {r.Total.ToString(CultureInfo.InvariantCulture)}{(r.Partial ? " · partial" : "")}");

    private static string DurationChart(IReadOnlyList<RunPoint> runs)
    {
        var max = runs.Max(r => r.DurationMs ?? 0);
        return Chart("Total duration, oldest run first", runs,
            r => r.DurationMs ?? 0,
            max == 0 ? 1 : max,
            _ => "history-bar-duration",
            r => $"{r.RunId} · {r.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {FormatMs(r.DurationMs)}{(r.Partial ? " · partial" : "")}");
    }

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
            if (p.DurationMs is { } ms)
                sb.Append(" · ").Append(FormatMs(ms));
            if (p.Error is { } error)
                sb.Append(" · ").Append(error.Length > 80 ? error[..80] + "…" : error);
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
        _ => "bypassed"
    };

    /// <summary>The colours the timeline bars use, so the sparkline reads like the rest of the report.</summary>
    private static string Colour(char result) => result switch
    {
        HistoryFormat.Passed => "#228b22",
        HistoryFormat.Failed => "#bf0000",
        HistoryFormat.Skipped or HistoryFormat.SkippedAfterFailure => "#949494",
        HistoryFormat.Absent => "#e6e6e6",
        HistoryFormat.Unknown => "#c8c8c8",
        _ => "#b8a000"
    };
}
