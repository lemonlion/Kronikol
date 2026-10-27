using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The search on a phone (plans/SEARCH_PLAN.md 4.2, 4.5.9): the Search tab in one column, a type switcher in place of the
/// sidebar and the bar's own Filters for the facets (no second sheet), and the tab row brought to the current tab. B15, the
/// search in reach down the page, is in MobileResponsiveTests.
/// </summary>
[Collection(PlaywrightCollections.Mobile)]
public class SearchResultsPhoneTests : PlaywrightTestBase
{
    public SearchResultsPhoneTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 390;
    protected override int ViewportHeight => 800;

    private async Task OpenAsync(string fileName, Feature[]? features = null, int width = 390)
    {
        await Page.SetViewportSizeAsync(width, 800);
        await Page.GotoReportAsync(SearchReports.Write(TempDir, OutputDir, fileName, features));
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results", null, new() { PollingInterval = 200 });
    }

    private async Task SearchAsync(string query)
    {
        await FillSearchBar(query, toSearchTab: true);
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden && document.getElementById('search-count').textContent !== ''",
            null, new() { PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    // whether a tab lies inside its row's box, the row scrolling sideways with its scrollbar hidden
    // whether the tab comes into its row's view: the row scrolls to a tab made current in the next frame, not in the
    // click or the Back that made it current (report-tabs-script.js intoRow), so the tab is waited for, a few frames at most
    private async Task<bool> TabInRowAsync(string key)
    {
        try
        {
            await Page.WaitForFunctionAsync(@"k => {
                const t = document.querySelector('.report-tabs > .report-tab[data-tab=""' + k + '""]'), row = t.parentElement;
                const a = row.getBoundingClientRect(), b = t.getBoundingClientRect();
                return b.left >= a.left - 1 && b.right <= a.right + 1;
            }", key, new() { Timeout = 3000, PollingInterval = 200 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // R24
    [Fact]
    public async Task On_a_phone_the_type_switcher_replaces_the_sidebar_and_the_filters_are_out_of_sight()
    {
        await OpenAsync("Phone_R24.html");
        await SearchAsync("toast");

        Assert.True(await Page.Locator("#panel-search .search-sidebar").IsHiddenAsync());
        Assert.True(await Page.Locator("#panel-search .search-sort").IsHiddenAsync());
        var switcher = Page.Locator("#panel-search .search-phone-row .search-type-switch");
        Assert.Equal("Scenarios 12", await switcher.TextContentAsync());
        Assert.Equal(1, await Page.Locator("#panel-search .search-phone-row .search-kebab").CountAsync());
        // the builder narrows the search on a phone; the filters are out of sight while the Search tab shows (5.12.0, the
        // owner: a search is no filter), and the tab has no sheet of its own
        Assert.True(await Page.Locator(".nav-filters-toggle").IsHiddenAsync());
        Assert.Equal(0, await Page.Locator("#panel-search [role=dialog], #panel-search dialog").CountAsync());

        await switcher.ClickAsync();
        // the switcher's own menu; the kebab beside it has its tips and exports
        var items = await Page.Locator("#panel-search .search-phone-row .search-type-switch + .search-menu .search-menu-item").AllTextContentsAsync();
        Assert.Equal(["Scenarios 12", "Features 1", "Names 12", "Steps 0", "Diagrams 0", "Errors 0"], items);
        await Page.Locator("#panel-search .search-phone-row .search-menu-item:text-is('Features 1')").ClickAsync();
        await TwoAnimationFramesAsync(Page);

        Assert.Equal("1 feature", await Page.Locator("#search-count").TextContentAsync());
        Assert.Equal("Features 1", await Page.Locator("#panel-search .search-phone-row .search-type-switch").TextContentAsync());
    }

    // R29
    [Fact]
    public async Task On_a_phone_the_tab_row_scrolls_to_the_current_tab()
    {
        // the report with every tab, History among them, as the plan's R29 has it
        await Page.SetViewportSizeAsync(320, 800);
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, "Phone_R29.html"));
        await Page.Locator(".report-tabs").WaitForAsync();
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results", null, new() { PollingInterval = 200 });
        var overflows = await Page.EvaluateAsync<bool>("() => { const r = document.querySelector('.report-tabs'); return r.scrollWidth > r.clientWidth + 1; }");

        await SearchAsync("payments");
        Assert.True(await TabInRowAsync("search"), "the Search tab, made current by the builder, is out of its row's view");

        // a tab far along the row, then the Features tab, then Back: the tab Back makes current is brought into view
        const string last = "history";
        await Page.Locator($".report-tabs > .report-tab[data-tab='{last}']").ClickAsync();
        await Page.Locator("#tab-features").ClickAsync();
        Assert.True(await TabInRowAsync("features"));
        await Page.GoBackAsync();
        await Page.WaitForFunctionAsync("k => document.querySelector('.report-tabs > .report-tab[aria-selected=\"true\"]').dataset.tab === k", last, new() { PollingInterval = 200 });

        Assert.True(await TabInRowAsync(last), $"the {last} tab, made current by Back, is out of its row's view");
        Assert.True(overflows, "the tab row fits the window, so this fact proves nothing at this width");
    }
}
