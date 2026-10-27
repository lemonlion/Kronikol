using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// QB22 (plans/SEARCH_PLAN.md 4.3): the query builder in the two engines CI does not install, as local legs that skip where
/// the engine is absent. A search typed into the builder and run with Enter lands on the Search tab with its results, so
/// the dialog, the combobox, the coloured mirror and the tab's script all work there.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class SearchBuilderEngineTests : PlaywrightTestBase
{
    public SearchBuilderEngineTests(PlaywrightFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public async Task The_builder_runs_a_search_in_Firefox_and_WebKit(string engine)
    {
        using var playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        try
        {
            var type = engine == "firefox" ? playwright.Firefox : playwright.Webkit;
            browser = await type.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException) { browser = null; }
        Assert.SkipWhen(browser is null, $"{engine} is not installed (CI installs chromium only)");
        await using var _ = browser!;
        var page = await OpenPageAsync(browser!, new BrowserNewPageOptions { ViewportSize = new() { Width = 1280, Height = 800 } });
        await page.GotoReportAsync(SearchReports.Write(TempDir, OutputDir, $"BuilderEngine_{engine}.html"));
        await page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });

        await page.Locator("#search-button").ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
        await page.Locator("#search-input").FillAsync("toast");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('#search-suggestions [role=option]').length > 0", null, new() { PollingInterval = 200 });
        await page.Locator("#search-input").PressAsync("Enter");

        await page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden && document.getElementById('search-count').textContent === '12 results'",
            null, new() { PollingInterval = 200 });
        Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('search-dialog').open"));
        // the search's own query, the filter field left as it was (5.12.0: 5.11.0 put the search in the field)
        Assert.Equal("toast", await page.EvaluateAsync<string>("() => search_query()"));
        Assert.Equal("", await page.EvaluateAsync<string>("() => document.getElementById('searchbar').value"));
    }
}
