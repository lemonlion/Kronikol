namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The report's diagnostics on the theme's tokens (5.0.0, plans/OPTION_C_PLAN.md S11): the card and its rule in the
/// theme's colours, light and dark; a render failure in the failure colour, unlike a malformed line; one rule between
/// items, where each item carried a rule under it; and a heading that is no disclosure in its tab but stays one on the
/// labs page.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class DiagnosticsCardTests : PlaywrightTestBase
{
    public DiagnosticsCardTests(PlaywrightFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_diagnostics_card_takes_the_theme(string theme)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, $"DiagnosticsCard-{theme}.html"));
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await OpenReportTabAsync("diagnostics");

        var r = await Page.Locator("details.report-diagnostics").First.EvaluateAsync<string[]>("""
            el => {
                const tok = n => { const p = document.createElement('div'); p.style.color = 'var(' + n + ')'; document.body.appendChild(p);
                                   const c = getComputedStyle(p).color; p.remove(); return c; };
                const cs = getComputedStyle(el);
                const fail = el.querySelector('.report-diagnostic-kind-renderfailure'), line = el.querySelector('.report-diagnostic-kind-malformedline');
                const items = el.querySelectorAll('.report-diagnostics-list > li');
                const style = (i, side) => items.length > i ? getComputedStyle(items[i])['border' + side + 'Style'] : 'missing';
                return [cs.backgroundColor, tok('--kron-card'), cs.borderTopColor, tok('--kron-hairline'),
                        fail ? getComputedStyle(fail).color : 'none', tok('--kron-fail'), line ? getComputedStyle(line).color : 'none',
                        style(0, 'Top'), style(1, 'Top'), style(0, 'Bottom'),
                        getComputedStyle(el.querySelector(':scope > summary')).listStyleType, getComputedStyle(el.querySelector(':scope > summary')).cursor];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
        Assert.NotEqual(r[4], r[6]);
        Assert.Equal("none", r[7]);
        Assert.Equal("solid", r[8]);
        Assert.Equal("none", r[9]);
        // in its tab the heading is no disclosure: no marker, no pointer
        Assert.Equal("none", r[10]);
        Assert.Equal("default", r[11]);
    }

    // The labs page has no tabs: there the diagnostics stay a disclosure. Since it carries the token sheet (5.0.0,
    // plans/OPTION_C_PLAN.md S13) they are drawn in the theme's tokens there too, where they kept their 4.x colours.
    [Fact]
    public async Task On_the_labs_page_the_diagnostics_stay_a_disclosure_in_the_themes_tokens()
    {
        ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, "DiagnosticsCard-labs-report.html", labsPageFileName: "DiagnosticsCard-labs.html");
        await Page.GotoAsync(new Uri(Path.Combine(TempDir, "DiagnosticsCard-labs.html")).AbsoluteUri);

        var r = await Page.Locator("details.report-diagnostics > summary").First.EvaluateAsync<string[]>("""
            s => {
                const p = document.createElement('div'); p.style.color = 'var(--kron-card)'; document.body.appendChild(p);
                const card = getComputedStyle(p).color; p.remove();
                const cs = getComputedStyle(s); return [cs.listStyleType, cs.cursor, getComputedStyle(s.parentElement).backgroundColor, card];
            }
            """);

        Assert.StartsWith("disclosure", r[0]);
        Assert.Equal("pointer", r[1]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal("rgb(255, 255, 255)", r[2]);
    }
}
