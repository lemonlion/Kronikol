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
    /// a search-matched parameterized row. (The row hover and selected tints are not overridden: every
    /// emitted row carries a status class, and each status has its own tints, the same in both themes.)</summary>
    [Fact]
    public void The_violet_theme_recolours_the_blue_the_base_sheet_paints_outside_the_toolbars()
    {
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ".scenario-link:hover", "background")?.ToLowerInvariant());
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ".copy-scenario-name:hover", "background")?.ToLowerInvariant());
        Assert.Equal("inset 4px 0 0 #8b5cf6", CssRules.Value(Violet, ".param-test-table tbody tr.row-search-match", "box-shadow")?.ToLowerInvariant());
    }

    // ── The parameterized row's states (roadmap 1.10) ──

    /// <summary>Every row carries a status class, whose rule is as specific as the base row's
    /// <c>:hover</c> and <c>row-active</c> rules and follows them, so neither ever painted a real row: a
    /// hovered row gave no feedback, and a selected skipped row (a paler tint than its resting one) or
    /// bypassed row (no tint of its own) looked like the rest. Each status now darkens its own tint under
    /// the pointer and further when selected, and states its hover between the two, so a hovered selected
    /// row keeps its selected tint.</summary>
    [Theory]
    [InlineData("passed")]
    [InlineData("failed")]
    [InlineData("skipped")]
    [InlineData("bypassed")]
    public void A_parameterized_row_darkens_its_status_tint_when_hovered_and_further_when_selected(string status)
    {
        string[] states = [$".param-test-table tbody tr.row-{status}", $".param-test-table tbody tr.row-{status}:hover", $".param-test-table tbody tr.row-active.row-{status}"];
        var lightness = states.Select(s => Lightness(CssRules.Value(Base, s, "background") ?? throw new Xunit.Sdk.XunitException($"no background for {s}"))).ToArray();
        Assert.True(lightness[0] - lightness[1] >= 2 && lightness[1] - lightness[2] >= 2,
            $"{status}: L* resting {lightness[0]:F1}, hovered {lightness[1]:F1}, selected {lightness[2]:F1}; each step should darken by 2 or more");
        var order = states.Select(s => CssRules.IndexOf(Base, s)).ToArray();
        Assert.True(order[0] < order[1] && order[1] < order[2], $"{status}: rules at {string.Join(", ", order)}; the hover belongs between the resting and the selected tint");
    }

    /// <summary>CIE L* of a <c>#rrggbb</c> colour: the lightness a reader sees.</summary>
    private static double Lightness(string hex)
    {
        static double Linear(int c) => c / 255.0 <= 0.04045 ? c / 255.0 / 12.92 : Math.Pow((c / 255.0 + 0.055) / 1.055, 2.4);
        var rgb = Enumerable.Range(0, 3).Select(i => int.Parse(hex.AsSpan(1 + 2 * i, 2), NumberStyles.HexNumber)).ToArray();
        var y = 0.2126729 * Linear(rgb[0]) + 0.7151522 * Linear(rgb[1]) + 0.0721750 * Linear(rgb[2]);
        return y > 216.0 / 24389 ? 116 * Math.Cbrt(y) - 16 : 24389.0 / 27 * y;
    }

    // ── What a feature or scenario holds stays inside it (roadmap 1.10, the plan's Q7) ──

    /// <summary>A long token (a type name, an identifier, a URL) breaks where it has to instead of running
    /// past the feature or scenario holding it, whose content-visibility clips it out of sight. Tables and
    /// the error diff keep their words whole and scroll instead: a column squeezed below its longest word
    /// would split words that fit.</summary>
    [Fact]
    public void Text_in_a_feature_breaks_a_long_token_while_tables_keep_their_words_whole()
    {
        Assert.Equal("anywhere", CssRules.Value(Base, ".feature", "overflow-wrap"));
        Assert.Equal("normal", CssRules.Value(Base, ".feature table", "overflow-wrap"));
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
    [InlineData(".test-execution-summary")]
    [InlineData(".example-image")]
    [InlineData(".raw-plantuml pre")]
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
