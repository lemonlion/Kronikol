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

    /// <summary>The phone layout has one breakpoint (5.0.0, plans/OPTION_C_PLAN.md S12): every width query near it, in
    /// every sheet and in every embedded script's <c>matchMedia</c>, is <c>max-width: 768px</c> for the phone or
    /// <c>min-width: 768.02px</c> above it, so a page 768.8 CSS px wide (Firefox at 125 %) is in exactly one of them and
    /// a script decides as the sheets do.</summary>
    [Fact]
    public void The_phone_breakpoint_is_one_in_every_sheet_and_every_script()
    {
        var queries = new List<(string Where, string Query)>();
        foreach (var (name, css) in AllSheets.Append(("report-tokens.css", Stylesheets.ReportTokensStyleSheet)))
            queries.AddRange(CssRules.MediaQueries(css).Select(q => (name, q)));
        var assembly = typeof(ReportGenerator).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            queries.AddRange(MatchMedia().Matches(reader.ReadToEnd()).Select(m => (name, m.Groups[1].Value)));
        }

        foreach (var (where, query) in queries)
        foreach (Match m in WidthBound().Matches(query))
        {
            var bound = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (bound is < 760 or > 780)
                continue;
            var expected = m.Groups[1].Value == "max" ? 768 : 768.02;
            Assert.True(bound == expected,
                $"{where}: '{query}' bounds the phone layout at {bound}px; write {m.Groups[1].Value}-width: {expected.ToString(CultureInfo.InvariantCulture)}px");
        }
        // a sweep that read nothing would pass: the base sheet's two sides and a script's question are among them
        Assert.Contains(queries, q => q.Where == "stylesheets.css" && q.Query.Contains("(max-width: 768px)", StringComparison.Ordinal));
        Assert.Contains(queries, q => q.Where == "stylesheets.css" && q.Query.Contains("(min-width: 768.02px)", StringComparison.Ordinal));
        Assert.Contains(queries, q => q.Where.EndsWith("report-filter-bar-script.js", StringComparison.Ordinal) && q.Query == "(max-width: 768px)");
    }

    [GeneratedRegex(@"(min|max)-width:\s*([\d.]+)px")]
    private static partial Regex WidthBound();

    /// <summary>More filters' count is text on the accent: in every block of the token sheet (light, the dark one the
    /// switch sets, and the dark one the system's setting gives a page with no pick) its ink and its ground, resolved
    /// through the tokens as the cascade does, read at AA (4.5:1). The dark accent is a light blue, so white on it
    /// read at 2.4:1.</summary>
    [Theory]
    [InlineData(":root[data-theme=\"dark\"]", null)]
    [InlineData(":root:not([data-theme])", "(prefers-color-scheme: dark)")]
    [InlineData(":root", null)]
    public void The_more_filters_count_reads_at_AA_in_every_theme(string block, string? media)
    {
        var ink = TokenValue(block, media, CssRules.Value(Base, ".more-filters-count", "color"));
        var ground = TokenValue(block, media, CssRules.Value(Base, ".more-filters-count", "background"));

        var ratio = Contrast(ink, ground);
        Assert.True(ratio >= 4.5, $"{block}: the count is {ink} on {ground}, {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1");
    }

    // WCAG 2.2's contrast ratio of two #rrggbb colours
    private static double Contrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            double Channel(int i)
            {
                var c = Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255.0;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
        }
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [GeneratedRegex(@"^var\((--[\w-]+)\)$")]
    private static partial Regex TokenReference();

    // what a declaration that is one token reference gives in a block of the token sheet, as the cascade resolves it
    private static string TokenValue(string block, string? media, string? value)
    {
        var tokens = Stylesheets.ReportTokensStyleSheet;
        var m = TokenReference().Match(value ?? "");
        Assert.True(m.Success, $"'{value}' is not one token");
        return CssRules.Value(tokens, block, m.Groups[1].Value, media) ?? CssRules.Value(tokens, ":root", m.Groups[1].Value)
            ?? throw new InvalidOperationException(m.Groups[1].Value + " is not in the token sheet");
    }

    /// <summary>The filter rows fold with one class at every width: More filters sets it on a desktop and the Filters
    /// button on a phone, so the rows stay as they were when the window crosses the phone breakpoint, and the search help
    /// opens while they are folded at either width (on a phone the rows were hidden by an inline style, the help with
    /// them).</summary>
    /// <summary>The filter rows' label column is at least 92 px, and a label never breaks (5.1.0): a face wider than
    /// the one the column was set in drew DEPENDENCIES wider than 92 px, and the page broke the word with its S on a line
    /// of its own; the column now widens for such a label. The 92 px is a minimum width, not a flex basis over a width
    /// of auto (the preview's round 6, <c>c-round6.css</c>; found missing by S14's parity run): Firefox takes a flex
    /// item's max-content contribution from its width, so the Status group, sized to its content, was sized from its
    /// label's text (about 42 px) while the label was laid out at 92 px, and its buttons ran 50 px past the group and
    /// under Duration. A contribution is held to the minimum: measured in Firefox, the group's last button ends at its
    /// edge with this rule and 50 px past it with that one. A label inline beside Status, or on a line of its own in a
    /// narrow box, is as wide as its text.</summary>
    [Fact]
    public void The_filter_rows_label_column_is_a_minimum_width_and_a_label_inline_or_alone_is_its_text()
    {
        foreach (var label in new[] { ".status-filters-label", ".duration-filters-label", ".happy-filters-label", ".tag-filters-label", ".dependency-filters-label" })
        {
            Assert.Equal("92px", CssRules.Value(Base, label, "min-width"));
            Assert.Equal("auto", CssRules.Value(Base, label, "width"));
            Assert.Equal("0 0 auto", CssRules.Value(Base, label, "flex"));
            Assert.Equal("nowrap", CssRules.Value(Base, label, "white-space"));
        }
        Assert.Equal("auto", CssRules.Value(Base, ".filter-row.pair-fits .duration-filters-label", "width"));
        Assert.Equal("auto", CssRules.Value(Base, ".filter-row.trio-fits .happy-filters-label", "width"));

        // CssRules reads no @container block: the narrow box's own rule, by its text
        var at = Base.IndexOf("@container (max-width: 430px) {", StringComparison.Ordinal);
        Assert.True(at >= 0, "the narrow box's container block is gone");
        var narrow = Base[at..Base.IndexOf("\n        }", at, StringComparison.Ordinal)];
        Assert.Matches(@"\.dependency-filters-label\s*\{[^}]*\bwidth:\s*auto", narrow);
    }

    [Fact]
    public void The_filter_rows_fold_with_one_class_at_every_width()
    {
        Assert.Equal("none", CssRules.Value(Base, ".filtering-box.rows-folded:not(.help-open) > .filters", "display"));
        Assert.Equal("none", CssRules.Value(Base, ".filtering-box.rows-folded.help-open > .filters > :not(.search-help-panel)", "display"));
        Assert.Null(CssRules.Value(Base, ".filtering-box.rows-folded:not(.help-open) > .filters", "display", "(min-width: 768.02px)"));
    }

    /// <summary>The phone's Filters button and its sticky search line take their colours from the tokens (they were
    /// rgb(100, 100, 100) and rgb(224, 224, 224) in every theme): the search line the filter box's own ground, and the
    /// button the rows' label ink, which reads at AA on that ground in every block of the token sheet.</summary>
    [Theory]
    [InlineData(":root[data-theme=\"dark\"]", null)]
    [InlineData(":root:not([data-theme])", "(prefers-color-scheme: dark)")]
    [InlineData(":root", null)]
    public void The_phone_Filters_button_and_search_line_read_in_every_theme(string block, string? media)
    {
        var box = CssRules.Value(Base, ".filtering-box", "background");
        Assert.Equal(box, CssRules.Value(Base, ".filter-search", "background", "(max-width: 768px)"));

        var ink = TokenValue(block, media, CssRules.Value(Base, ".mobile-filter-toggle", "color"));
        var ground = TokenValue(block, media, box);
        var ratio = Contrast(ink, ground);
        Assert.True(ratio >= 4.5, $"{block}: the Filters button is {ink} on {ground}, {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1");
    }

    /// <summary>No sheet keeps an empty media block (stylesheets.css held one at min-width 768.02px).</summary>
    [Fact]
    public void No_sheet_has_an_empty_media_block()
    {
        foreach (var (name, css) in AllSheets.Append(("report-tokens.css", Stylesheets.ReportTokensStyleSheet)))
            Assert.False(EmptyMedia().IsMatch(CssComment().Replace(css, "")), $"{name} has an empty @media block");
    }

    [GeneratedRegex(@"@media[^{]*\{\s*\}")]
    private static partial Regex EmptyMedia();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();

    [GeneratedRegex(@"matchMedia\(\s*'([^']*)'")]
    private static partial Regex MatchMedia();

    // ── T2: the scenario toolbar's base rules live in the base sheet (S2) ──

    private static readonly string[] ToolbarSelectors =
    [
        ".diagram-toggle", ".diagram-toggle-btn", ".diagram-toggle-btn:hover",
        ".diagram-toggle-active", ".diagram-toggle-active:hover", ".diagram-toggle[data-layout=\"inline\"]",
    ];

    [Fact]
    public void The_scenario_toolbar_is_a_wrapping_flex_row_in_the_base_sheet()
    {
        Assert.Equal("flex", CssRules.Value(Base, ".diagram-toggle", "display"));
        Assert.Equal("wrap", CssRules.Value(Base, ".diagram-toggle", "flex-wrap"));
        // 5.0.0 (plans/OPTION_C_PLAN.md S1): a length, since 1em became 14px with the page's type; since S8 the tabs bar
        // and the options bar share it, and only the options bar adds its own top and bottom padding
        Assert.Equal("0 16px", CssRules.Value(Base, ".diagram-toggle", "padding"));
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

    // ── T4: the violet theme's hover edges come before its active states (S4); since 5.0.0 it sets the report's tokens (S13) ──

    // 5.0.0: the tags are chips of their own, and Happy paths a three-way control (plans/OPTION_C_PLAN.md S6)
    private static readonly string[] FilterToggles = ["tag", "dependency", "status"];

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

    [Fact]
    public void The_violet_theme_grounds_the_tags_switch_as_it_does_the_dependencies_switch()
    {
        // Found by plans/TAGS_PLAN.md T2: the violet theme gave .dep-mode-toggle and .cat-mode-toggle their idle ground and
        // missed .tag-mode-toggle, so the Tags row's ANY / ALL read apart from the Dependencies row's.
        // 5.0.0 (plans/OPTION_C_PLAN.md S13): the theme sets the report's tokens and grounds neither switch, so the base
        // sheet's ground holds for both, and both take the theme's hover edge
        Assert.Equal(CssRules.Value(Violet, ".dep-mode-toggle", "background"), CssRules.Value(Violet, ".tag-mode-toggle", "background"));
        Assert.NotNull(CssRules.Value(Violet, ".tag-mode-toggle:hover", "border-color"));
        Assert.Equal(CssRules.Value(Violet, ".dep-mode-toggle:hover", "border-color"), CssRules.Value(Violet, ".tag-mode-toggle:hover", "border-color"));
    }

    // the hovers whose edge the theme sets; their tint is the base sheet's, read from --kron-hover, which the theme sets
    private static readonly string[] HoverEdges =
    [
        ".happy-seg:hover", ".tag-toggle:hover", ".tag-more:hover", ".dependency-toggle:hover", ".status-toggle:hover",
        ".export-btn:hover", ".fold-btn:hover", ".percentile-btn:hover", ".details-radio-btn:hover",
        ".scenario-diagram-controls-toggle:hover", ".tag-mode-toggle:hover", ".dep-mode-toggle:hover",
    ];

    // 5.0.0: the Scenario Timeline toggle is a tab (plans/OPTION_C_PLAN.md S3), so the theme has no timeline-toggle rule
    private static readonly string[] ActiveStates =
    [
        ".happy-seg[aria-pressed=\"true\"]", ".tag-toggle.tag-active", ".dependency-toggle.dependency-active", ".status-toggle.status-active",
        ".percentile-btn.percentile-active", ".details-radio-btn.details-active",
        ".iflow-toggle-active", ".iflow-toggle-active:hover", ".diagram-toggle-active", ".diagram-toggle-active:hover",
    ];

    [Fact]
    public void Every_violet_hover_edge_precedes_every_violet_active_state()
    {
        var lastEdge = HoverEdges.Max(s =>
        {
            var i = CssRules.IndexOf(Violet, s);
            Assert.True(i >= 0, $"the violet theme has no {s} rule");
            return i;
        });
        foreach (var s in ActiveStates)
        {
            var i = CssRules.IndexOf(Violet, s);
            Assert.True(i >= 0, $"the violet theme has no {s} rule");
            Assert.True(i > lastEdge, $"{s} must follow every hover edge, or a hover of equal specificity shadows it");
        }
    }

    /// <summary>Since 5.0.0 a hovered control takes the violet tint from the base sheets' own hover rule, which reads
    /// <c>--kron-hover</c>: the theme sets the token (plans/OPTION_C_PLAN.md S13), and of a hover only its edge.</summary>
    [Fact]
    public void Violet_hovers_take_the_themes_tint_from_its_hover_token()
    {
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ":root", "--kron-hover")?.ToLowerInvariant());
        var sheets = new[] { Base, Popup, DiagramContextMenu.GetCollapsibleNotesStyles() };
        string[] tinted = [.. HoverEdges.Where(s => s != ".scenario-diagram-controls-toggle:hover"),
            ".diagram-toggle-btn:hover", ".iflow-toggle-btn:hover", ".iflow-rel-summary-table tr:hover td", ".scenario-link:hover", ".copy-scenario-name:hover"];
        foreach (var s in tinted)
        {
            var background = sheets.Select(css => CssRules.Value(css, s, "background")).LastOrDefault(v => v is not null);
            Assert.True(background?.StartsWith("var(--kron-hover", StringComparison.Ordinal) == true, $"the base sheets give {s} the background {background ?? "of nothing"}");
            Assert.Null(CssRules.Value(Violet, s, "background"));
        }
        Assert.Equal("var(--violet-edge)", CssRules.Value(Violet, ".happy-seg:hover", "border-color"));
    }

    [Fact]
    public void Violet_active_states_are_violet_and_their_hovers_darker()
    {
        Assert.Equal("#8b5cf6", CssRules.Value(Violet, ":root", "--violet-solid")?.ToLowerInvariant());
        Assert.Equal("#7c3aed", CssRules.Value(Violet, ":root", "--violet-solid-hover")?.ToLowerInvariant());
        Assert.Equal("var(--violet-solid)", CssRules.Value(Violet, ".percentile-btn.percentile-active", "border-color"));
        Assert.Equal("var(--violet-solid)", CssRules.Value(Violet, ".diagram-toggle-active", "background"));
        Assert.Equal("var(--violet-solid-hover)", CssRules.Value(Violet, ".diagram-toggle-active:hover", "background"));
        Assert.Equal("var(--violet-solid-hover)", CssRules.Value(Violet, ".iflow-toggle-active:hover", "background"));
    }

    /// <summary>Q3: the blue the base sheet paints on the scenario's "#" link, its copy-name button and a search-matched
    /// parameterized row. The link and the button read <c>--kron-hover</c>, which the theme sets since 5.0.0 (S13); the row's
    /// edge is the theme's own. (A row's hover and selected tints read <c>--kron-hover</c> and <c>--kron-selected</c>, which
    /// the theme sets too.)</summary>
    [Fact]
    public void The_violet_theme_recolours_the_blue_the_base_sheet_paints_outside_the_toolbars()
    {
        Assert.Equal("var(--kron-hover)", CssRules.Value(Base, ".scenario-link:hover", "background"));
        Assert.Equal("var(--kron-hover)", CssRules.Value(Base, ".copy-scenario-name:hover", "background"));
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ":root", "--kron-hover")?.ToLowerInvariant());
        Assert.Equal("inset 4px 0 0 var(--violet-solid)", CssRules.Value(Violet, ".param-test-table tbody tr.row-search-match", "box-shadow"));
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
