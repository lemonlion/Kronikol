using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The labs page beside a report (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md section 3.4), in a browser: it opens with
/// its History section open, its table of every scenario paints each sparkline's runs, its diagnostics list is open,
/// and a scenario named on it opens that scenario in the report.
/// </summary>
// The fixture runs the report pipeline (HistoryReportHelper.GenerateWithLabsPage), whose diagram fetcher and log store are
// process-wide: in the collection of the other classes that run it, so no two pipelines overlap.
[Collection(PlaywrightCollections.Diagrams)]
public class LabsPageTests : PlaywrightTestBase
{
    public LabsPageTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly DiagnosticEntry[] Diagnostics =
        [new(DiagnosticKind.CaptureDegraded, "tap-di-redis: decoding disabled on 1 connection(s)")];

    private async Task<(string Page, string Report)> Open(string reportFileName)
    {
        var urls = HistoryReportHelper.GenerateWithLabsPage(TempDir, OutputDir, reportFileName, Diagnostics);
        await Page.GotoReportAsync(urls.Page);
        await Page.Locator("header.labs-header").WaitForAsync();
        return urls;
    }

    [Fact]
    public async Task The_page_opens_with_its_history_and_its_diagnostics_open()
    {
        await Open("LabsPage_Open.html");

        Assert.True(await Page.Locator("#history-section").EvaluateAsync<bool>("el => el.open"));
        await Expect(Page.Locator("#history-section .history-list").First).ToBeVisibleAsync();
        Assert.True(await Page.Locator("details.report-diagnostics").EvaluateAsync<bool>("el => el.open"));
        await Expect(Page.Locator("details.report-diagnostics")).ToContainTextAsync("tap-di-redis: decoding disabled");
        // The table of every scenario starts collapsed, one row per scenario.
        Assert.False(await Page.Locator("details.labs-scenarios").EvaluateAsync<bool>("el => el.open"));
        await Expect(Page.Locator("tr.labs-row")).ToHaveCountAsync(4);
    }

    [Fact]
    public async Task Each_sparkline_paints_a_stop_per_run()
    {
        await Open("LabsPage_Sparkline.html");
        await Page.Locator("details.labs-scenarios > summary").ClickAsync();

        var sparkline = Page.Locator("tr.labs-row .history-sparkline").First;
        await Expect(sparkline).ToBeVisibleAsync();
        var painted = await sparkline.EvaluateAsync<string>("el => getComputedStyle(el).backgroundImage");
        Assert.StartsWith("linear-gradient(", painted);
        // Seven runs (six seeded and this one), each a hard stop pair of one colour.
        Assert.True(painted.Split("rgb(").Length - 1 >= 7, painted);
        var box = await sparkline.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Width > 0 && box.Height > 0, $"the sparkline paints nothing: {box.Width}x{box.Height}");
        // The broken scenario's verdict is drawn beside it.
        await Expect(Page.Locator("tr.labs-row .history-verdict-broke")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task A_scenario_named_on_the_page_opens_in_the_report()
    {
        var (_, report) = await Open("LabsPage_Link.html");

        var link = Page.Locator($"#history-section a.history-link[href='LabsPage_Link.html#sid-{HistoryReportHelper.PayId}']").First;
        await Expect(link).ToBeVisibleAsync();
        // The wait starts before the click, and asks for the commit only: started after it, it found the report's URL
        // already there when the report opened quickly, and then waited for a load event that never came (Linux CI on
        // 5.16.0, as BackForwardRestoreTests' wait once did). WaitForURLReportAsync then waits for the report itself.
        var opened = Page.WaitForURLReportAsync(report + "#sid-" + HistoryReportHelper.PayId, new() { WaitUntil = WaitUntilState.Commit });
        await link.ClickAsync();
        await opened;
        await Page.WaitForFunctionAsync(
            $"() => {{ const el = document.querySelector('[data-stable-id=\"{HistoryReportHelper.PayId}\"]'); return el && el.hasAttribute('open'); }}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        Assert.True(await Page.Locator($"[data-stable-id='{HistoryReportHelper.PayId}']").First.EvaluateAsync<bool>(
            "el => el.closest('details.feature').hasAttribute('open')"));
    }

    /// <summary>Of the report's scripts the page runs only the theme's: the one that chooses light or dark before the first
    /// style and the one that works the switch on its title line (5.0.0, plans/OPTION_C_PLAN.md S13).</summary>
    [Fact]
    public async Task The_page_runs_only_the_theme_scripts()
    {
        await Open("LabsPage_ThemeScripts.html");

        Assert.Equal(["kronikol-theme-boot", "kronikol-theme-switch"],
            await Page.Locator("script").EvaluateAllAsync<string[]>("scripts => scripts.map(s => s.id)"));
    }
}
