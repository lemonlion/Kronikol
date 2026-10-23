using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The run band's layout (5.0.0, plans/OPTION_C_PLAN.md S2, D3 to D5): the ring, the summary and the CI zone on one row
/// while they fit, the CI zone under the summary when they do not, decided on measured widths; the filter box on the row
/// after the band.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class RunBandLayoutTests : PlaywrightTestBase
{
    public RunBandLayoutTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1400;
    protected override int ViewportHeight => 900;

    private string GenerateBandReport(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "a", DisplayName = "Create order", Result = ExecutionResult.Passed },
                    new Scenario { Id = "b", DisplayName = "Reject order", Result = ExecutionResult.Failed, ErrorMessage = "boom" }
                ]
            }
        };
        var ci = new CiMetadata(CiEnvironment.GitHubActions, "322", "main", "0ee43e8c0b5d3e85a4a099a20ee4ce37305b6b3f",
            "https://github.com/lemonlion/BreakfastProvider/actions/runs/38868550876", "lemonlion/BreakfastProvider", "38868550876");
        var start = new DateTime(2026, 10, 16, 17, 27, 11, DateTimeKind.Utc);
        var path = ReportGenerator.GenerateHtmlReport([], features, start, start.AddSeconds(5), null, fileName, "Band", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, ciMetadata: ci);
        var target = Path.Combine(TempDir, fileName);
        File.Copy(path, target, true);
        return new Uri(target).AbsoluteUri;
    }

    private Task<double[]> TopsAsync() =>
        Page.EvaluateAsync<double[]>(
            "() => ['.run-ring', '.run-summary', '.run-ci', '.filtering-box'].map(s => document.querySelector(s).getBoundingClientRect().top)");

    [Fact]
    public async Task On_a_wide_page_the_ring_the_summary_and_ci_share_one_row_and_the_filters_follow()
    {
        await Page.GotoReportAsync(GenerateBandReport("Band_Wide.html"));
        await Page.Locator(".run-band").WaitForAsync();

        Assert.False(await Page.Locator(".run-band").EvaluateAsync<bool>("b => b.classList.contains('ci-below')"));
        var summary = await Page.Locator(".run-summary").BoundingBoxAsync();
        var ci = await Page.Locator(".run-ci").BoundingBoxAsync();
        Assert.True(ci!.X > summary!.X + summary.Width, "the CI zone is to the right of the summary");
        Assert.True(Math.Abs(ci.Y - summary.Y) < 1, $"the zones share a row: {summary.Y} and {ci.Y}");
        var band = await Page.Locator(".run-band").BoundingBoxAsync();
        var box = await Page.Locator("#filtering-box").BoundingBoxAsync();
        Assert.True(box!.Y >= band!.Y + band.Height, "the filter box is on the row after the band");
    }

    [Fact]
    public async Task On_a_narrow_page_the_ci_zone_moves_under_the_summary_and_back()
    {
        await Page.SetViewportSizeAsync(820, 900);
        await Page.GotoReportAsync(GenerateBandReport("Band_Narrow.html"));
        await Page.Locator(".run-band.ci-below").WaitForAsync(new() { Timeout = 5000 });

        var summary = await Page.Locator(".run-summary").BoundingBoxAsync();
        var ci = await Page.Locator(".run-ci").BoundingBoxAsync();
        Assert.True(ci!.Y >= summary!.Y + summary.Height, "the CI zone is under the summary");
        Assert.True(Math.Abs(ci.X - summary.X) < 1, $"and lines up with it: {summary.X} and {ci.X}");

        await Page.SetViewportSizeAsync(1400, 900);
        await Page.WaitForFunctionAsync("() => !document.querySelector('.run-band').classList.contains('ci-below')", null,
            new() { PollingInterval = 200, Timeout = 5000 });
    }

    /// <summary>A reader's text spacing (WCAG 1.4.12), applied after the page loaded as an extension applies it, widens
    /// the zones past the row: the band measures again and moves the CI zone under the summary, and back when the spacing
    /// goes. Measured once at load, the CI zone wrapped under the summary unmarked, at 1,280 to 1,400 px.</summary>
    [Fact]
    public async Task Text_spacing_applied_after_load_moves_the_ci_zone_under_the_summary_and_back()
    {
        await Page.GotoReportAsync(GenerateBandReport("Band_Spacing.html"));
        await Page.Locator(".run-band").WaitForAsync();
        Assert.False(await Page.Locator(".run-band").EvaluateAsync<bool>("b => b.classList.contains('ci-below')"));

        await Page.EvaluateAsync("""
            () => {
                const style = document.createElement('style');
                style.id = 'spacing';
                style.textContent = '*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}.run-stat dd{font-size:20px !important}';
                document.head.appendChild(style);
            }
            """);
        await Page.WaitForFunctionAsync("() => document.querySelector('.run-band').classList.contains('ci-below')", null,
            new() { PollingInterval = 200, Timeout = 5000 });

        await Page.EvaluateAsync("() => document.getElementById('spacing').remove()");
        await Page.WaitForFunctionAsync("() => !document.querySelector('.run-band').classList.contains('ci-below')", null,
            new() { PollingInterval = 200, Timeout = 5000 });
    }

    /// <summary>D5: the filter box on its own row is a card like the band, its title a label, and its export buttons
    /// text buttons that take the hover tint, in both themes.</summary>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_filter_box_is_a_card_like_the_band_with_text_export_buttons(string theme)
    {
        await Context.AddInitScriptAsync($"try {{ localStorage.setItem('kronikol.theme', '{theme}'); }} catch (e) {{ }}");
        await Page.GotoReportAsync(GenerateBandReport($"Band_FilterCard_{theme}.html"));
        await Page.Locator("#filtering-box").WaitForAsync();

        var styles = await Page.EvaluateAsync<string[]>(
            """
            () => {
                const token = (name, prop) => { const p = document.createElement('div'); p.style.setProperty(prop, `var(${name})`); document.body.appendChild(p); const v = getComputedStyle(p).getPropertyValue(prop); p.remove(); return v; };
                const box = getComputedStyle(document.querySelector('#filtering-box')), band = getComputedStyle(document.querySelector('.run-band'));
                const title = getComputedStyle(document.querySelector('.filtering-box-header h2'));
                const button = getComputedStyle(document.querySelector('.filtering-box-export .export-btn'));
                return [box.backgroundColor, token('--kron-card', 'background-color'), box.borderTopColor, token('--kron-hairline', 'border-top-color'),
                        box.borderTopLeftRadius, band.borderTopLeftRadius, box.boxShadow, band.boxShadow,
                        title.textTransform, title.color, token('--kron-muted', 'color'), button.backgroundColor, button.borderTopColor];
            }
            """);
        Assert.Equal(styles[1], styles[0]);
        Assert.Equal(styles[3], styles[2]);
        Assert.Equal(styles[5], styles[4]);
        Assert.Equal(styles[7], styles[6]);
        Assert.Equal("uppercase", styles[8]);
        Assert.Equal(styles[10], styles[9]);
        Assert.Equal("rgba(0, 0, 0, 0)", styles[11]);
        Assert.Equal("rgba(0, 0, 0, 0)", styles[12]);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_ring_takes_its_colours_from_the_tokens(string theme)
    {
        await Context.AddInitScriptAsync($"try {{ localStorage.setItem('kronikol.theme', '{theme}'); }} catch (e) {{ }}");
        await Page.GotoReportAsync(GenerateBandReport($"Band_Ring_{theme}.html"));
        await Page.Locator(".run-ring svg").WaitForAsync();

        var colours = await Page.EvaluateAsync<string[]>(
            """
            () => {
                const token = (name, prop) => { const p = document.createElement('div'); p.style.setProperty(prop, `var(${name})`); document.body.appendChild(p); const v = getComputedStyle(p).getPropertyValue(prop); p.remove(); return v; };
                const s = sel => getComputedStyle(document.querySelector(sel));
                return [s('.run-ring .ring-pass').stroke, token('--kron-pass-ring', 'color'), s('.run-ring .ring-fail').stroke, token('--kron-fail', 'color'),
                        s('.run-ring .ring-rate').fill, token('--kron-ink', 'color')];
            }
            """);
        Assert.Equal(colours[1], colours[0]);
        Assert.Equal(colours[3], colours[2]);
        Assert.Equal(colours[5], colours[4]);
    }
}
