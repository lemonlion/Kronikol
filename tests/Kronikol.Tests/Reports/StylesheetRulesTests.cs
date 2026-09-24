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
    ];

    // ── T1: the widths (S1, S1b) ──

    [Fact]
    public void Export_button_labels_never_wrap_inside_their_buttons() =>
        Assert.Equal("nowrap", CssRules.Value(Base, ".export-btn", "white-space"));

    [Theory]
    [InlineData(".filtering-box-export")]
    [InlineData(".filtering-box-header")]
    [InlineData(".toolbar-left")]
    public void Rows_that_would_squeeze_their_buttons_wrap_them_whole_instead(string selector) =>
        Assert.Equal("wrap", CssRules.Value(Base, selector, "flex-wrap"));

    [Fact]
    public void Between_the_phone_layout_and_the_breakpoint_the_filtering_box_takes_its_own_row()
    {
        Assert.Equal("wrap", CssRules.Value(Base, ".header-row", "flex-wrap", Band));
        Assert.Equal("100%", CssRules.Value(Base, ".filtering-box", "flex-basis", Band));
    }

    [Fact]
    public void The_ci_box_is_capped_in_the_row_layout_and_breaks_its_values_not_its_labels()
    {
        Assert.Equal("100%", CssRules.Value(Base, ".ci-metadata table", "max-width"));
        Assert.Equal("nowrap", CssRules.Value(Base, ".ci-metadata td:first-child", "white-space"));
        Assert.Equal("anywhere", CssRules.Value(Base, ".ci-metadata td:last-child", "overflow-wrap"));
        Assert.Equal("20em", CssRules.Value(Base, ".ci-metadata", "max-width", RowLayout));
        Assert.Null(CssRules.Value(Base, ".ci-metadata", "max-width"));
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
        Assert.Equal("1em", CssRules.Value(Base, ".diagram-toggle", "padding-left"));
        Assert.Equal("1em", CssRules.Value(Base, ".diagram-toggle", "padding-right"));
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
        ".export-btn:hover", ".collapse-expand-all:hover", ".percentile-btn:hover", ".timeline-toggle:hover",
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
    /// a search-matched parameterized row. (The row hover and active tints are not overridden: every
    /// emitted row carries a status class whose later rule of equal specificity wins over both.)</summary>
    [Fact]
    public void The_violet_theme_recolours_the_blue_the_base_sheet_paints_outside_the_toolbars()
    {
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ".scenario-link:hover", "background")?.ToLowerInvariant());
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ".copy-scenario-name:hover", "background")?.ToLowerInvariant());
        Assert.Equal("inset 4px 0 0 #8b5cf6", CssRules.Value(Violet, ".param-test-table tbody tr.row-search-match", "box-shadow")?.ToLowerInvariant());
    }
}
