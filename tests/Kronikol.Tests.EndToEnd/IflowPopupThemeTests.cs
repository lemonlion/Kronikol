namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The internal-flow popup on the theme's tokens (5.0.0, plans/OPTION_C_PLAN.md S11): its card, ink and heading in the
/// theme's colours, light and dark, over an overlay that darkens further in the dark theme; its two views as tabs in the
/// accent and the quiet ink.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class IflowPopupThemeTests : PlaywrightTestBase
{
    public IflowPopupThemeTests(PlaywrightFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("light", "0.4")]
    [InlineData("dark", "0.6")]
    public async Task The_popup_takes_the_theme(string theme, string overlayAlpha)
    {
        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, $"IflowPopupTheme-{theme}.html", withFlow: 1, withoutFlow: 0);
        await Page.GotoAsync(uri);
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await Page.WaitForFunctionAsync("() => !!document.querySelector('.plantuml-browser:not(.iflow-diagram) svg')",
            null, new() { PollingInterval = 200 });
        await Page.EvaluateAsync("""
            () => Array.from(document.querySelectorAll('.plantuml-browser:not(.iflow-diagram) svg text'))
                .find(t => t.textContent.includes('with-flow-0'))
                .dispatchEvent(new MouseEvent('click', { bubbles: true }))
            """);
        var popup = Page.Locator(".iflow-popup");
        await Expect(popup).ToBeVisibleAsync();

        var r = await popup.EvaluateAsync<string[]>("""
            el => {
                const tok = n => { const p = document.createElement('div'); p.style.color = 'var(' + n + ')'; document.body.appendChild(p);
                                   const c = getComputedStyle(p).color; p.remove(); return c; };
                const cs = getComputedStyle(el), h = el.querySelector('h3'), close = el.querySelector('.iflow-popup-close');
                return [cs.backgroundColor, tok('--kron-card'), cs.color, tok('--kron-ink'), getComputedStyle(h).color,
                        getComputedStyle(close).color, tok('--kron-muted'), getComputedStyle(document.querySelector('.iflow-overlay')).backgroundColor];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[3], r[4]);
        Assert.Equal(r[6], r[5]);
        Assert.Equal($"rgba(0, 0, 0, {overlayAlpha})", r[7]);
    }

    // The popup's two views are tabs on the theme's tokens: set on a page that has no token sheet, the chosen tab takes
    // the accent and its underline, the other the quiet ink, and neither is filled.
    [Fact]
    public async Task The_popups_tabs_take_the_accent_and_the_quiet_ink()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true)));
        await Page.EvaluateAsync("""
            () => { const s = document.documentElement.style; s.setProperty('--kron-accent', 'rgb(1, 2, 3)'); s.setProperty('--kron-ink-quiet', 'rgb(4, 5, 6)'); }
            """);
        await Page.Locator("#trigger-seg-1").ClickAsync();
        var popup = Page.Locator(".iflow-popup");
        await Expect(popup).ToBeVisibleAsync();

        var r = await popup.EvaluateAsync<string[]>("""
            el => {
                const a = getComputedStyle(el.querySelector('.iflow-toggle-active')), b = getComputedStyle(el.querySelector('.iflow-toggle-btn:not(.iflow-toggle-active)'));
                return [a.color, a.boxShadow, a.backgroundColor, b.color, b.backgroundColor];
            }
            """);

        Assert.Equal("rgb(1, 2, 3)", r[0]);
        Assert.StartsWith("rgb(1, 2, 3) 0px -2px 0px 0px inset", r[1]);
        Assert.Equal("rgba(0, 0, 0, 0)", r[2]);
        Assert.Equal("rgb(4, 5, 6)", r[3]);
        Assert.Equal("rgba(0, 0, 0, 0)", r[4]);
    }
}
