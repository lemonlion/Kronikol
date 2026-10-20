using System.Text;
using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Every colour the report's own sheets paint follows the theme (5.0.0, plans/OPTION_C_PLAN.md S13b): it is read from a
/// token, or from a variable a theme sets, and never written as a value that reads in one theme only. A sheet that a page
/// without the token sheet carries too (the component diagram's page) keeps a fallback inside its var(), which this
/// allows. What stays a value is named here rule by rule, so a new one fails where it is written: black at an alpha as a
/// shadow or a scrim, which darkens either ground, the jump button's hover, its solid darkened, and the warm-up hatch,
/// white at an alpha over the bar's own colour. The rules for markup no page writes are gone, and so is the script only
/// that markup called.
/// </summary>
public partial class ReportSheetColoursTests
{
    private static readonly Dictionary<string, string> Sheets = new()
    {
        ["stylesheets.css"] = Stylesheets.HtmlReportStyleSheet,
        ["context-menu-styles.css"] = DiagramContextMenu.GetStyles(),
        ["inline-svg-styles.css"] = DiagramContextMenu.GetInlineSvgStyles(),
        ["internal-flow-popup-styles.css"] = DiagramContextMenu.GetInternalFlowPopupStyles(),
        ["collapsible-notes-styles.css"] = DiagramContextMenu.GetCollapsibleNotesStyles(),
        ["history-styles.css"] = Stylesheets.HistoryStyleSheet,
        ["report-diagnostics-styles.css"] = Stylesheets.ReportDiagnosticsStyleSheet,
        ["labs-styles.css"] = Stylesheets.LabsStyleSheet,
        ["warm-up-styles.css"] = Stylesheets.WarmUpStyleSheet,
    };

    public static TheoryData<string> SheetNames => [.. Sheets.Keys];

    /// <summary>The values meant for both themes, by sheet, selector and property.</summary>
    private static readonly HashSet<(string Sheet, string Selector, string Property)> BothThemes =
    [
        ("stylesheets.css", ".lightbox-overlay", "background"),
        ("stylesheets.css", ".lightbox-overlay img", "box-shadow"),
        ("stylesheets.css", ".jump-to-failure:hover", "background"),
        ("context-menu-styles.css", ".diagram-ctx-menu", "box-shadow"),
        ("context-menu-styles.css", ".diagram-ctx-menu .submenu", "box-shadow"),
        ("internal-flow-popup-styles.css", ".iflow-popup", "box-shadow"),
        ("warm-up-styles.css", ".timeline-bar .timeline-warm-up", "background"),
    ];

