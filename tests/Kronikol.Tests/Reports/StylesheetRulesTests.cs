using System.Globalization;
using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Where each toolbar and width rule lives, and in what order the violet theme states its hovers
/// (plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.md §3, §5 T1, T2, T4). The E2E sweep proves what these rules
/// do at every width; these facts pin the rules themselves, including the ones no Chromium viewport
/// can reach (a fractional width between 768 and 769 px).
/// </summary>
public partial class StylesheetRulesTests
{
    private static readonly string Base = Stylesheets.HtmlReportStyleSheet;
    private static readonly string Popup = DiagramContextMenu.GetInternalFlowPopupStyles();
    private static readonly string Violet = Stylesheets.VioletThemeStyleSheet;

    private const string Band = "(min-width: 768.02px) and (max-width: 1160px)";
    private const string RowLayout = "(min-width: 768.02px)";

    private static readonly (string Name, string Css)[] AllSheets =
    [
        ("stylesheets.css", Base),
        ("internal-flow-popup-styles.css", Popup),
        ("collapsible-notes-styles.css", DiagramContextMenu.GetCollapsibleNotesStyles()),
        ("context-menu-styles.css", DiagramContextMenu.GetStyles()),
        ("inline-svg-styles.css", DiagramContextMenu.GetInlineSvgStyles()),
        ("VioletThemeStyleSheet", Violet),
        // Split out of stylesheets.css in 4.6.0: carried only by a page that draws what they style.
        ("history-styles.css", Stylesheets.HistoryStyleSheet),
        ("report-diagnostics-styles.css", Stylesheets.ReportDiagnosticsStyleSheet),
        ("labs-styles.css", Stylesheets.LabsStyleSheet),
    ];

    // ── T1: the widths (S1, S1b) ──

    /// <summary>An export button is never squeezed, so its label stays on one line wherever the row has room
    /// for it; a button wider than the whole row (a letter-spaced "Export Filtered HTML" in the narrow
    /// in-row filtering box just above the breakpoint) is capped at the row and wraps its label, where
    /// <c>white-space: nowrap</c> held it whole and pushed the export buttons out of the box.</summary>
    [Fact]
    public void An_export_button_is_never_squeezed_and_never_wider_than_its_row()
    {
        Assert.Equal("0", CssRules.Value(Base, ".export-btn", "flex-shrink"));
        Assert.Equal("100%", CssRules.Value(Base, ".export-btn", "max-width"));
        Assert.Null(CssRules.Value(Base, ".export-btn", "white-space"));
    }

    [Theory]
    [InlineData(".filtering-box-export")]
    [InlineData(".filtering-box-header")]
    [InlineData(".toolbar-left")]
    public void Rows_that_would_squeeze_their_buttons_wrap_them_whole_instead(string selector) =>
        Assert.Equal("wrap", CssRules.Value(Base, selector, "flex-wrap"));

    /// <summary>5.0.0 (plans/OPTION_C_PLAN.md S2): the filter box has a row of its own at every width, after the run
    /// band, so the 4.x header row and its band rules are gone.</summary>
    [Fact]
    public void The_header_row_is_gone_with_its_rules()
    {
        Assert.Equal(-1, CssRules.IndexOf(Base, ".header-row"));
        Assert.Null(CssRules.Value(Base, ".filtering-box", "flex-basis", Band));
    }

    /// <summary>The run band's values break where they must and its zones shrink, so a long branch or repository never
    /// widens the page (what the 4.x CI box's cap did); its labels are single words.</summary>
    [Fact]
    public void The_run_band_breaks_its_values_and_lets_its_zones_shrink()
    {
        Assert.Equal("anywhere", CssRules.Value(Base, ".run-stat dd", "overflow-wrap"));
        Assert.Equal("0", CssRules.Value(Base, ".run-zone", "min-width"));
        Assert.Equal("wrap", CssRules.Value(Base, ".run-stats", "flex-wrap"));
        Assert.Equal(-1, CssRules.IndexOf(Base, ".ci-metadata"));
    }

