using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The search button on the title line (plans/SEARCH_PLAN.md 4.2; facts T2, T3, T4, T6, T7, T9): it has the theme switch's
/// height, edge, corners, ground and shadow; it is 192 to 384 px wide above the phone breakpoint and the switch's square
/// on a phone; it shares the switch's top and bottom at every width; it shows the applied query with its values coloured,
/// and its name holds it; its clear empties the query and nothing else, as one entry.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class SearchButtonTests : PlaywrightTestBase
{
    public SearchButtonTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenAsync(string fileName, int width = 1280, int height = 800, string hash = "")
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoReportAsync(SearchReports.Write(TempDir, OutputDir, fileName) + hash);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await TwoAnimationFramesAsync(Page);
    }

    private Task<string> Style(string selector, string property) =>
        Page.EvaluateAsync<string>("([s, p]) => getComputedStyle(document.querySelector(s)).getPropertyValue(p)", new[] { selector, property });

    // T2
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_search_box_has_the_switch_s_height_edge_corners_ground_and_shadow(string theme)
    {
        await OpenAsync($"SearchButton_Look_{theme}.html");
        if (theme == "dark") await Page.Locator(".theme-toggle").ClickAsync();
        // the pointer off the switch, whose hover ground a click leaves under it
        await Page.Mouse.MoveAsync(1, 600);
        await TwoAnimationFramesAsync(Page);

        foreach (var property in new[] { "height", "border-top-width", "border-top-color", "border-top-left-radius", "background-color", "box-shadow" })
            Assert.Equal(await Style(".theme-toggle", property), await Style(".search-box", property));
    }

    // T3
    [Theory]
    [InlineData(1440, 384, 384)]
    [InlineData(1280, 192, 384)]
    [InlineData(1012, 192, 384)]
    [InlineData(769, 192, 384)]
    [InlineData(768, 34, 34)]
    [InlineData(390, 34, 34)]
    public async Task The_search_box_is_192_to_384_px_wide_from_768_02_px_and_the_switch_s_square_below(int width, double min, double max)
    {
        await OpenAsync($"SearchButton_Width_{width}.html", width);

        var box = await Page.Locator(".search-box").BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.InRange(box!.Width, min - 0.5, max + 0.5);
        if (width <= 768) Assert.InRange(box.Height, 33.5, 34.5);
    }

    // T4
    [Fact]
    public async Task The_search_box_and_the_switch_share_their_top_and_bottom_at_every_width()
    {
        await OpenAsync("SearchButton_Align.html");
        for (var width = 320; width <= 1400; width += 40)
        {
            await Page.SetViewportSizeAsync(width, 800);
            await TwoAnimationFramesAsync(Page);
            var search = (await Page.Locator(".search-box").BoundingBoxAsync())!;
            var toggle = (await Page.Locator(".theme-toggle").BoundingBoxAsync())!;
            Assert.True(Math.Abs(search.Y - toggle.Y) < 0.6 && Math.Abs(search.Y + search.Height - toggle.Y - toggle.Height) < 0.6,
                $"at {width} px the search box is {search.Y}..{search.Y + search.Height}, the switch {toggle.Y}..{toggle.Y + toggle.Height}");
            Assert.True(search.X + search.Width <= toggle.X, $"at {width} px the search box runs into the switch");
        }
    }

    // T6, T9
    [Fact]
    public async Task An_applied_query_shows_in_the_search_box_with_its_values_coloured()
    {
        await OpenAsync("SearchButton_Query.html", hash: "#search=pancake%20status%3Afailed");

        var query = Page.Locator("#search-button .search-button-query");
        await Expect(query).ToBeVisibleAsync();
        await Expect(query).ToHaveTextAsync("pancake status:failed");
        await Expect(Page.Locator("#search-button .search-button-hint")).ToBeHiddenAsync();
        await Expect(Page.Locator("#search-clear")).ToBeVisibleAsync();
        // a value in the selected colours, as the builder colours it (4.3.2)
        var value = query.Locator(".q-value").First;
        await Expect(value).ToHaveTextAsync("failed");
        Assert.NotEqual(await Style("#search-button .search-button-query", "color"),
            await value.EvaluateAsync<string>("e => getComputedStyle(e).color"));
        // T9: the visible label is in the name (WCAG 2.5.3)
        await Expect(Page.Locator("#search-button")).ToHaveAttributeAsync("aria-label", "Search: pancake status:failed. Change the search, /");
    }

    // T7
    [Fact]
    public async Task Clear_empties_the_query_and_nothing_else_in_one_entry()
    {
        await OpenAsync("SearchButton_Clear.html", hash: "#status=Passed&search=toast");
        var length = await Page.EvaluateAsync<int>("() => history.length");

        await Page.Locator("#search-clear").ClickAsync();

        Assert.Equal("", await Page.EvaluateAsync<string>("() => search_query()"));
        await Page.WaitForFunctionAsync("() => !location.hash.includes('search=') && location.hash.includes('status=Passed')", null, new() { PollingInterval = 200 });
        Assert.Equal(length + 1, await Page.EvaluateAsync<int>("() => history.length"));
        await Expect(Page.Locator("#search-clear")).ToBeHiddenAsync();
        await Expect(Page.Locator("#search-button")).ToBeFocusedAsync();
        await Page.GoBackAsync();
        await Page.WaitForFunctionAsync("() => search_query() === 'toast'", null, new() { PollingInterval = 200 });
    }
}