    /// <summary>A colour written as a value: a hex colour, a colour function with a number in it, or a colour's name.</summary>
    [GeneratedRegex(@"#[0-9a-f]{3,8}\b|\b(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch)\([^)]*\d|\b(?:white|black|red|green|blue|gr[ae]y|silver|orange|yellow|purple|navy|maroon)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Literal();

    /// <summary>A property that paints a colour, a custom property among them.</summary>
    [GeneratedRegex(@"^(?:--|color$|background|border|outline|box-shadow$|text-shadow$|fill$|stroke$|accent-color$|caret-color$|text-decoration|column-rule)")]
    private static partial Regex Paints();

    /// <summary>The value with each var() taken out, its fallback with it: a fallback is what a page without the token
    /// sheet shows, so it may be a value.</summary>
    internal static string WithoutVariables(string value)
    {
        var text = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (string.CompareOrdinal(value, i, "var(", 0, 4) == 0)
            {
                var depth = 0;
                var j = i + 3;
                for (; j < value.Length; j++)
                {
                    if (value[j] == '(') depth++;
                    else if (value[j] == ')' && --depth == 0) break;
                }
                text.Append("var()");
                i = j;
                continue;
            }
            text.Append(value[i]);
        }
        return text.ToString();
    }

    private static IEnumerable<(CssRule Rule, string Property, string Value)> Painted(string name) =>
        from rule in CssRules.Parse(Sheets[name])
        from declaration in rule.Declarations
        where Paints().IsMatch(declaration.Key) && Literal().IsMatch(WithoutVariables(declaration.Value))
        select (rule, declaration.Key, declaration.Value);

    [Theory]
    [MemberData(nameof(SheetNames))]
    public void Every_colour_a_report_sheet_paints_follows_the_theme(string name)
    {
        var own = Painted(name)
            .Where(p => !p.Rule.Selectors.All(s => BothThemes.Contains((name, s, p.Property))))
            .Select(p => $"{string.Join(", ", p.Rule.Selectors)} {{ {p.Property}: {p.Value} }}")
            .ToList();
        Assert.True(own.Count == 0, $"{name} paints {own.Count} colours of its own, the same in every theme:\n" + string.Join("\n", own));
    }

    /// <summary>Each value named as meant for both themes is still in its sheet, so the list cannot outlive its rules.</summary>
    [Fact]
    public void Each_colour_meant_for_both_themes_is_still_written_where_it_is_named()
    {
        foreach (var (sheet, selector, property) in BothThemes)
            Assert.True(Painted(sheet).Any(p => p.Rule.Selectors.Contains(selector) && p.Property == property),
                $"{sheet}: {selector} {{ {property} }} is named as a colour for both themes and is not written");
    }

    /// <summary>The diagram's menu draws its items in the page's ink on the page's card, where it drew them on white and
    /// left them the ink of the page under it, the dark theme's light ink among them.</summary>
    [Fact]
    public void The_diagram_menu_sets_its_own_ink_on_its_own_ground()
    {
        var css = Sheets["context-menu-styles.css"];
        Assert.Equal("var(--kron-ink, #242b36)", CssRules.Value(css, ".diagram-ctx-menu", "color"));
        Assert.Equal("var(--kron-card, #fff)", CssRules.Value(css, ".diagram-ctx-menu", "background"));
        Assert.Equal("var(--kron-card, #fff)", CssRules.Value(css, ".diagram-ctx-menu .submenu", "background"));
        Assert.Equal("var(--kron-hover, #e8f0fe)", CssRules.Value(css, ".diagram-ctx-menu > div:hover", "background"));
    }

    /// <summary>The sparkline's run colours are variables of its sheet, each read from a token, so a run reads on the
    /// card of either theme; the page writes the variables' names, not the colours. The sheet sets no ground of its own:
    /// the page writes the gradient as an inline background shorthand, which would hide it, so 4.x's grey never painted.</summary>
    [Fact]
    public void The_sparkline_reads_each_kind_of_run_from_a_token()
    {
        var css = Sheets["history-styles.css"];
        var expected = new Dictionary<string, string>
        {
            ["--sp"] = "var(--kron-pass-ring)", ["--sf"] = "var(--kron-fail-solid)", ["--ss"] = "var(--kron-skip)",
            ["--sa"] = "var(--kron-track)", ["--su"] = "var(--kron-hairline)", ["--sn"] = "var(--kron-hairline-soft)",
            ["--so"] = "var(--kron-warn)",
        };
        foreach (var (name, value) in expected)
            Assert.Equal(value, CssRules.Value(css, ".history-sparkline", name));
        Assert.Null(CssRules.Value(css, ".history-sparkline", "background"));
    }

    /// <summary>No page writes an examples table, its rows or a table diff's surplus and missing rows (the parameterized
    /// tables replaced the first long ago), and no feature is a child of the body (5.0.0's sit in a tab panel), so their
    /// rules go, and the script that only those rows called goes from every report.</summary>
    [Fact]
    public void The_rules_and_the_script_for_markup_no_page_writes_are_gone()
    {
        var selectors = CssRules.Parse(Sheets["stylesheets.css"]).SelectMany(r => r.Selectors).ToList();
        foreach (var dead in new[] { ".examples-table", ".examples-row-passed", ".examples-row-failed", ".examples-row-skipped",
                     ".examples-row-bypassed", ".examples-row-expandable:hover", ".examples-row-expanded", ".examples-detail-row td",
                     ".row-surplus td:first-child", ".row-missing td:first-child", "body > details > details > details.example" })
            Assert.DoesNotContain(dead, selectors);
        Assert.DoesNotContain(selectors, s => s.Contains("examples-table", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(ReportGenerator).Assembly.GetManifestResourceNames(),
            n => n.EndsWith("report-toggle-examples-detail-function.js", StringComparison.Ordinal));
    }
}
