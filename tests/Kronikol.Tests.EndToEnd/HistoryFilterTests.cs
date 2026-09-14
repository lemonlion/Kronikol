using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The verdicts in the search box: <c>$flaky</c>, <c>$broke</c>, <c>$stable</c>... filter on the
/// scenario's cross-run verdicts and combine with the statuses, because a scenario is routinely both
/// <c>$failed</c> and <c>$flaky</c>.
/// </summary>
[Collection(PlaywrightCollections.Search)]
public class HistoryFilterTests : PlaywrightTestBase
{
    public HistoryFilterTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task Open(string fileName)
    {
        await Page.GotoAsync(HistoryReportHelper.Generate(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    /// <summary>
    /// Waits for exactly these scenarios to be the visible ones. A wait on the count alone returns
    /// before a query has applied whenever the previous query left the same number visible.
    /// </summary>
    private async Task SearchAndWaitFor(string query, params string[] expectedStableIds)
    {
        await FillSearchBar(query);
        var expected = string.Join(",", expectedStableIds.OrderBy(id => id, StringComparer.Ordinal));
        await Page.WaitForFunctionAsync(
            $$"""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none')
                .map(s => s.getAttribute('data-stable-id')).sort().join(',') === '{{expected}}'
            """,
            null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    private static readonly string FindId = Kronikol.Reports.ScenarioStableId.Compute(HistoryReportHelper.Suite, "Search", "Find a product");

    [Fact]
    public async Task Dollar_flaky_shows_only_the_flaky_scenario()
    {
        await Open("HistoryFilter_Flaky.html");

        await SearchAndWaitFor("$flaky", HistoryReportHelper.RetryId);
    }

    [Fact]
    public async Task Verdicts_combine_with_statuses_and_each_other()
    {
        await Open("HistoryFilter_Combine.html");

        await SearchAndWaitFor("$failed && $broke", HistoryReportHelper.PayId);
        await SearchAndWaitFor("$passed && $flaky", HistoryReportHelper.RetryId);
        await SearchAndWaitFor("$flaky || $broke", HistoryReportHelper.PayId, HistoryReportHelper.RetryId);
        await SearchAndWaitFor("$stable", HistoryReportHelper.RefundId, FindId);
        await SearchAndWaitFor("$failed && !!$flaky", HistoryReportHelper.PayId);
        await SearchAndWaitFor("", HistoryReportHelper.PayId, HistoryReportHelper.RetryId, HistoryReportHelper.RefundId, FindId);
    }

    [Fact]
    public async Task A_status_name_still_means_the_status()
    {
        await Open("HistoryFilter_Status.html");

        await SearchAndWaitFor("$failed", HistoryReportHelper.PayId);
        await SearchAndWaitFor("$passed", HistoryReportHelper.RetryId, HistoryReportHelper.RefundId, FindId);
    }
}