    /// <summary>A viewport can be 768.8 CSS px wide (Firefox at 125 %): a lower bound anywhere from
    /// 768 to 769 px other than 768.02 leaves it in no block, or in two (plan §2.12).</summary>
    [Fact]
    public void No_min_width_bound_leaves_a_gap_or_an_overlap_at_the_phone_breakpoint()
    {
        foreach (var (name, css) in AllSheets)
        foreach (var media in CssRules.MediaQueries(css))
        foreach (Match m in MinWidth().Matches(media))
        {
            var bound = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.False(bound is >= 768 and <= 769 && bound != 768.02,
                $"{name}: '@media {media}' bounds the row layout at {bound}px; write 768.02px");
        }
    }

    [GeneratedRegex(@"min-width:\s*([\d.]+)px")]
    private static partial Regex MinWidth();

    // ── T2: the scenario toolbar's base rules live in the base sheet (S2) ──

    private static readonly string[] ToolbarSelectors =
    [
        ".diagram-toggle", ".diagram-toggle-btn", ".diagram-toggle-btn:hover",
        ".diagram-toggle-active", ".diagram-toggle-active:hover", ".diagram-toggle-spacer",
    ];

    [Fact]
    public void The_scenario_toolbar_is_a_wrapping_flex_row_in_the_base_sheet()
    {
        Assert.Equal("flex", CssRules.Value(Base, ".diagram-toggle", "display"));
        Assert.Equal("wrap", CssRules.Value(Base, ".diagram-toggle", "flex-wrap"));
        // 5.0.0 (plans/OPTION_C_PLAN.md S1): a length, since 1em became 14px with the page's type
        Assert.Equal("16px", CssRules.Value(Base, ".diagram-toggle", "padding-left"));
        Assert.Equal("16px", CssRules.Value(Base, ".diagram-toggle", "padding-right"));
    }

    [Fact]
    public void Each_scenario_toolbar_rule_lives_in_the_base_sheet_and_nowhere_else_at_top_level()
    {
        foreach (var selector in ToolbarSelectors)
        {
            Assert.True(CssRules.For(Base, selector).Count > 0, $"stylesheets.css has no top-level {selector} rule");
            foreach (var (name, css) in AllSheets.Where(s => s.Css != Base && s.Css != Violet))
                Assert.Empty(CssRules.For(css, selector));
        }
    }

    [Fact]
    public void The_internal_flow_popup_sheet_holds_no_scenario_toolbar_rule_at_all() =>
        Assert.DoesNotContain(CssRules.Parse(Popup), r => r.Selectors.Any(s => s.Contains(".diagram-toggle")));

    // ── T4: the violet theme's hovers come before its active states (S4) ──

    private static readonly string[] FilterToggles = ["happy-path", "dependency", "status", "category"];

    [Fact]
    public void A_violet_filter_toggle_keeps_its_active_colour_while_hovered()
    {
        foreach (var toggle in FilterToggles)
        {
            var hover = CssRules.IndexOf(Violet, $".{toggle}-toggle:hover");
            var active = CssRules.IndexOf(Violet, $".{toggle}-toggle.{toggle}-active");
            Assert.True(hover >= 0 && active >= 0, $"the violet theme lacks the {toggle} hover or active rule");
            Assert.True(hover < active, $".{toggle}-toggle:hover must precede .{toggle}-toggle.{toggle}-active (same specificity: the later wins)");
        }
    }

    private static readonly string[] IdleHovers =
    [
        ".happy-path-toggle:hover", ".dependency-toggle:hover", ".status-toggle:hover", ".category-toggle:hover",
        ".export-btn:hover", ".fold-btn:hover", ".percentile-btn:hover", ".timeline-toggle:hover",
        ".details-radio-btn:hover", ".scenario-diagram-controls-toggle:hover", ".diagram-toggle-btn:hover",
        ".iflow-toggle-btn:hover", ".dep-mode-toggle:hover", ".cat-mode-toggle:hover", ".iflow-rel-summary-table tr:hover td",
    ];

    private static readonly string[] ActiveStates =
    [
        ".happy-path-toggle.happy-path-active", ".dependency-toggle.dependency-active", ".status-toggle.status-active",
        ".category-toggle.category-active", ".percentile-btn.percentile-active", ".details-radio-btn.details-active",
        ".timeline-toggle-active", ".timeline-toggle-active:hover", ".iflow-toggle-active", ".iflow-toggle-active:hover",
        ".diagram-toggle-active", ".diagram-toggle-active:hover",
    ];

