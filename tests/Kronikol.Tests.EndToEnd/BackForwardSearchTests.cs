using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The way to a match and the browser's history (plans/SEARCH_PLAN.md 4.6.5, H19 and H20): following a result's line writes
/// the match into the address as <c>hit=</c>, so Back returns to the results, and Forward or a reload goes to the match
/// again; a <c>hit=</c> that names a place this report no longer has opens the scenario and says so.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class BackForwardSearchTests : PlaywrightTestBase
{
    public BackForwardSearchTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenAsync(string fileName, string hash = "")
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoReportAsync(SearchHitWayTests.WriteKitchen(TempDir, OutputDir, fileName) + hash);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results && !!window.kronikol_hit", null, new() { PollingInterval = 200 });
    }

    private Task<string> HashAsync() => Page.EvaluateAsync<string>("() => decodeURIComponent(location.hash)");

    private Task WaitForTabAsync(string tab) => Page.WaitForFunctionAsync(
        "k => document.querySelector('.report-tabs > .report-tab[aria-selected=\"true\"]').dataset.tab === k", tab, new() { PollingInterval = 200 });

    // the step the way put the focus on, once it has
    private async Task<string> FocusedStepAsync()
    {
        await Page.WaitForFunctionAsync("() => !!document.activeElement && !!document.activeElement.closest('details.scenario-steps')", null,
            new() { PollingInterval = 200 });
        return await Page.EvaluateAsync<string>("() => document.activeElement.textContent.replace(/\\s+/g, ' ').trim()");
    }

    private ILocator StepLine => Page.Locator("#panel-search .search-result .search-line:has(.search-line-kind:text-is('STEP'))").First;

    // H19
    [Fact]
    public async Task Back_returns_to_the_result_and_Forward_or_a_reload_returns_to_the_marked_match()
    {
        await OpenAsync("BackSearch_H19.html");
        await FillSearchBar("marmalade", toSearchTab: true);
        await WaitForTabAsync("search");
        await StepLine.ClickAsync();
        await WaitForTabAsync("features");
        Assert.Contains("hit=s:3", await HashAsync());
        Assert.Contains("the plate holds marmalade", await FocusedStepAsync());

        await Page.GoBackAsync();
        await WaitForTabAsync("search");
        await Expect(StepLine).ToBeVisibleAsync();
        Assert.DoesNotContain("hit=", await HashAsync());

        await Page.GoForwardAsync();
        await WaitForTabAsync("features");
        Assert.Contains("hit=s:3", await HashAsync());
        Assert.Contains("the plate holds marmalade", await FocusedStepAsync());

        // a reload reads the address as it is, and goes to the match again
        await Page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await Page.ReloadReportAsync();
        await Page.WaitForFunctionAsync("() => !!window.kronikol_hit", null, new() { PollingInterval = 200 });
        Assert.Contains("hit=s:3", await HashAsync());
        Assert.Contains("the plate holds marmalade", await FocusedStepAsync());
    }

    // H20
    [Fact]
    public async Task A_hit_link_that_no_longer_fits_opens_the_scenario_and_says_so()
    {
        // the address a line wrote, its step moved on since: the report's scenario has three steps, not nine
        await OpenAsync("BackSearch_H20.html");
        await FillSearchBar("marmalade", toSearchTab: true);
        await WaitForTabAsync("search");
        await StepLine.ClickAsync();
        await WaitForTabAsync("features");
        var stale = (await Page.EvaluateAsync<string>("() => location.hash")).Replace("hit=s%3A3", "hit=s%3A9").Replace("hit=s:3", "hit=s:9");
        Assert.Contains("hit=s", stale);
        Assert.DoesNotContain("s:3", Uri.UnescapeDataString(stale));

        await OpenAsync("BackSearch_H20b.html", stale);
        await Page.WaitForFunctionAsync(
            "() => { const s = document.querySelector('.kron-nav-status'); return !!s && s.textContent.indexOf('not found in this report') >= 0; }",
            null, new() { PollingInterval = 200 });
        Assert.Equal("This match was not found in this report; its scenario is shown.",
            await Page.Locator(".kron-nav-status").TextContentAsync());
        // the scenario is open and in the window, and the stale hit= is not read as a filter
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => { const s = Array.from(document.querySelectorAll('details.scenario')).find(d => d.textContent.indexOf('Breakfast is served') >= 0); const r = s.getBoundingClientRect(); return s.open && r.bottom > 0 && r.top < window.innerHeight; }"));
        Assert.DoesNotContain("hit", await Page.Locator("#searchbar").InputValueAsync());
    }
}
