using System.Reflection;

namespace Kronikol;

/// <summary>
/// Provides embedded CSS stylesheets used in generated HTML reports and diagrams.
/// </summary>
public class Stylesheets
{
    private static readonly Lazy<string> ReportTokensStyleSheetLazy = new(() => Load("report-tokens.css"));
    private static readonly Lazy<string> HtmlReportStyleSheetLazy = new(() => Load("stylesheets.css"));
    private static readonly Lazy<string> HistoryStyleSheetLazy = new(() => Load("history-styles.css"));
    private static readonly Lazy<string> ReportDiagnosticsStyleSheetLazy = new(() => Load("report-diagnostics-styles.css"));
    private static readonly Lazy<string> LabsStyleSheetLazy = new(() => Load("labs-styles.css"));
    private static readonly Lazy<string> WarmUpStyleSheetLazy = new(() => Load("warm-up-styles.css"));
    private static readonly Lazy<string> SearchStyleSheetLazy = new(() => Load("search-styles.css"));

    private static string Load(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource {fileName} not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The main HTML-report stylesheet, externalized to <c>Reports/stylesheets.css</c> so the
    /// exact bytes can be shared with the Kronikol4J port (JAVA_PORT_PLAN section 4.2). Since 4.6.0 it holds no
    /// rules for the history views or the report diagnostics section, which have sheets of their own that a page
    /// carries only when it draws them.</summary>
    public static string HtmlReportStyleSheet => HtmlReportStyleSheetLazy.Value;

    /// <summary>The report's colours and type as custom properties, light and dark (5.0.0, plans/OPTION_C_PLAN.md S1). It
    /// opens a report's styles and the labs page's (S13), so every sheet after it, a theme's and a
    /// <see cref="ReportConfigurationOptions.CustomCss"/> sheet included, can use the tokens or set them again.</summary>
    internal static string ReportTokensStyleSheet => ReportTokensStyleSheetLazy.Value;

    /// <summary>The rules of the cross-run history views: the per-scenario sparkline and verdict pill, and the History
    /// section. Carried by a report that draws them and by the labs page.</summary>
    internal static string HistoryStyleSheet => HistoryStyleSheetLazy.Value;

    /// <summary>The rules of the Report diagnostics section. Carried by a report that draws it and by the labs page.</summary>
    internal static string ReportDiagnosticsStyleSheet => ReportDiagnosticsStyleSheetLazy.Value;

    /// <summary>The labs page's own rules: its header and its table of every scenario's history.</summary>
    internal static string LabsStyleSheet => LabsStyleSheetLazy.Value;

    /// <summary>The rules of the first-call warm-up's marks: the muted text in a duration badge and the shaded share of a
    /// timeline bar (plans/WARM_UP_PLAN.md R2). Carried only by a page that draws a mark.</summary>
    internal static string WarmUpStyleSheet => WarmUpStyleSheetLazy.Value;

    /// <summary>The rules of the search (plans/SEARCH_PLAN.md 4.2 to 4.6): the button on the title line, the query
    /// builder, the Search tab and the marks of a match, in the report's tokens only. Carried by every page with the
    /// search.</summary>
    internal static string SearchStyleSheet => SearchStyleSheetLazy.Value;

    /// <summary>The violet theme, the default <see cref="ReportConfigurationOptions.HtmlSpecificationsCustomStyleSheet"/>
    /// (5.0.0, plans/OPTION_C_PLAN.md S13): it sets the report's colour tokens again, light and dark, in the blocks the
    /// token sheet uses, and paints in variables of its own what no token carries: the violet cards, and the solid violet
    /// of a chosen control, kept from 4.x under the ink made for it. It is emitted after the built-in sheets, and its hover
    /// edges come before its chosen states, which must win over them.</summary>
    public const string VioletThemeStyleSheet =
        """
                /* The violet theme (5.0.0, plans/OPTION_C_PLAN.md S13). The report's tokens, light on :root and dark on
                   :root[data-theme="dark"] and again under prefers-color-scheme, as the token sheet has them: each dark value
                   is the token sheet's own turned to the theme's hue at the same lightness, and the two dark blocks must stay
                   identical. Then the theme's own variables, for what no token carries. */
                :root {
                    --kron-accent: #6d28d9;
                    --kron-accent-rail: rgba(109, 40, 217, .18);
                    --kron-hover: #ede9fe;
                    --kron-selected: #c4b5fd;
                    --kron-selected-ink: #5b21b6;
                    --kron-selected-edge: #8b5cf6;
                    --kron-field: #f5f3ff;
                    --kron-strip: #f5f3ff;
                    --kron-pass: #5b21b6;
                    --kron-pass-soft: #ede9fe;
                    --kron-bypass: #5b21b6;
                    --kron-bypass-soft: #ddd6fe;
                    --violet-card: #ddd6fe;
                    --violet-rail: #a78bfa;
                    --violet-edge: #a78bfa;
                    --violet-solid: #8b5cf6;
                    --violet-solid-hover: #7c3aed;
                }
                :root[data-theme="dark"] {
                    --kron-accent: #a998eb;
                    --kron-accent-rail: rgba(169, 152, 235, .30);
                    --kron-hover: #2b2837;
                    --kron-selected: #342b51;
                    --kron-selected-ink: #cac0f6;
                    --kron-selected-edge: #8774c8;
                    --kron-field: #272431;
                    --kron-strip: #22202b;
                    --kron-pass: #b7a3ff;
                    --kron-pass-soft: #2a253e;
                    --kron-bypass: #b3a4f0;
                    --kron-bypass-soft: #2c2543;
                    --violet-card: #1d1b25;
                    --violet-rail: #3e3b4c;
                    --violet-edge: #a78bfa;
                    --violet-solid: #7c3aed;
                    --violet-solid-hover: #6d28d9;
                }
                @media (prefers-color-scheme: dark) {
                    :root:not([data-theme]) {
                        --kron-accent: #a998eb;
                        --kron-accent-rail: rgba(169, 152, 235, .30);
                        --kron-hover: #2b2837;
                        --kron-selected: #342b51;
                        --kron-selected-ink: #cac0f6;
                        --kron-selected-edge: #8774c8;
                        --kron-field: #272431;
                        --kron-strip: #22202b;
                        --kron-pass: #b7a3ff;
                        --kron-pass-soft: #2a253e;
                        --kron-bypass: #b3a4f0;
                        --kron-bypass-soft: #2c2543;
                        --violet-card: #1d1b25;
                        --violet-rail: #3e3b4c;
                        --violet-edge: #a78bfa;
                        --violet-solid: #7c3aed;
                        --violet-solid-hover: #6d28d9;
                    }
                }

                .feature, .features-table-view, .filtering-box, .report-nav > .filtering-box { background-color: var(--violet-card); }
                .sub-steps { border-left-color: var(--violet-rail); }
                .attachment-image { border-color: var(--violet-rail); }
                .attachment-image-name { color: var(--kron-selected-ink); }
                span.label { background-color: var(--kron-selected); border-color: var(--kron-selected); color: var(--kron-selected-ink); }
                #searchbar { border-color: var(--kron-selected-edge); }

                /* Hover edges first: a chosen state of the same specificity must come after them. A hover's tint is the
                   base sheet's, from --kron-hover. */
                .happy-seg:hover,
                .tag-toggle:hover,
                .dependency-toggle:hover,
                .status-toggle:hover,
                .tag-more:hover,
                .export-btn:hover, .fold-btn:hover, .percentile-btn:hover, .details-radio-btn:hover,
                .scenario-diagram-controls-toggle:hover, .tag-mode-toggle:hover, .dep-mode-toggle:hover,
                .tag-ways-toggle:hover, .dep-ways-toggle:hover {
                    border-color: var(--violet-edge);
                }
                .iflow-rel-list li:hover { border-color: var(--violet-solid); }

                .happy-seg[aria-pressed="true"],
                .tag-toggle.tag-active,
                .dependency-toggle.dependency-active,
                .status-toggle.status-active,
                .percentile-btn.percentile-active,
                .details-radio-btn.details-active,
                .tag-ways-toggle[data-ways="3"], .dep-ways-toggle[data-ways="3"] {
                    background: var(--violet-solid);
                    color: var(--kron-on-solid);
                    border-color: var(--violet-solid);
                }
                .details-radio-btn.toggle-btn.details-active::before { background: var(--kron-on-solid); border-color: var(--kron-on-solid); }
                .iflow-toggle-active, .diagram-toggle-active { background: var(--violet-solid); color: var(--kron-on-solid); border-color: var(--violet-solid); }
                .iflow-toggle-active:hover, .diagram-toggle-active:hover { background: var(--violet-solid-hover); color: var(--kron-on-solid); }

                .step-status.passed { background: var(--violet-solid); color: var(--kron-on-solid); }
                .param-test-table tbody tr.row-search-match { box-shadow: inset 4px 0 0 var(--violet-solid); }
                .back-to-top { background: var(--violet-solid); border-color: var(--violet-solid); color: var(--kron-on-solid); }
                .back-to-top:hover { background: var(--violet-solid-hover); }
        """;
}