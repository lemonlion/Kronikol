using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A default report from a run that read a ledger (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md): nothing about history
/// is painted, a verdict query typed in the search box matches nothing without an error, and Export Filtered HTML
/// carries no history. The history is on the labs page beside it (<see cref="LabsPageTests"/>).
/// </summary>
[Collection(PlaywrightCollections.Search)]
public class DefaultReportWithoutHistoryTests : PlaywrightTestBase
{
    public DefaultReportWithoutHistoryTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task Open(string reportFileName)
    {
        var (_, report) = HistoryReportHelper.GenerateWithLabsPage(TempDir, OutputDir, reportFileName);
        await Page.GotoAsync(report);
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.setAttribute('open', ''))");
    }

    [Fact]
    public async Task No_sparkline_or_verdict_is_painted_in_any_scenario_header()
    {
        await Open("DefaultNoHistory_Headers.html");

        await Expect(Page.Locator("details.scenario")).ToHaveCountAsync(4);
        await Expect(Page.Locator(".history-sparkline")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".history-verdict")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-history-verdicts]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#history-section")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_verdict_query_matches_nothing_and_raises_no_error()
    {
        var errors = new List<string>();
        Page.Console += (_, message) => { if (message.Type == "error") lock (errors) errors.Add(message.Text); };
        Page.PageError += (_, error) => { lock (errors) errors.Add(error); };
        await Open("DefaultNoHistory_Search.html");

        await FillSearchBar("$flaky");
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.scenario')).every(s => getComputedStyle(s).display === 'none')",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        // And the statuses still filter as they always have.
        await FillSearchBar("$failed");
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none').length === 1",
            null, new() { Timeout = 5000, PollingInterval = 200 });
        lock (errors)
            Assert.Empty(errors);
    }

    [Fact]
    public async Task The_filtered_export_carries_no_history()
    {
        await Open("DefaultNoHistory_Export.html");

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
        {
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" }).ClickAsync();
        });
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);
        var exported = await File.ReadAllTextAsync(path);

        Assert.Contains("data-stable-id=\"", exported);
        Assert.DoesNotContain("history-", exported);
        Assert.DoesNotContain("data-history-verdicts", exported);
    }
}
