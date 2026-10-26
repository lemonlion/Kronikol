using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A default report from a run that read a ledger (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md, as 5.0.0 moves it:
/// plans/OPTION_C_PLAN.md S3, R18.1): its history is the History tab and nothing of it is painted in the features,
/// a verdict query typed in the search box matches nothing without an error, and Export Filtered HTML carries no
/// history. The history is on the labs page beside it too (<see cref="LabsPageTests"/>).
/// </summary>
// The fixture runs the report pipeline (HistoryReportHelper.GenerateWithLabsPage), whose diagram fetcher and log store are
// process-wide: in the collection of the other classes that run it, so no two pipelines overlap.
[Collection(PlaywrightCollections.Diagrams)]
public class DefaultReportWithoutHistoryTests : PlaywrightTestBase
{
    public DefaultReportWithoutHistoryTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task Open(string reportFileName)
    {
        var (_, report) = HistoryReportHelper.GenerateWithLabsPage(TempDir, OutputDir, reportFileName);
        await Page.GotoReportAsync(report);
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.setAttribute('open', ''))");
    }

    [Fact]
    public async Task No_sparkline_or_verdict_is_painted_in_any_scenario_header()
    {
        await Open("DefaultNoHistory_Headers.html");

        await Expect(Page.Locator("details.scenario")).ToHaveCountAsync(4);
        // the features: the History tab's own lists carry verdict pills, the scenarios none
        await Expect(Page.Locator("#report-content .history-sparkline")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#report-content .history-verdict")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-history-verdicts]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#panel-history > #history-section")).ToHaveCountAsync(1);
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
            await PressBoxExportAsync(Page, "Export Filtered HTML");
        });
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);
        var exported = ReportFileText.Read(path);

        Assert.Contains("data-stable-id=\"", exported);
        // the features alone: no History section, no sparkline, no verdict. The head it copies is the report's, whose
        // stylesheet names the History tab's classes since 5.0.0, so the markup is what is checked.
        Assert.DoesNotContain("<details id=\"history-section\"", exported);
        Assert.DoesNotContain("<span class=\"history-sparkline\"", exported);
        Assert.DoesNotContain("<span class=\"history-verdict", exported);
        Assert.DoesNotContain("data-history-verdicts", exported);
    }
}
