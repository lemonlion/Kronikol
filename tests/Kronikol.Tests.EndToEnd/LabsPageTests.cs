using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The labs page beside a report (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md section 3.4), in a browser: it opens with
/// its History section open, its table of every scenario paints each sparkline's runs, its diagnostics list is open,
/// and a scenario named on it opens that scenario in the report.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class LabsPageTests : PlaywrightTestBase
{
    public LabsPageTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly DiagnosticEntry[] Diagnostics =
        [new(DiagnosticKind.CaptureDegraded, "tap-di-redis: decoding disabled on 1 connection(s)")];

    private async Task<(string Page, string Report)> Open(string reportFileName)
    {
        var urls = HistoryReportHelper.GenerateWithLabsPage(TempDir, OutputDir, reportFileName, Diagnostics);
        await Page.GotoAsync(urls.Page);
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
        await link.ClickAsync();

        await Page.WaitForURLAsync(report + "#sid-" + HistoryReportHelper.PayId);
        await Page.WaitForFunctionAsync(
            $"() => {{ const el = document.querySelector('[data-stable-id=\"{HistoryReportHelper.PayId}\"]'); return el && el.hasAttribute('open'); }}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        Assert.True(await Page.Locator($"[data-stable-id='{HistoryReportHelper.PayId}']").First.EvaluateAsync<bool>(
            "el => el.closest('details.feature').hasAttribute('open')"));
    }

    [Fact]
    public async Task The_page_runs_no_script()
    {
        await Open("LabsPage_NoScript.html");

        Assert.Equal(0, await Page.Locator("script").CountAsync());
    }
}
