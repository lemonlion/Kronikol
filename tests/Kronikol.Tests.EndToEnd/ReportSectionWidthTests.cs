using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// How what a run report holds outside its features fits the window (plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.md,
/// the audit). No feature clips these sections, so a long token that does not break scrolls the whole page
/// sideways: a failure cluster's message, a test method's full name in the cluster's list and in the
/// History section, the History section's branch, a dependency chip named after a typed client. Each
/// breaks inside its box. Under WCAG text spacing, just above the breakpoint, the filtering box of a report
/// with a long branch is narrower than the search help's table, which scrolls inside its panel, and than
/// one export button, which wraps its label inside the box. <see cref="ViewportSweepTests"/> proves the
/// page never scrolls sideways at any width; these facts pin which way each kind of content fits.
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
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, $"EverySection_{width}{(textSpacing ? "_spaced" : "")}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        if (textSpacing)
            await Page.AddStyleTagAsync(new() { Content = TextSpacing });
    }

    private Task<int> PageOverflow() =>
        Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

    [Theory]
    [InlineData(320, ".failure-cluster > summary", "System.InvalidOperationException")]
    [InlineData(320, "a.failure-cluster-scenario-link", "OrderReconciliationTests")]
    [InlineData(320, "a.history-link", "OrderReconciliationTests")]
    [InlineData(320, ".history-meta code", "dependabot/nuget")]
    [InlineData(1200, "button.dependency-toggle", "NightlySettlementLedgerHttpClient")]
    public async Task A_long_token_outside_the_features_breaks_inside_its_box(int width, string selector, string text)
    {
        await Open(width);
        var el = Page.Locator(selector, new() { HasTextString = text }).First;
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
        var button = Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" });

        // The export buttons' right edge, the filtering box's content edge, and the button's lines.
        var geometry = await button.EvaluateAsync<double[]>("""
            b => {
                const box = b.closest('.filtering-box'), bs = getComputedStyle(box);
                const edge = box.getBoundingClientRect().right - parseFloat(bs.borderRightWidth) - parseFloat(bs.paddingRight);
                const range = document.createRange();
                range.selectNodeContents(b);
                const lines = new Set([...range.getClientRects()].filter(r => r.width > 0).map(r => Math.round(r.top))).size;
                return [b.closest('.filtering-box-export').getBoundingClientRect().right, edge, lines];
            }
            """);
        Assert.True(geometry[0] <= geometry[1] + 1, $"the export buttons end {geometry[0] - geometry[1]:F0} px past the filtering box's content");
        Assert.Equal(2, (int)geometry[2]);
        Assert.Equal(0, await PageOverflow());
    }
}
