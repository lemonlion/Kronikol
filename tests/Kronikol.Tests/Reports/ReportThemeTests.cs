using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The report's colours as tokens and its light and dark themes (5.0.0, plans/OPTION_C_PLAN.md S1, D1): the token sheet
/// opens the page's styles, the theme is chosen before anything is styled, and the title line carries the switch.
/// </summary>
public class ReportThemeTests
{
    private static string GenerateReport(string fileName, string title = "Test")
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = "t1", DisplayName = "Create order", Result = ExecutionResult.Passed }]
            }
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, title, true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    private static string Block(string css, string opener)
    {
        var start = css.IndexOf(opener + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no block {opener}");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        return css[(open + 1)..close];
    }

    private static Dictionary<string, string> Declarations(string block) =>
        Regex.Matches(Regex.Replace(block, @"/\*.*?\*/", "", RegexOptions.Singleline), @"([-\w]+)\s*:\s*([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());

    [Fact]
    public void The_token_sheet_opens_the_first_style_block_ahead_of_the_base_sheet()
    {
        var html = GenerateReport("Theme_TokensFirst.html");
        var firstStyle = html.IndexOf("<style>", StringComparison.Ordinal);
        var tokens = html.IndexOf(Stylesheets.ReportTokensStyleSheet, StringComparison.Ordinal);
        var baseSheet = html.IndexOf(Stylesheets.HtmlReportStyleSheet, StringComparison.Ordinal);

        Assert.True(firstStyle >= 0 && tokens > firstStyle, "the token sheet is in the first style block");
        Assert.True(string.IsNullOrWhiteSpace(html[(firstStyle + "<style>".Length)..tokens]), "nothing comes before it");
        Assert.True(baseSheet > tokens, "the base sheet follows it");
    }

    [Fact]
    public void The_theme_is_chosen_before_the_first_style_block()
    {
        var html = GenerateReport("Theme_Boot.html");
        var boot = html.IndexOf("<script id=\"kronikol-theme-boot\">", StringComparison.Ordinal);
        var firstStyle = html.IndexOf("<style>", StringComparison.Ordinal);

        Assert.True(boot >= 0 && boot < firstStyle, "the boot script comes before any style");
        var script = html[boot..html.IndexOf("</script>", boot, StringComparison.Ordinal)];
        Assert.Contains("localStorage.getItem('kronikol.theme')", script);
        Assert.Contains("(prefers-color-scheme: dark)", script);
        Assert.Contains("setAttribute('data-theme', t)", script);
    }

    [Fact]
    public void Both_dark_blocks_hold_the_same_values_and_every_dark_token_has_a_light_value()
    {
        var css = Stylesheets.ReportTokensStyleSheet.Replace("\r\n", "\n");
        var light = Declarations(Block(css, "\n:root"));
        var dark = Declarations(Block(css, ":root[data-theme=\"dark\"]"));
        var system = Declarations(Block(css, ":root:not([data-theme])"));

        Assert.Equal("light", light["color-scheme"]);
        Assert.Equal("dark", dark["color-scheme"]);
        Assert.Equal(dark.OrderBy(d => d.Key), system.OrderBy(d => d.Key));
        Assert.Empty(dark.Keys.Except(light.Keys));
        Assert.Contains("@media (prefers-color-scheme: dark) {\n    :root:not([data-theme]) {", css);
    }

    [Fact]
    public void Hidden_wins_over_any_display_rule()
    {
        Assert.Contains("[hidden] { display: none !important; }", Stylesheets.ReportTokensStyleSheet);
    }

    [Fact]
    public void The_title_line_holds_the_title_and_the_theme_switch()
    {
        var html = GenerateReport("Theme_TitleLine.html", "Checkout <run>");

        Assert.Contains("<div class=\"title-line\"><h1>Checkout &lt;run&gt;</h1><button type=\"button\" class=\"theme-toggle\" "
                        + "aria-label=\"Dark theme\" aria-pressed=\"false\" title=\"Switch to the dark theme\">", html);
        Assert.Contains("<svg class=\"theme-icon theme-moon\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">", html);
        Assert.Contains("<svg class=\"theme-icon theme-sun\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">", html);
    }

    [Fact]
    public void The_switch_script_remembers_the_choice_and_follows_the_system_until_then()
    {
        var html = GenerateReport("Theme_Switch.html");
        var at = html.IndexOf("<script id=\"kronikol-theme-switch\">", StringComparison.Ordinal);

        Assert.True(at >= 0, "the switch script is in the page");
        var script = html[at..html.IndexOf("</script>", at, StringComparison.Ordinal)];
        Assert.Contains("localStorage.setItem('kronikol.theme', t)", script);
        Assert.Contains("addEventListener('change'", script);
        Assert.Contains("closest('.theme-toggle')", script);
    }

    [Fact]
    public void The_page_takes_its_ground_ink_and_type_from_tokens()
    {
        var css = Stylesheets.HtmlReportStyleSheet.Replace("\r\n", "\n");
        var body = Declarations(Block(css, "body"));

        Assert.Equal("var(--kron-ground)", body["background"]);
        Assert.Equal("var(--kron-ink)", body["color"]);
        Assert.Equal("var(--kron-font)", body["font-family"]);
    }
}
