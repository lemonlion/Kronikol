namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The flame chart in the diagrams' manner (5.0.0, plans/OPTION_C_PLAN.md S11, D44 and the owner's round 17 item 10): a
/// source takes one of eight hues (blue, violet, teal, rose, green, amber, purple, coral), drawn as a pale fill with a 3px
/// edge in a stronger shade and ink text, 20px rows 2px apart, in the light and the dark theme, where any hue at 60%
/// saturation was lightened with depth.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class FlameChartPaletteTests : DiagramNotePlaywrightBase
{
    public FlameChartPaletteTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly int[] Hues = [212, 262, 188, 338, 152, 32, 290, 8];

    private async Task OpenFlameAsync(string fileName, string theme)
    {
        await Page.SetViewportSizeAsync(1400, 800);
        await Page.GotoAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, fileName, _ => { }));
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await Page.Locator("details.example-diagrams:has(> .scenario-settings) > .diagram-toggle-tabs > .diagram-toggle-btn[data-dtype='flame']").First.ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.iflow-flame-bar')).some(b => b.offsetParent !== null)", null,
            new() { Timeout = 30000, PollingInterval = 200 });
    }

    // The bar's lightness (sRGB, weighted as for luminance, in percent) holds each theme to its own shades: a dark page drawing the
    // light shades would still match the tokens it reads, so the expected colours alone could not see it.
    [Theory]
    [InlineData("light", 60, 100)]
    [InlineData("dark", 0, 30)]
    public async Task A_flame_bar_is_a_pale_fill_with_an_edge_in_its_sources_hue(string theme, int low, int high)
    {
        await OpenFlameAsync($"FlamePalette-{theme}.html", theme);

        var r = await Page.EvaluateAsync<string[]>("""
            () => {
                const bar = Array.from(document.querySelectorAll('.iflow-flame-bar')).find(b => b.offsetParent !== null);
                const fh = bar.style.getPropertyValue('--fh').trim();
                const cs = getComputedStyle(bar), root = getComputedStyle(document.documentElement);
                const probe = document.createElement('div'); document.body.appendChild(probe);
                const paint = v => { probe.style.color = ''; probe.style.color = v; return getComputedStyle(probe).color; };
                const fill = paint('hsl(' + fh + ' ' + root.getPropertyValue('--dg-flame-fill') + ')');
                const edge = paint('hsl(' + fh + ' ' + root.getPropertyValue('--dg-flame-edge') + ')');
                const ink = paint('var(--kron-ink)');
                probe.remove();
                return [fh, cs.backgroundColor, fill, cs.borderLeftColor, edge, cs.borderLeftWidth, cs.color, ink,
                        String(Math.round(bar.getBoundingClientRect().height)), cs.marginTop,
                        String((c => (0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]) / 255)(cs.backgroundColor.match(/\d+(\.\d+)?/g).map(Number)))];
            }
            """);

        Assert.True(int.TryParse(r[0], out var hue) && Hues.Contains(hue), $"the bar's hue was '{r[0]}'");
        Assert.Equal(r[2], r[1]);
        Assert.Equal(r[4], r[3]);
        Assert.Equal("3px", r[5]);
        Assert.Equal(r[7], r[6]);
        Assert.Equal("20", r[8]);
        Assert.Equal("2px", r[9]);
        Assert.InRange(100 * double.Parse(r[10], System.Globalization.CultureInfo.InvariantCulture), low, high);
    }
}
