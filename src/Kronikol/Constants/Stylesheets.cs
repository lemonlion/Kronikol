using System.Reflection;

namespace Kronikol;

/// <summary>
/// Provides embedded CSS stylesheets used in generated HTML reports and diagrams.
/// </summary>
public class Stylesheets
{
    private static readonly Lazy<string> HtmlReportStyleSheetLazy = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("stylesheets.css", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded resource stylesheets.css not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>The main HTML-report stylesheet, externalized to <c>Reports/stylesheets.css</c> so the
    /// exact bytes can be shared with the Kronikol4J port (JAVA_PORT_PLAN section 4.2).</summary>
    public static string HtmlReportStyleSheet => HtmlReportStyleSheetLazy.Value;

    /// <summary>The violet overlay that is the default <see cref="ReportConfigurationOptions.HtmlSpecificationsCustomStyleSheet"/>:
    /// it recolours <see cref="HtmlReportStyleSheet"/> and the component sheets rather than replacing them, and is
    /// emitted after them. Its idle hover rules come before its active rules, which must win over them.</summary>
    public const string VioletThemeStyleSheet =
        """
                .feature { background-color: #DDD6FE; }
                .features-summary-details { background-color: #DDD6FE; }
                .test-execution-summary { background-color: #DDD6FE; }
                .ci-metadata { background-color: #DDD6FE; }
                .filtering-box { background-color: #DDD6FE; }
                .example-diagrams { border-color: #DDD6FE; }

                /* Idle hovers first: an active rule of the same specificity must come after them. */
                .happy-path-toggle:hover,
                .dependency-toggle:hover,
                .status-toggle:hover,
                .category-toggle:hover {
                    background: #EDE9FE;
                    border-color: #A78BFA;
                }

                .export-btn:hover, .collapse-expand-all:hover, .percentile-btn:hover, .timeline-toggle:hover,
                .details-radio-btn:hover, .scenario-diagram-controls-toggle:hover { background: #EDE9FE; border-color: #A78BFA; }
                .diagram-toggle-btn:hover, .iflow-toggle-btn:hover { background: #EDE9FE; }
                .dep-mode-toggle:hover, .cat-mode-toggle:hover { background: #EDE9FE; border-color: #A78BFA; }
                .iflow-rel-summary-table tr:hover td { background: #EDE9FE; }
                .scenario-link:hover, .copy-scenario-name:hover { background: #EDE9FE; }

                .happy-path-toggle.happy-path-active,
                .dependency-toggle.dependency-active,
                .status-toggle.status-active,
                .category-toggle.category-active {
                    background: #8B5CF6;
                    color: white;
                    border-color: #8B5CF6;
                }
                .percentile-btn.percentile-active {
                    background: #8B5CF6;
                    color: white;
                    border-color: #8B5CF6;
                }
                .timeline-toggle-active { background: #8B5CF6; color: white; border-color: #8B5CF6; }
                .timeline-toggle-active:hover { background: #7C3AED; }

                .dep-mode-toggle, .cat-mode-toggle { background: #F5F3FF; }
                .scenario-focused { outline-color: #8B5CF6; }
                .step-attachment { color: #8B5CF6; }
                .attachment-image { border-color: #A78BFA; }
                .attachment-image-name { color: #A78BFA; }

                .details-radio-btn.details-active {
                    background: #8B5CF6;
                    color: white;
                    border-color: #8B5CF6;
                }

                .iflow-toggle-active { background: #8B5CF6; color: #fff; border-color: #8B5CF6; }
                .iflow-toggle-active:hover { background: #7C3AED; }
                .diagram-toggle-active { background: #8B5CF6; color: #fff; border-color: #8B5CF6; }
                .diagram-toggle-active:hover { background: #7C3AED; }
                .iflow-rel-list li:hover { background: #EDE9FE; border-color: #8B5CF6; }

                .step-status.passed { background: #8B5CF6; }
                .step-status.bypassed { background: #DDD6FE; color: #5B21B6; }
                .step-status.passed-bypassed { background: #DDD6FE; color: #5B21B6; }

                .rule { border-left-color: #8B5CF6; }
                span.label { background-color: #C4B5FD; }
                #searchbar { border-color: #C4B5FD; }
                .search-help-toggle { border-color: #C4B5FD; color: #C4B5FD; }
                .search-help-toggle:hover { background: #3B2F63; color: #E9D5FF; }
                .search-help-panel { border-color: #5B21B6; background: #1E1534; }
                .search-help-table th { border-bottom-color: #5B21B6; }
                .search-help-table td { border-bottom-color: #2E2048; }
                .search-help-table code { }
                .search-help-note { color: #A78BFA; }
                .search-help-note kbd { border-color: #5B21B6; }
                .sub-steps { border-left-color: #DDD6FE; }
                .feature-summary-table th { background: #F5F3FF; }
                .param-success { background: #EDE9FE; }
                .param-test-table tbody tr.row-search-match { box-shadow: inset 4px 0 0 #8B5CF6; }
                .duration-fast { background: #EDE9FE; color: #5B21B6; }
                @media (max-width: 768px) {
                    .filter-search { background: #DDD6FE; }
                }
                .back-to-top { background: #8B5CF6; }
                .back-to-top:hover { background: #7C3AED; }
        """;
}