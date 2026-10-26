using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The labs page and the violet theme in a browser, light and dark (5.0.0, plans/OPTION_C_PLAN.md S13, Q3): the labs page
/// follows the system's setting until the reader picks a theme with the switch on its title line, keeps the pick over a
/// reload, and paints its parts in the theme's tokens; a Specifications report opened dark is dark and still violet; a
/// chosen tab of the violet theme keeps white ink when hovered; the violet reaches the controls its 4.x rules never named;
/// and the search help reads in every theme, where the default theme drew it light on a dark page and the violet dark on
/// a light one.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ThemedPagesTests : PlaywrightTestBase
{
    public ThemedPagesTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string White = "rgb(255, 255, 255)";

    private ILocator Switch => Page.Locator("button.theme-toggle");

    private static ColorScheme Scheme(string scheme) => scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;

    private Task<string?> ThemeAsync() =>
        Page.EvaluateAsync<string?>("() => document.documentElement.getAttribute('data-theme')");

    /// <summary>The labs page the every-section fixture writes beside its report, opened.</summary>
    private async Task OpenLabsPageAsync(string name)
    {
        ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, name + "-report.html", labsPageFileName: name + ".html");
        await Page.GotoReportAsync(new Uri(Path.Combine(TempDir, name + ".html")).AbsoluteUri);
        await Page.Locator("header.labs-header").WaitForAsync();
    }

    private async Task OpenVioletRunReportAsync(string fileName)
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, fileName, _ => { },
            stylesheet: Stylesheets.VioletThemeStyleSheet));
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    // A token as the page resolves it: a probe element turns it into the rgb() form getComputedStyle gives.
    private const string Token =
        "const tok = n => { const p = document.createElement('div'); p.style.color = 'var(' + n + ')'; document.body.appendChild(p); "
        + "const c = getComputedStyle(p).color; p.remove(); return c; };";

    [Theory]
    [InlineData("light", "rgb(235, 238, 244)", "rgb(255, 255, 255)")]
    [InlineData("dark", "rgb(14, 19, 26)", "rgb(23, 29, 38)")]
    public async Task The_labs_page_follows_the_systems_setting_and_paints_its_parts_in_the_tokens(string scheme, string ground, string card)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = Scheme(scheme) });
        await OpenLabsPageAsync($"ThemedLabs-{scheme}");
        await Page.Locator("details.labs-scenarios > summary").ClickAsync();
        await Expect(Page.Locator("tr.labs-feature > th").First).ToBeVisibleAsync();

        var r = await Page.EvaluateAsync<string[]>($$"""
            () => {
                {{Token}}
                const bg = s => getComputedStyle(document.querySelector(s)).backgroundColor;
                const ink = s => getComputedStyle(document.querySelector(s)).color;
                return [document.documentElement.getAttribute('data-theme') || 'none',
                        getComputedStyle(document.body).backgroundColor, tok('--kron-ground'),
                        bg('#history-section'), bg('details.report-diagnostics'), bg('details.labs-scenarios'), tok('--kron-card'),
                        ink('header.labs-header h1'), tok('--kron-ink'),
                        ink('.labs-meta'), tok('--kron-muted'),
                        bg('.labs-badge'), tok('--kron-warn-soft'),
                        bg('tr.labs-feature > th'), tok('--kron-strip')];
            }
            """);

        Assert.Equal(scheme, r[0]);
        Assert.Equal(ground, r[1]);
        Assert.Equal(r[2], r[1]);
        Assert.Equal(card, r[3]);
        Assert.Equal(r[6], r[3]);
        Assert.Equal(r[6], r[4]);
        Assert.Equal(r[6], r[5]);
        Assert.Equal(r[8], r[7]);
        Assert.Equal(r[10], r[9]);
        Assert.Equal(r[12], r[11]);
        Assert.Equal(r[14], r[13]);
        await Expect(Switch).ToHaveAttributeAsync("aria-pressed", scheme == "dark" ? "true" : "false");
    }

    [Fact]
    public async Task The_switch_on_the_labs_page_flips_its_theme_and_the_pick_holds_over_a_reload()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await OpenLabsPageAsync("ThemedLabs-switch");
        await Expect(Switch).ToHaveAttributeAsync("aria-pressed", "false");

        await Switch.ClickAsync();
        Assert.Equal("dark", await ThemeAsync());
        await Expect(Switch).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Switch).ToHaveAttributeAsync("title", "Switch to the light theme");
        await Expect(Page.Locator("body")).ToHaveCSSAsync("background-color", "rgb(14, 19, 26)");

        await Page.ReloadReportAsync();
        await Page.Locator("header.labs-header").WaitForAsync();
        Assert.Equal("dark", await ThemeAsync());
        await Expect(Switch).ToHaveAttributeAsync("aria-pressed", "true");
    }

    /// <summary>The Specifications report's violet in both themes: the cards and a chosen control, where 4.x's literal colours
    /// painted the light theme's violet on a dark page.</summary>
    [Theory]
    [InlineData("light", "rgb(235, 238, 244)", "rgb(221, 214, 254)", "rgb(139, 92, 246)")]
    [InlineData("dark", "rgb(14, 19, 26)", "rgb(29, 27, 37)", "rgb(124, 58, 237)")]
    public async Task The_specifications_report_is_violet_in_the_light_and_the_dark_theme(string scheme, string ground, string card, string chosen)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = Scheme(scheme) });
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithWideHeader(TempDir, OutputDir, $"ThemedSpecs-{scheme}.html",
            specifications: true, internalFlowTracking: false));
        await Page.Locator("details.feature").First.WaitForAsync();
        Assert.Equal(scheme, await ThemeAsync());

        await OpenFilterRowsAsync();
        var show = Page.Locator(".filtering-box .happy-seg[data-hp='off']").First;
        await Expect(show).ToHaveAttributeAsync("aria-pressed", "true");
        await Page.Mouse.MoveAsync(0, 0);
        await Expect(Page.Locator("body")).ToHaveCSSAsync("background-color", ground);
        await Expect(Page.Locator("details.feature").First).ToHaveCSSAsync("background-color", card);
        // the box sits at the bar's top since plans/SEARCH_PLAN.md F2, and keeps its violet there
        await Expect(Page.Locator(".filtering-box").First).ToHaveCSSAsync("background-color", card);
        await Expect(show).ToHaveCSSAsync("background-color", chosen);
        await Expect(show).ToHaveCSSAsync("color", White);
    }

    /// <summary>A chosen diagram tab is solid violet under white ink; hovered it darkens and keeps the ink, where the base
    /// sheet's hover rule, the more specific, gave it the accent on solid violet.</summary>
    [Theory]
    [InlineData("light", "rgb(124, 58, 237)")]
    [InlineData("dark", "rgb(109, 40, 217)")]
    public async Task A_hovered_chosen_diagram_tab_keeps_its_white_ink_on_a_darker_violet(string scheme, string hovered)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = Scheme(scheme) });
        await OpenVioletRunReportAsync($"ThemedTabs-{scheme}.html");
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => document.querySelectorAll('details.example-diagrams').forEach(d => d.open = true)");

        var chosen = Page.Locator("details.example-diagrams .diagram-toggle-btn.diagram-toggle-active").First;
        await Expect(chosen).ToBeVisibleAsync();
        await Expect(chosen).ToHaveCSSAsync("color", White);
        await chosen.HoverAsync();
        await Expect(chosen).ToHaveCSSAsync("background-color", hovered);
        await Expect(chosen).ToHaveCSSAsync("color", White);
    }

    /// <summary>The theme sets the report's selected tokens, so a control 4.x's rules never named, the bar's Filters open
    /// (More filters until plans/SEARCH_PLAN.md F5), is violet too. (A report tab hovered takes its hover token:
    /// ToolbarStylingTests' header hovers.)</summary>
    [Fact]
    public async Task The_violet_reaches_the_controls_its_4x_rules_never_named()
    {
        await OpenVioletRunReportAsync("ThemedReach.html");

        await OpenFilterRowsAsync();
        var filters = Page.Locator(".nav-filters-toggle").First;
        await Expect(filters).ToHaveAttributeAsync("aria-expanded", "true");
        await Page.Mouse.MoveAsync(0, 0);
        await Expect(filters).ToHaveCSSAsync("background-color", "rgb(196, 181, 253)");
        await Expect(filters).ToHaveCSSAsync("color", "rgb(91, 33, 182)");
    }

    /// <summary>The search help in the default theme and the violet, light and dark: its text on its ground in the page's
    /// ink and its code in chips, each readable, where the default theme drew a light panel under the dark theme's light ink
    /// and the violet a dark panel under the light theme's dark ink. Since plans/SEARCH_PLAN.md 4.3.5 the help is the query
    /// builder's tips, on the builder's card.</summary>
    [Theory]
    [InlineData(false, "light")]
    [InlineData(false, "dark")]
    [InlineData(true, "light")]
    [InlineData(true, "dark")]
    public async Task The_search_help_reads_in_every_theme(bool violet, string scheme)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = Scheme(scheme) });
        await Page.GotoReportAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, $"ThemedHelp-{violet}-{scheme}.html", _ => { },
            stylesheet: violet ? Stylesheets.VioletThemeStyleSheet : null));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("#search-button").ClickAsync();
        await Page.Locator(".search-tips-toggle").ClickAsync();
        var panel = Page.Locator("#search-tips");
        await Expect(panel).ToBeVisibleAsync();
        var r = await panel.EvaluateAsync<string[]>($$"""
            panel => {
                {{Token}}
                const card = panel.closest('.search-panel'), cell = panel.querySelector('.search-tips-table td'), code = panel.querySelector('.search-tips-table code');
                return [getComputedStyle(card).backgroundColor, tok('--kron-card'), getComputedStyle(cell).color, tok('--kron-ink'),
                        getComputedStyle(code).backgroundColor, tok('--kron-code-bg'), getComputedStyle(code).color];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
        Assert.True(Contrast(r[2], r[0]) >= 4.5, $"the help's text {r[2]} on {r[0]}: {Contrast(r[2], r[0]):F2}:1");
        Assert.True(Contrast(r[6], r[4]) >= 4.5, $"a code chip's text {r[6]} on {r[4]}: {Contrast(r[6], r[4]):F2}:1");
    }

    /// <summary>WCAG's contrast ratio of two opaque colours written as getComputedStyle writes them.</summary>
    private static double Contrast(string a, string b)
    {
        static double Luminance(string rgb)
        {
            var c = Regex.Matches(rgb, @"\d+(?:\.\d+)?").Take(3)
                .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture) / 255)
                .Select(v => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4))
                .ToArray();
            return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
        }
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
