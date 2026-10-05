using System.Globalization;
using System.Text;
using Kronikol.History;
using static System.Net.WebUtility;

namespace Kronikol.Reports;

/// <summary>
/// The labs page, <c>{HtmlTestRunReportFileName}.labs.html</c> beside the report: the cross-run history views and
/// the run's report diagnostics, which from 4.6.0 a default <c>TestRunReport.html</c> no longer carries while their
/// design is worked out (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md). It holds the History section, every
/// scenario's sparkline and verdict, and the diagnostics list, drawn by the renderers a report's opt-ins use
/// (<see cref="HistoryHtml"/>, <see cref="ReportGenerator.RenderReportDiagnostics"/>), so the two cannot drift.
/// </summary>
/// <remarks>
/// Built only from what a run, <c>kronikol merge</c> and <c>kronikol ingest</c> all have: the history verdicts and
/// the diagnostic entries. It carries no script: its scenario names link into the report by the <c>#sid-</c> anchor
/// the report resolves on load, and are text when no report is written beside it.
/// </remarks>
internal static class LabsReportGenerator
{
    /// <summary>What follows the report's name in the page's file name.</summary>
    internal const string Suffix = ".labs.html";

    /// <summary>The page beside a report whose HTML is <c>{reportFileName}.html</c>: <c>TestRunReport.labs.html</c>.</summary>
    public static string FileName(string reportFileName) => reportFileName + Suffix;

    /// <summary>Whether the page has anything to show: history that was read, or a diagnostic the run recorded.</summary>
    public static bool HasContent(HistoryVerdicts? history, IReadOnlyCollection<DiagnosticEntry> diagnostics) =>
        history is not null || diagnostics.Count > 0;

