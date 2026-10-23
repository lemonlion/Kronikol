using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// How what a run report holds outside its features fits the window (plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.md,
/// the audit). No feature clips these sections, so a long token that does not break scrolls the whole page
/// sideways: a failure cluster's message, a test method's full name in the cluster's list and in the
/// History section, the History section's branch, a dependency chip named after a typed client. Each
/// breaks inside its box. Under WCAG text spacing, just above the breakpoint, the 4.x filtering box of a
/// report with a long branch was narrower than the search help's table, which scrolls inside its panel, and
/// than one export button, which wraps its label inside the box. 5.0.0's smaller type (plans/OPTION_C_PLAN.md
/// S1) leaves the box wider than all three at every width, so those facts hold the box narrow, as a CustomCss
/// or a narrow frame can. <see cref="ViewportSweepTests"/> proves the page never scrolls sideways at any width;
/// these facts pin which way each kind of content fits.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ReportSectionWidthTests : PlaywrightTestBase
{
    public ReportSectionWidthTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>WCAG 1.4.12's text spacing: line height 1.5, letter spacing 0.12 em, word spacing 0.16 em,
    /// paragraph spacing 2 em.</summary>
    private const string TextSpacing =
        "*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}";

    private async Task Open(int width, bool textSpacing = false)
    {
        await Page.SetViewportSizeAsync(width, 900);
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, $"EverySection_{width}{(textSpacing ? "_spaced" : "")}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        if (textSpacing)
            await Page.AddStyleTagAsync(new() { Content = TextSpacing });
    }

    /// <summary>Holds the filtering box at <paramref name="maxWidth"/> px, narrower than the content a fact needs
    /// it narrower than.</summary>
    private Task HoldFilteringBox(int maxWidth) =>
        Page.EvaluateAsync("w => { document.querySelector('.filtering-box').style.maxWidth = w + 'px'; }", maxWidth);

    private Task<int> PageOverflow() =>
        Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

    [Theory]
    [InlineData(320, ".failure-cluster > summary", "System.InvalidOperationException", 0)]
    [InlineData(320, "a.failure-cluster-scenario-link", "OrderReconciliationTests", 0)]
    [InlineData(320, "a.history-link", "OrderReconciliationTests", 0)]
    [InlineData(320, ".history-meta code", "dependabot/nuget", 0)]
    [InlineData(1200, "button.dependency-toggle", "NightlySettlementLedgerHttpClient", 220)]
    public async Task A_long_token_outside_the_features_breaks_inside_its_box(int width, string selector, string text, int filteringBox)
    {
        await Open(width);
        if (filteringBox > 0)
        {
            await HoldFilteringBox(filteringBox);
            await OpenFilterRowsAsync();
        }
        var el = Page.Locator(selector, new() { HasTextString = text }).First;
        // since 5.0.0 a section beside the features is a tab: open the one that holds it (plans/OPTION_C_PLAN.md S3)
        var tab = await el.EvaluateAsync<string?>("e => { const p = e.closest('.report-panel'); return p && p.hidden ? p.dataset.panel : null; }");
        if (tab is not null) await OpenReportTabAsync(tab);
        await el.ScrollIntoViewIfNeededAsync();

        // The right edge of its text, the content edge of the box holding it, and the lines it takes.
        var geometry = await el.EvaluateAsync<double[]>("""
            el => {
                const box = el.closest('.failure-clusters, .history-section, .filtering-box');
                const edge = box.getBoundingClientRect().left + box.clientLeft + box.clientWidth;
                const range = document.createRange();
                range.selectNodeContents(el);
                const rects = [...range.getClientRects()].filter(r => r.width > 0);
                return [Math.max(...rects.map(r => r.right)), edge, new Set(rects.map(r => Math.round(r.top))).size];
            }
            """);
        Assert.True(geometry[0] <= geometry[1] + 1, $"the text ends {geometry[0] - geometry[1]:F0} px past its box at {width} px");
        Assert.True(geometry[2] > 1, $"the token should break onto a second line at {width} px, not run on");
        Assert.Equal(0, await PageOverflow());
    }

    /// <summary>The table ran past the filtering box and scrolled the page by up to 82 px, from 1170 to
    /// 1260 px.</summary>
    [Fact]
    public async Task The_search_help_table_scrolls_inside_its_panel_when_the_filtering_box_is_narrower()
    {
        await Open(1180, textSpacing: true);
        await HoldFilteringBox(220);
        await Page.Locator(".search-help-toggle").First.ClickAsync();
        var panel = Page.Locator(".search-help-panel").First;
        await Expect(panel).ToBeVisibleAsync();

        await Expect(panel).ToHaveCSSAsync("overflow-x", "auto");
        var widths = await panel.EvaluateAsync<double[]>("p => [p.scrollWidth, p.clientWidth]");
        Assert.True(widths[0] > widths[1], $"the table should be wider than its panel here (scroll width {widths[0]}, width {widths[1]})");
        Assert.Equal(0, await PageOverflow());
    }

    /// <summary>With its label held on one line the export buttons ran up to 13 px into the box's padding
    /// here, and with a classic scrollbar 11 px past the box and the page 3 px sideways.</summary>
    [Fact]
    public async Task An_export_button_wider_than_the_filtering_box_wraps_its_label_inside_it()
    {
        await Open(1161, textSpacing: true);
        // Since 5.0.0 a filtering box under 430 px drops the word Filtered (OPTION_C_PLAN S6), and Export HTML fits a
        // 140 px box whole, so the box is held narrower than that.
        await HoldFilteringBox(100);
        var button = Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" });
        Assert.True(await button.Locator(".wide-only").EvaluateAsync<bool>("s => getComputedStyle(s).display === 'none'"));

        // The right edge of the export labels, the filtering box's content edge, and the button's lines. Since 5.0.0 an
        // export button is a text button whose padding runs past the content edge on purpose (OPTION_C_PLAN S2), so
        // its label, not its box, is what must stay inside.
        var geometry = await button.EvaluateAsync<double[]>("""
            b => {
                const box = b.closest('.filtering-box'), bs = getComputedStyle(box);
                const edge = box.getBoundingClientRect().right - parseFloat(bs.borderRightWidth) - parseFloat(bs.paddingRight);
                const rects = el => { const range = document.createRange(); range.selectNodeContents(el); return [...range.getClientRects()].filter(r => r.width > 0); };
                const lines = new Set(rects(b).map(r => Math.round(r.top))).size;
                const right = Math.max(...[...b.closest('.filtering-box-export').querySelectorAll('.export-btn')].flatMap(e => rects(e).map(r => r.right)));
                return [right, edge, lines];
            }
            """);
        Assert.True(geometry[0] <= geometry[1] + 1, $"the export labels end {geometry[0] - geometry[1]:F0} px past the filtering box's content");
        Assert.Equal(2, (int)geometry[2]);
        Assert.Equal(0, await PageOverflow());
    }
}
