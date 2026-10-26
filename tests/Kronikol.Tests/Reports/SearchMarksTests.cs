using System.Globalization;
using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The marks a match is shown with where it is drawn (plans/SEARCH_PLAN.md 4.6.5): their two tokens read against every
/// fill a mark can sit on (H36; the painted mark is the E2E fact of the same name), their script is in the page, and their
/// rules paint from the tokens.
/// </summary>
public partial class SearchMarksTests
{
    private static readonly string Tokens = Stylesheets.ReportTokensStyleSheet;
    private static readonly string Search = Stylesheets.SearchStyleSheet;

    // what a token gives in a block of the token sheet, as the cascade resolves it: the block's own value, else the light one
    private static string Token(string block, string? media, string name) =>
        CssRules.Value(Tokens, block, name, media) ?? CssRules.Value(Tokens, ":root", name)
        ?? throw new InvalidOperationException(name + " is not in the token sheet");

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

    private static string Ratio(double r) => r.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>The three blocks of the token sheet: light, the dark one the switch sets, and the dark one the system's
    /// setting gives a page with no pick.</summary>
    public static TheoryData<string, string?> Blocks => new()
    {
        { ":root", null },
        { ":root[data-theme=\"dark\"]", null },
        { ":root:not([data-theme])", "(prefers-color-scheme: dark)" },
    };

    /// <summary>H36: the mark's edge, the underline under every occurrence and the outline round the one named, shows the
    /// reader where the match is (WCAG 1.4.11): 3:1 at least against a payload's card, an event's card, a step bar and the
    /// card the diagram and the page's sections are drawn on.</summary>
    [Theory]
    [MemberData(nameof(Blocks))]
    public void H36_The_mark_edge_shows_against_every_note_fill(string block, string? media)
    {
        var edge = Token(block, media, "--kron-mark-edge");
        foreach (var fill in new[] { "--dg-note", "--dg-event", "--dg-step", "--kron-card" })
        {
            var ground = Token(block, media, fill);
            var ratio = Contrast(edge, ground);
            Assert.True(ratio >= 3, $"{block}: the edge {edge} on {fill} {ground} is {Ratio(ratio)}:1");
        }
    }

    /// <summary>Every ink a marked word can be drawn in reads at AA (4.5:1) on the mark (WCAG 1.4.3): a note's body and its
    /// headers, a step bar's text and its table's head, the page's text, and a link, which an arrow's label to an internal
    /// flow is drawn as. The dark mark was #4d3d0a until the marks were drawn, on which the dark accent read at 4.34:1.</summary>
    [Theory]
    [MemberData(nameof(Blocks))]
    public void Text_on_a_mark_reads_at_AA_in_every_theme(string block, string? media)
    {
        var mark = Token(block, media, "--kron-mark");
        foreach (var ink in new[] { "--dg-ink", "--dg-ink-soft", "--dg-step-ink", "--kron-ink", "--kron-ink-soft", "--kron-accent" })
        {
            var colour = Token(block, media, ink);
            var ratio = Contrast(colour, mark);
            Assert.True(ratio >= 4.5, $"{block}: {ink} {colour} on the mark {mark} is {Ratio(ratio)}:1");
        }
    }

    [Fact]
    public void The_marks_script_is_in_the_page_after_the_search_scripts()
    {
        var path = ReportGenerator.GenerateHtmlReport([], [new Feature { DisplayName = "Pantry", Scenarios = [new Scenario { Id = "p1", DisplayName = "A batch", Result = ExecutionResult.Passed }] }],
            DateTime.UtcNow, DateTime.UtcNow, null, "SearchMarks_Script.html", "Pantry", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var html = ReportHtml.Read(path);

        var marks = html.IndexOf("<script id=\"kronikol-search-marks\">", StringComparison.Ordinal);
        Assert.True(marks >= 0, "the marks script is in the page");
        Assert.True(marks > html.IndexOf("<script id=\"kronikol-search-results\">", StringComparison.Ordinal), "after the Search tab's script");
        var script = html[marks..html.IndexOf("</script>", marks, StringComparison.Ordinal)];
        Assert.Contains("window.kronikol_marks = {", script);
    }

    /// <summary>The marks paint from the tokens, so a custom theme that sets them changes them: the ground and the edge in
    /// the diagram and in the HTML's highlights, the place's outline in the accent; and the arrival's pulse holds still
    /// under reduced motion.</summary>
    [Fact]
    public void The_marks_paint_from_the_tokens_and_hold_still_under_reduced_motion()
    {
        Assert.Equal("var(--kron-mark)", CssRules.Value(Search, "svg .kron-mark-ground", "fill"));
        Assert.Equal("var(--kron-mark-edge)", CssRules.Value(Search, "svg .kron-mark-ground.kron-mark-current", "stroke"));
        Assert.Equal("var(--kron-mark-edge)", CssRules.Value(Search, "svg .kron-mark-edge", "stroke"));
        Assert.Equal("var(--kron-accent)", CssRules.Value(Search, "svg rect.kron-mark-place", "stroke"));
        Assert.Contains("var(--kron-accent)", CssRules.Value(Search, ".kron-mark-place", "outline"));
        Assert.Contains("var(--kron-accent)", CssRules.Value(Search, "div.note-scroll.kron-mark-place", "outline"));
        Assert.Equal("var(--kron-mark)", CssRules.Value(Search, "::highlight(kron-mark)", "background-color"));
        Assert.Contains("var(--kron-mark-edge)", CssRules.Value(Search, "::highlight(kron-mark)", "text-decoration"));
        Assert.Equal("var(--kron-mark)", CssRules.Value(Search, "::highlight(kron-mark-current)", "background-color"));
        Assert.Contains("var(--kron-mark-edge)", CssRules.Value(Search, "::highlight(kron-mark-current)", "text-decoration"));
        Assert.NotNull(CssRules.Value(Search, ".kron-mark-place.kron-mark-arrive", "animation"));
        Assert.Equal("none", CssRules.Value(Search, ".kron-mark-place.kron-mark-arrive", "animation", "(prefers-reduced-motion: reduce)"));
        Assert.Equal("none", CssRules.Value(Search, "svg rect.kron-mark-place.kron-mark-arrive", "animation", "(prefers-reduced-motion: reduce)"));
        // no colour of its own: every one is a token
        foreach (var rule in CssRules.Parse(Search).Where(r => r.Selectors.Any(s => s.Contains("kron-mark", StringComparison.Ordinal))))
        foreach (var (property, value) in rule.Declarations)
            Assert.False(Colour().IsMatch(value), $"{string.Join(", ", rule.Selectors)} {{ {property}: {value} }} names a colour of its own");
    }

    [GeneratedRegex(@"#[0-9a-f]{3,8}\b|\b(?:rgba?|hsla?)\(", RegexOptions.IgnoreCase)]
    private static partial Regex Colour();
}
