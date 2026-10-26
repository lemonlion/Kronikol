using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The labs page and the violet theme on the report's tokens (5.0.0, plans/OPTION_C_PLAN.md S13, Q3). The labs page chooses
/// light or dark before its first style, as a report does, opens its styles with the token sheet, carries the report's
/// switch on its title line, and draws its own parts in the tokens; the sheets it shares with the report keep no light
/// fallback, since every page that carries them carries the token sheet. The violet theme sets the report's tokens for the
/// light and the dark theme, in the blocks the token sheet uses, and draws everything else in variables, a solid violet
/// always under the ink made for it. The search help, which the violet painted dark on a light page, is drawn in the
/// tokens in every theme.
/// </summary>
public partial class ThemedPagesHtmlTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-themed-pages").FullName;

    private static readonly string Violet = Stylesheets.VioletThemeStyleSheet;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static string Labs(string title = "Checkout - Test Run Report", string? customCss = null) =>
        LabsReportGenerator.Build(new LabsPage(title, [], "ThemedSuite", null,
            [new DiagnosticEntry(DiagnosticKind.CaptureDegraded, "tap-di-redis: decoding disabled on 1 connection(s)")],
            "TestRunReport.html", CustomCss: customCss));

    private static IDocument Parse(string html) =>
        BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();

    private static string Lf(string text) => text.Replace("\r\n", "\n");

    /// <summary>One of the library's embedded scripts, as the generators load it.</summary>
    private static string Resource(string suffix)
    {
        var assembly = typeof(ReportGenerator).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    /// <summary>The theme switch a run report draws on its title line.</summary>
    private string ReportSwitch()
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "t1", DisplayName = "Create order", Result = ExecutionResult.Passed }] }],
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(_dir, "ThemedPages_Report.html"), "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var button = Parse(ReportHtml.Read(path)).QuerySelector("div.title-line > .title-actions > button.theme-toggle");
        Assert.True(button is not null, "the run report has no theme switch on its title line");
        return button!.OuterHtml;
    }

    /// <summary>A colour written as a value rather than read from a variable.</summary>
    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?|oklch|lab|lch)\(|\b(?:white|black)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Literal();

    private static Dictionary<string, string> Block(string css, string selector, string? media = null)
    {
        var rules = CssRules.For(css, selector, media);
        Assert.True(rules.Count == 1, $"{rules.Count} rules for {selector}{(media is null ? "" : " in @media " + media)}");
        return rules[0].Declarations.ToDictionary(d => d.Key, d => d.Value.ToLowerInvariant());
    }

    // ── the labs page ──

    [Fact]
    public void The_labs_page_chooses_its_theme_before_its_first_style()
    {
        var head = Parse(Labs()).Head!;

        var first = head.Children.First(e => e.LocalName is "script" or "style");
        Assert.Equal("script", first.LocalName);
        Assert.Equal("kronikol-theme-boot", first.Id);
        Assert.Equal(Lf(Resource("report-theme-boot-script.js")), Lf(first.TextContent));
    }

    [Fact]
    public void The_labs_page_opens_its_styles_with_the_token_sheet()
    {
        var styles = Parse(Labs(customCss: ".labs-badge { outline: 1px dotted; }")).Head!.QuerySelectorAll("style").ToArray();

        Assert.Equal(2, styles.Length);
        var builtIn = Lf(styles[0].TextContent);
        Assert.StartsWith(Lf(Stylesheets.ReportTokensStyleSheet), builtIn.TrimStart());
        int[] order =
        [
            builtIn.IndexOf(Lf(Stylesheets.ReportTokensStyleSheet), StringComparison.Ordinal),
            builtIn.IndexOf(Lf(Stylesheets.HtmlReportStyleSheet), StringComparison.Ordinal),
            builtIn.IndexOf(Lf(Stylesheets.HistoryStyleSheet), StringComparison.Ordinal),
            builtIn.IndexOf(Lf(Stylesheets.ReportDiagnosticsStyleSheet), StringComparison.Ordinal),
            builtIn.IndexOf(Lf(Stylesheets.LabsStyleSheet), StringComparison.Ordinal),
        ];
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
        // the reader's own sheet still comes after the built-in sheets, so it can set the tokens again
        Assert.Equal(".labs-badge { outline: 1px dotted; }", styles[1].TextContent);
    }

    [Fact]
    public void The_labs_page_has_the_reports_theme_switch_on_its_title_line()
    {
        var page = Parse(Labs("Checkout <run>"));

        var header = page.QuerySelector("header.labs-header");
        Assert.NotNull(header);
        // the title line, then the two lines under it as before
        Assert.Equal(["div", "p", "p"], header!.Children.Select(e => e.LocalName).ToArray());
        var line = header.Children[0];
        Assert.Equal("title-line", line.ClassName);
        Assert.Equal(["h1", "button"], line.Children.Select(e => e.LocalName).ToArray());
        Assert.Equal("Checkout <run> Labs", Regex.Replace(line.Children[0].TextContent, @"\s+", " ").Trim());
        Assert.NotNull(line.Children[0].QuerySelector(":scope > span.labs-badge"));
        Assert.Equal(ReportSwitch(), line.Children[1].OuterHtml);

        var script = page.Head!.QuerySelector("script#kronikol-theme-switch");
        Assert.True(script is not null, "the page has no switch script, so its switch would do nothing");
        Assert.Equal(Lf(Resource("report-theme-switch-script.js")), Lf(script!.TextContent));
    }

    [Fact]
    public void The_labs_pages_own_parts_are_drawn_in_the_tokens()
    {
        var css = Stylesheets.LabsStyleSheet;

        Assert.Equal("var(--kron-warn-soft)", CssRules.Value(css, ".labs-badge", "background"));
        Assert.Equal("var(--kron-warn)", CssRules.Value(css, ".labs-badge", "color"));
        Assert.Equal("1px solid var(--kron-warn-edge)", CssRules.Value(css, ".labs-badge", "border"));
        Assert.Equal("var(--kron-fs-label)", CssRules.Value(css, ".labs-badge", "font-size"));
        Assert.Equal("var(--kron-muted)", CssRules.Value(css, ".labs-about", "color"));
        Assert.Equal("var(--kron-muted)", CssRules.Value(css, ".labs-meta", "color"));
        // the table of every scenario is a card, as the History section and the diagnostics are: its rules would vanish
        // on the page's ground
        Assert.Equal("var(--kron-card)", CssRules.Value(css, ".labs-scenarios", "background"));
        Assert.Equal("1px solid var(--kron-hairline)", CssRules.Value(css, ".labs-scenarios", "border"));
        Assert.Equal("1px solid var(--kron-hairline-soft)", CssRules.Value(css, ".labs-scenario-table td", "border-bottom"));
        Assert.Equal("var(--kron-strip)", CssRules.Value(css, ".labs-scenario-table .labs-feature th", "background"));
        Assert.Equal("var(--kron-faint)", CssRules.Value(css, ".labs-stable", "color"));
        foreach (var rule in CssRules.Parse(css))
        foreach (var (property, value) in rule.Declarations)
            Assert.False(Literal().IsMatch(value), $"labs-styles.css: {rule.Selectors[0]} {{ {property}: {value} }} is a colour of its own");
    }

    /// <summary>The history views and the diagnostics are drawn by the run report and the labs page, and both carry the token
    /// sheet since S13, so a colour that falls back to a light value of its own would only ever hide a missing token.</summary>
    [Theory]
    [InlineData("history-styles.css")]
    [InlineData("report-diagnostics-styles.css")]
    public void A_sheet_carried_only_beside_the_token_sheet_has_no_colour_of_its_own_to_fall_back_on(string name)
    {
        var css = Regex.Replace(name == "history-styles.css" ? Stylesheets.HistoryStyleSheet : Stylesheets.ReportDiagnosticsStyleSheet,
            @"/\*.*?\*/", "", RegexOptions.Singleline);

        var fallbacks = Regex.Matches(css, @"var\(--[\w-]+\s*,[^)]*\)?").Select(m => m.Value).ToArray();
        Assert.True(fallbacks.Length == 0, $"{name} falls back on: {string.Join(" ", fallbacks)}");
        // a sheet that read no token at all would pass the line above
        Assert.Contains("var(--kron-card)", css);
    }

    // ── the violet theme ──

    [Fact]
    public void The_violet_theme_sets_the_reports_tokens_for_the_light_and_the_dark_theme()
    {
        var light = Block(Violet, ":root");
        var dark = Block(Violet, ":root[data-theme=\"dark\"]");
        var system = Block(Violet, ":root:not([data-theme])", "(prefers-color-scheme: dark)");

        // 4.x's violet, where the report's own tokens carry it, and the theme's own variables for the rest
        Assert.Equal("#ede9fe", light["--kron-hover"]);
        Assert.Equal("#6d28d9", light["--kron-accent"]);
        Assert.Equal("#ddd6fe", light["--violet-card"]);
        Assert.Equal("#8b5cf6", light["--violet-solid"]);
        Assert.Equal("#7c3aed", light["--violet-solid-hover"]);
        // a dark value for every one, the same in both dark blocks, as the token sheet has them
        Assert.Equal(light.Keys.Order(), dark.Keys.Order());
        Assert.Equal(dark.OrderBy(d => d.Key), system.OrderBy(d => d.Key));
        Assert.Equal("#2b2837", dark["--kron-hover"]);
        Assert.Equal("#a998eb", dark["--kron-accent"]);
        Assert.Equal("#1d1b25", dark["--violet-card"]);
        Assert.Equal("#7c3aed", dark["--violet-solid"]);
        Assert.Equal("#6d28d9", dark["--violet-solid-hover"]);
        // a name of the report's is one of its tokens; the theme's own begin --violet-
        var tokens = CssRules.For(Stylesheets.ReportTokensStyleSheet, ":root").Single().Declarations.Keys.ToHashSet();
        Assert.Contains(light.Keys, k => k.StartsWith("--kron-", StringComparison.Ordinal));
        Assert.All(light.Keys, k => Assert.True(k.StartsWith("--violet-", StringComparison.Ordinal) || tokens.Contains(k), $"{k} is no token of the token sheet"));
    }

    [Fact]
    public void Every_colour_the_violet_theme_draws_outside_its_token_blocks_is_a_variable()
    {
        var rules = CssRules.Parse(Violet).Where(r => !r.Selectors.Any(s => s.StartsWith(":root", StringComparison.Ordinal))).ToList();

        Assert.True(rules.Count >= 10, $"the violet theme has {rules.Count} rules outside its token blocks");
        foreach (var rule in rules)
        foreach (var (property, value) in rule.Declarations)
            Assert.True(!Literal().IsMatch(value) && value.Contains("var(--", StringComparison.Ordinal),
                $"{rule.Selectors[0]} {{ {property}: {value} }} is a colour of its own, the same in both themes");
    }

    /// <summary>The solid violet the theme keeps from 4.x for a chosen control, the passed step and the back-to-top button is
    /// drawn under <c>--kron-on-solid</c>: under the base sheet's ink a tick was 1.50:1 and the arrow 1.84:1, and a chosen
    /// tab hovered took the accent.</summary>
    [Fact]
    public void Every_solid_violet_the_theme_paints_carries_the_ink_made_for_it()
    {
        var solid = CssRules.Parse(Violet)
            .Where(r => r.Declarations.Any(d => d.Key is "background" or "background-color" && d.Value.StartsWith("var(--violet-solid", StringComparison.Ordinal)))
            .ToList();

        Assert.True(solid.Count >= 6, $"{solid.Count} rules paint a solid violet");
        foreach (var rule in solid.Where(r => !r.Selectors.All(s => s.StartsWith(".back-to-top:hover", StringComparison.Ordinal))))
            Assert.True(rule.Declarations.GetValueOrDefault("color") == "var(--kron-on-solid)",
                $"{rule.Selectors[0]} paints a solid violet under {rule.Declarations.GetValueOrDefault("color") ?? "the ink it inherits"}");
        Assert.Equal("var(--kron-on-solid)", CssRules.Value(Violet, ".details-radio-btn.toggle-btn.details-active::before", "background"));
    }

    // ── the search help ──

    // the syntax tips, which the query builder holds since the search left the box (plans/SEARCH_PLAN.md 4.3.5)
    [Fact]
    public void The_search_help_is_drawn_in_the_tokens_and_the_violet_leaves_it_to_them()
    {
        var css = Stylesheets.SearchStyleSheet;
        string[] parts = [".search-tips-toggle", ".search-tips-toggle:hover", ".search-tips", ".search-tips-table th",
            ".search-tips-table td", ".search-tips code", ".search-tips-note", ".search-tips kbd"];

        foreach (var selector in parts)
        {
            var rules = CssRules.For(css, selector);
            Assert.True(rules.Count > 0, $"search-styles.css has no {selector} rule");
            foreach (var (property, value) in rules.SelectMany(r => r.Declarations))
                Assert.False(Literal().IsMatch(value), $"{selector} {{ {property}: {value} }} is a colour of its own, the same in both themes");
        }
        Assert.Equal("var(--kron-code-bg)", CssRules.Value(css, ".search-tips code", "background"));
        Assert.DoesNotContain(CssRules.Parse(Stylesheets.HtmlReportStyleSheet), r => r.Selectors.Any(s => s.StartsWith(".search-help", StringComparison.Ordinal)));
        Assert.DoesNotContain(CssRules.Parse(Violet), r => r.Selectors.Any(s => s.StartsWith(".search-", StringComparison.Ordinal)));
    }
}