    [Fact]
    public void Every_violet_idle_hover_precedes_every_violet_active_state()
    {
        var lastIdle = IdleHovers.Max(s =>
        {
            var i = CssRules.IndexOf(Violet, s);
            Assert.True(i >= 0, $"the violet theme has no {s} rule");
            return i;
        });
        foreach (var s in ActiveStates)
        {
            var i = CssRules.IndexOf(Violet, s);
            Assert.True(i >= 0, $"the violet theme has no {s} rule");
            Assert.True(i > lastIdle, $"{s} must follow every idle hover, or a hover of equal specificity shadows it");
        }
    }

    [Fact]
    public void Violet_idle_hovers_use_the_themes_own_tint()
    {
        foreach (var s in IdleHovers)
            Assert.Equal("#ede9fe", CssRules.Value(Violet, s, "background")?.ToLowerInvariant());
    }

    [Fact]
    public void Violet_active_states_are_violet_and_their_hovers_darker()
    {
        Assert.Equal("#8b5cf6", CssRules.Value(Violet, ".percentile-btn.percentile-active", "border-color")?.ToLowerInvariant());
        Assert.Equal("#8b5cf6", CssRules.Value(Violet, ".timeline-toggle-active", "background")?.ToLowerInvariant());
        Assert.Equal("#8b5cf6", CssRules.Value(Violet, ".timeline-toggle-active", "border-color")?.ToLowerInvariant());
        Assert.Equal("#7c3aed", CssRules.Value(Violet, ".timeline-toggle-active:hover", "background")?.ToLowerInvariant());
        Assert.Equal("#7c3aed", CssRules.Value(Violet, ".diagram-toggle-active:hover", "background")?.ToLowerInvariant());
    }

