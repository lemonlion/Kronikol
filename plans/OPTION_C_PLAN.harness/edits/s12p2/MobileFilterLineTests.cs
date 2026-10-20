using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The phone's Filters line (5.0.0, plans/OPTION_C_PLAN.md S12): on a phone the filter rows fold under a Filters button on
/// a line of its own, which the readout rides; the button folds them with More filters' class, so the rows stay as they
/// were when the window crosses the phone breakpoint either way; the keyboard reaches it; the search help opens while the
/// rows are folded; and in the dark theme the sticky search line has the filter box's own ground.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class MobileFilterLineTests : PlaywrightTestBase
{
    public MobileFilterLineTests(PlaywrightFixture fixture) : base(fixture) { }

    // twelve scenarios in two features, timed, every third tagged smoke and one a happy path, so the box has every row
    private string Generate(string fileName)
    {
        var features = Enumerable.Range(1, 2).Select(f => new Feature
        {
            DisplayName = $"Feature {f}",
            Scenarios = Enumerable.Range(1, 6).Select(i => new Scenario
            {
                Id = $"f{f}s{i}", DisplayName = $"Scenario {f}.{i}", Result = ExecutionResult.Passed,
                Duration = TimeSpan.FromMilliseconds(10 * i + f), Labels = i % 3 == 0 ? ["smoke"] : null, IsHappyPath = f == 1 && i == 1
            }).ToArray()
        }).ToArray();
        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenAsync(string fileName, int width)
    {
        await Page.SetViewportSizeAsync(width, 800);
        await Page.GotoAsync(Generate(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await TwoAnimationFramesAsync(Page);
    }

    private ILocator Filters => Page.Locator(".mobile-filter-toggle");
    private ILocator More => Page.Locator(".filtering-box .more-filters");
    private ILocator Rows => Page.Locator("#filter-rows .filter-row").First;
    private ILocator Readout => Page.Locator(".filtering-box .filter-readout");

    // where the readout is: on the Filters line, right before Clear All, or anywhere else
    private Task<string> ReadoutPlaceAsync() => Readout.EvaluateAsync<string>("""
        r => r.parentElement.classList.contains('mobile-filter-line') ? 'line'
            : r.nextElementSibling && /clear_all_filters/.test(r.nextElementSibling.getAttribute('onclick') || '') ? 'before Clear All'
            : 'elsewhere'
        """);

    [Fact]
    public async Task On_a_phone_the_readout_rides_the_Filters_line_and_keeps_counting()
    {
        await OpenAsync("MobileFilterLine_Readout.html", 400);

        Assert.Equal("line", await ReadoutPlaceAsync());
        var middles = await Page.EvaluateAsync<double[]>("""
            () => ['.mobile-filter-toggle', '.filtering-box .filter-readout'].map(s => {
                const r = document.querySelector(s).getBoundingClientRect();
                return r.top + r.height / 2;
            })
            """);
        Assert.True(Math.Abs(middles[0] - middles[1]) <= 2, $"the readout's middle is at {middles[1]}, the button's at {middles[0]}");

        await FillSearchBar("Scenario 1.2");
        await Expect(Readout).ToContainTextAsync("Filtered");
        Assert.Equal("line", await ReadoutPlaceAsync());
    }

    [Fact]
    public async Task On_a_wider_window_the_readout_is_before_Clear_All_and_it_follows_the_width()
    {
        await OpenAsync("MobileFilterLine_Width.html", 1280);
        Assert.Equal("before Clear All", await ReadoutPlaceAsync());
        await Expect(Page.Locator(".mobile-filter-line")).ToBeHiddenAsync();

        await Page.SetViewportSizeAsync(400, 800);
        await Page.WaitForFunctionAsync("() => document.querySelector('.filtering-box .filter-readout').parentElement.classList.contains('mobile-filter-line')",
            null, new() { PollingInterval = 200 });

        await Page.SetViewportSizeAsync(1280, 800);
        await Page.WaitForFunctionAsync("() => !document.querySelector('.filtering-box .filter-readout').parentElement.classList.contains('mobile-filter-line')",
            null, new() { PollingInterval = 200 });
        Assert.Equal("before Clear All", await ReadoutPlaceAsync());
    }

    [Fact]
    public async Task The_keyboard_reaches_the_Filters_button_and_opens_and_folds_the_rows()
    {
        await OpenAsync("MobileFilterLine_Keys.html", 400);
        await Page.Locator(".search-help-toggle").FocusAsync();

        await Page.Keyboard.PressAsync("Tab");
        Assert.True(await Filters.EvaluateAsync<bool>("b => document.activeElement === b"), "Tab from the search help went to the Filters button");
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Rows).ToBeHiddenAsync();

        await Page.Keyboard.PressAsync("Enter");
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Rows).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Space");
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Rows).ToBeHiddenAsync();
    }

    [Fact]
    public async Task On_a_phone_the_search_help_opens_while_the_rows_are_folded()
    {
        await OpenAsync("MobileFilterLine_Help.html", 400);
        await Page.Locator(".search-help-toggle").ClickAsync();

        await Expect(Page.Locator(".search-help-panel")).ToBeVisibleAsync();
        await Expect(Rows).ToBeHiddenAsync();
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task Rows_opened_on_a_phone_stay_open_on_a_wider_window_and_rows_folded_there_stay_folded_on_a_phone()
    {
        await OpenAsync("MobileFilterLine_Across.html", 400);
        await Filters.ClickAsync();
        await Expect(Rows).ToBeVisibleAsync();

        await Page.SetViewportSizeAsync(1280, 800);
        await Expect(Rows).ToBeVisibleAsync();
        await Expect(More).ToHaveAttributeAsync("aria-expanded", "true");

        await More.ClickAsync();
        await Expect(Rows).ToBeHiddenAsync();
        await Page.SetViewportSizeAsync(400, 800);
        await Expect(Rows).ToBeHiddenAsync();
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task A_page_opened_wide_then_narrowed_folds_its_rows_under_Filters_which_opens_them()
    {
        await OpenAsync("MobileFilterLine_Narrowed.html", 1280);
        await Page.SetViewportSizeAsync(400, 800);

        await Expect(Filters).ToBeVisibleAsync();
        await Expect(Rows).ToBeHiddenAsync();
        await Filters.ClickAsync();
        await Expect(Rows).ToBeVisibleAsync();
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "true");
    }

    [Fact]
    public async Task A_page_opened_on_a_phone_then_widened_opens_its_rows_with_More_filters()
    {
        await OpenAsync("MobileFilterLine_Widened.html", 400);
        await Page.SetViewportSizeAsync(1280, 800);

        await More.ClickAsync();
        await Expect(Rows).ToBeVisibleAsync();
        await Expect(More).ToHaveAttributeAsync("aria-expanded", "true");
    }

    [Fact]
    public async Task In_the_dark_theme_the_phones_sticky_search_line_has_the_filter_boxs_ground()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await OpenAsync("MobileFilterLine_Dark.html", 400);

        var grounds = await Page.EvaluateAsync<string[]>(
            "() => ['.filter-search', '.filtering-box'].map(s => getComputedStyle(document.querySelector(s)).backgroundColor)");
        Assert.Equal(grounds[1], grounds[0]);
        Assert.NotEqual("rgb(224, 224, 224)", grounds[0]);
    }
}