    /// <summary>The page's HTML.</summary>
    public static string Build(LabsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var title = HtmlEncode(page.Title);
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n<html>\n<head>\n");
        html.Append("<meta charset=\"utf-8\" />\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
        html.Append("<meta name=\"generator\" content=\"Kronikol v").Append(HtmlEncode(ReportGenerator.KronikolVersion)).Append("\" />\n");
        html.Append("<title>Labs · ").Append(title).Append("</title>\n");
        html.Append("<style>\n")
            .Append(Stylesheets.HtmlReportStyleSheet).Append('\n')
            .Append(Stylesheets.HistoryStyleSheet).Append('\n')
            .Append(Stylesheets.ReportDiagnosticsStyleSheet).Append('\n')
            .Append(Stylesheets.LabsStyleSheet)
            .Append("</style>\n");
        if (page.CustomCss is not null)
            html.Append("<style>").Append(page.CustomCss).Append("</style>\n");
        html.Append("<link rel=\"icon\" href=\"").Append(page.CustomFaviconBase64 ?? Constants.DefaultFavicon.DataUri).Append("\">\n");
        html.Append("</head>\n<body>\n");
        if (page.CustomLogoHtml is not null)
            html.Append("<div class=\"custom-logo\">").Append(page.CustomLogoHtml).Append("</div>\n");

        html.Append("<header class=\"labs-header\">");
        html.Append("<h1>").Append(title).Append(" <span class=\"labs-badge\">Labs</span></h1>");
        html.Append("<p class=\"labs-about\">Labs: views still being designed. Their layout may change in any release.</p>");
        html.Append("<p class=\"labs-meta\">").Append(string.Join(" · ", Meta(page))).Append("</p>");
        html.Append("</header>\n");

        if (page.History is { } history)
        {
            html.Append(HistoryHtml.Section(history, page.ReportHref)).Append('\n');
            html.Append(ScenarioTable(history, page.Features, page.Suite, page.ReportHref)).Append('\n');
        }

        if (page.Diagnostics.Count > 0)
            html.Append(ReportGenerator.RenderReportDiagnostics(page.Diagnostics, open: true)).Append('\n');

        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    private static IEnumerable<string> Meta(LabsPage page)
    {
        if (!string.IsNullOrEmpty(page.Suite))
            yield return $"suite <code>{HtmlEncode(page.Suite)}</code>";
        if (!string.IsNullOrEmpty(page.RunId))
            yield return $"run <code>{HtmlEncode(page.RunId)}</code>";
        if (page.EndedAt is { } ended)
            yield return $"ended {ended.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC";
        if (page.ReportHref is { } report)
            yield return $"<a class=\"labs-back\" href=\"{HtmlEncode(Uri.EscapeDataString(report))}\">Open the report</a>";
    }

    /// <summary>
    /// Every scenario's history, one row each in report order (a feature, then its scenarios), collapsed: the
    /// sparkline, pill and evidence a report's opt-in draws in each scenario header. An id the run holds twice
    /// gives each of its scenarios its own entry, by the slot rule the report follows.
    /// </summary>
    private static string ScenarioTable(HistoryVerdicts history, Feature[] features, string? suite, string? reportHref)
    {
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new StringBuilder();
        var count = 0;
        foreach (var feature in features)
        {
            var scenarios = feature.Scenarios ?? [];
            if (scenarios.Length == 0)
                continue;
            rows.Append("<tr class=\"labs-feature\"><th colspan=\"4\">").Append(HtmlEncode(feature.DisplayName)).Append("</th></tr>");
            foreach (var scenario in scenarios)
            {
                count++;
                var stableId = ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues);
                var entry = HistoryHtml.Entry(history, slots, stableId);
                rows.Append("<tr class=\"labs-row\"><td>").Append(HistoryHtml.ScenarioLink(stableId, HtmlEncode(scenario.DisplayName), reportHref)).Append("</td>");
                rows.Append("<td>").Append(entry is null ? "" : HistoryHtml.Sparkline(entry)).Append("</td>");
                rows.Append("<td>").Append(entry is null ? "" : Verdict(entry)).Append("</td>");
                rows.Append("<td class=\"history-evidence\">").Append(entry is null ? "" : HtmlEncode(entry.Evidence)).Append("</td></tr>");
            }
        }

        return "<details class=\"labs-scenarios\"><summary class=\"h2\">Every scenario <span class=\"history-count\">"
               + count.ToString(CultureInfo.InvariantCulture) + "</span></summary>"
               + "<div class=\"labs-table-wrap\"><table class=\"labs-scenario-table\"><thead><tr><th>Scenario</th><th>Last runs</th><th>Verdict</th><th>Evidence</th></tr></thead>"
               + "<tbody>" + rows + "</tbody></table></div></details>";
    }

    private static string Verdict(ScenarioHistory entry)
    {
        var pills = HistoryHtml.Pill(entry);
        return pills.Length > 0 ? pills : "<span class=\"labs-stable\">stable</span>";
    }
}

/// <summary>What the labs page is drawn from.</summary>
/// <param name="Title">The report's title; the page names itself after it.</param>
/// <param name="Features">The run's features, for the table of every scenario.</param>
/// <param name="Suite">The suite every stable id was computed with.</param>
/// <param name="History">The history read for the run, or null when none was (or the run did not embed it).</param>
/// <param name="Diagnostics">The run's report diagnostics.</param>
/// <param name="ReportHref">The report's file name beside the page, for the links into it; null when no report is
/// written, and the scenario names are text.</param>
/// <param name="RunId">The run's id, when it has one.</param>
/// <param name="EndedAt">When the run ended, when that is known.</param>
/// <param name="CustomCss">The user's stylesheet, as the report carries it.</param>
/// <param name="CustomFaviconBase64">The user's favicon, as the report carries it.</param>
/// <param name="CustomLogoHtml">The user's logo, as the report carries it.</param>
internal sealed record LabsPage(
    string Title,
    Feature[] Features,
    string? Suite,
    HistoryVerdicts? History,
    IReadOnlyList<DiagnosticEntry> Diagnostics,
    string? ReportHref,
    string? RunId = null,
    DateTimeOffset? EndedAt = null,
    string? CustomCss = null,
    string? CustomFaviconBase64 = null,
    string? CustomLogoHtml = null);