    /// <summary>Q3: the blue the base sheet paints on the scenario's "#" link, its copy-name button and
    /// a search-matched parameterized row. (The row hover and selected tints are not overridden: every
    /// emitted row carries a status class, and each status has its own tints, the same in both themes.)</summary>
    [Fact]
    public void The_violet_theme_recolours_the_blue_the_base_sheet_paints_outside_the_toolbars()
    {
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ".scenario-link:hover", "background")?.ToLowerInvariant());
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ".copy-scenario-name:hover", "background")?.ToLowerInvariant());
        Assert.Equal("inset 4px 0 0 #8b5cf6", CssRules.Value(Violet, ".param-test-table tbody tr.row-search-match", "box-shadow")?.ToLowerInvariant());
    }

    // ── The parameterized row's states (roadmap 1.10; 5.0.0) ──

    /// <summary>Roadmap 1.10 found every row's status tint as specific as the row's <c>:hover</c> and <c>row-active</c>
    /// rules and after them, so a hovered or selected row looked like the rest, and gave each status a darker tint for
    /// both. Since 5.0.0 (plans/OPTION_C_PLAN.md S1) a row has no status tint, as in the Option C preview: the Status
    /// column's badge says it, and every row shows the same hover and selected state, a hovered selected row keeping
    /// its selection.</summary>
    [Fact]
    public void Every_parameterized_row_shows_the_same_hover_and_selection()
    {
        foreach (var status in new[] { "passed", "failed", "skipped", "bypassed" })
            Assert.Equal(-1, CssRules.IndexOf(Base, $".param-test-table tbody tr.row-{status}"));
        Assert.Equal("var(--kron-hover)", CssRules.Value(Base, ".param-test-table tbody tr:hover", "background"));
        Assert.Equal("var(--kron-selected)", CssRules.Value(Base, ".param-test-table tbody tr.row-active", "background"));
        Assert.Equal("var(--kron-selected)", CssRules.Value(Base, ".param-test-table tbody tr.row-active:hover", "background"));
        Assert.True(CssRules.IndexOf(Base, ".param-test-table tbody tr:hover") < CssRules.IndexOf(Base, ".param-test-table tbody tr.row-active"),
            "the selected state follows the hover");
    }

    // ── What a feature or scenario holds stays inside it (roadmap 1.10, the plan's Q7) ──

    /// <summary>A long token (a type name, an identifier, a URL) breaks where it has to instead of running
    /// past what holds it: inside a feature or scenario, whose content-visibility clips it out of sight,
    /// and everywhere else, where it scrolled the whole page (a failure cluster's message, a scenario name
    /// in the History section, a dependency chip). Tables and the error diff keep their words whole and
    /// scroll instead: a column squeezed below its longest word would split words that fit.</summary>
    [Fact]
    public void Text_anywhere_in_the_report_breaks_a_long_token_while_tables_keep_their_words_whole()
    {
        Assert.Equal("anywhere", CssRules.Value(Base, "body", "overflow-wrap"));
        Assert.Equal("normal", CssRules.Value(Base, "table", "overflow-wrap"));
        Assert.Equal("normal", CssRules.Value(Base, ".error-diff", "overflow-wrap"));
    }

    /// <summary>Each of these was a scroll container only at phone widths, or not at all, so a wide one was
    /// clipped by its scenario or scrolled the whole page.</summary>
    [Theory]
    [InlineData(".param-table-wrapper")]
    [InlineData(".step-param-table")]
    [InlineData(".step-param-combined-table")]
    [InlineData(".step-docstring")]
    [InlineData(".features-summary-table-wrapper")]
    [InlineData(".example-image")]
    [InlineData(".raw-plantuml pre")]
    [InlineData(".search-help-panel")]
    public void A_table_or_block_wider_than_its_holder_scrolls_inside_it(string selector) =>
        Assert.Equal("auto", CssRules.Value(Base, selector, "overflow-x"));

    /// <summary>The image keeps its 320 by 240 px cap (border outside it) and fills at most its link, whose
    /// width the cap now bounds together with its step: a percentage cap on the image itself would size
    /// the link to the image's natural width.</summary>
    [Fact]
    public void An_attachment_image_keeps_its_cap_and_never_outgrows_its_step()
    {
        Assert.Equal("min(322px, 100%)", CssRules.Value(Base, ".attachment-image-link", "max-width"));
        Assert.Equal("100%", CssRules.Value(Base, ".attachment-image", "max-width"));
        Assert.Equal("242px", CssRules.Value(Base, ".attachment-image", "max-height"));
        Assert.Equal("border-box", CssRules.Value(Base, ".attachment-image", "box-sizing"));
    }

    /// <summary>A label is a pill of one or two words, which moves to the next line whole; one wider than
    /// its line (an identifier used as a tag) was held on one line and clipped. It may break now, which it
    /// does only when it is wider than the whole line.</summary>
    [Fact]
    public void A_label_wider_than_its_line_may_break() =>
        Assert.NotEqual("nowrap", CssRules.Value(Base, "span.label", "white-space"));

    /// <summary>Stray text between two rules makes the browser drop the rule after it whole: the text
    /// <c>rgb(100, 100, 100)</c> left after the lightbox rule cost the doc string its block and its
    /// scrolling. Every selector in the built-in sheets is made of selector tokens and nothing else.</summary>
    [Fact]
    public void Every_selector_in_the_built_in_sheets_is_well_formed()
    {
        var bad = AllSheets
            .SelectMany(s => CssRules.Parse(s.Css).SelectMany(r => r.Selectors).Where(x => !Selector().IsMatch(x)).Select(x => $"{s.Name}: {x}"))
            .ToList();
        Assert.True(bad.Count == 0, "selectors a browser would drop, with their whole rule:\n" + string.Join("\n", bad));
    }

    /// <summary>Type, universal, class, id, attribute, pseudo-class and pseudo-element tokens (arguments
    /// nested one level deep, as in <c>:not(:has(svg))</c>) and combinators.</summary>
    [GeneratedRegex(@"^(?:\s*[>+~]\s*|\s+|\*|[a-zA-Z][\w-]*|\.[\w-]+|#[\w-]+|\[[^\[\]]*\]|::?[\w-]+(?:\((?:[^()]|\([^()]*\))*\))?)+$")]
    private static partial Regex Selector();
}
