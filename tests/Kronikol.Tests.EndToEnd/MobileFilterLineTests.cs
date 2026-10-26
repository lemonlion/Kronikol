using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The phone's Filters line. From 5.0.0 (plans/OPTION_C_PLAN.md S12) the filter rows folded under a Filters button of the
/// box's own on a phone, on a line the readout rode; since plans/SEARCH_PLAN.md F3 and F5 the box is hidden at every width
/// until the bar's Filters opens it, and on a phone the bar's Filters and its readout take a line of their own under the
/// tabs (F-n). The box keeps its state across the phone breakpoint either way, the keyboard reaches Filters, the search's
/// tips open in the builder's sheet, and the dark theme gives the phone's search box the theme switch's ground.
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
                Duration = TimeSpan.FromMilliseconds(10 * i + f), Tags = i % 3 == 0 ? ["smoke"] : null, IsHappyPath = f == 1 && i == 1
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
        await Page.GotoReportAsync(Generate(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await TwoAnimationFramesAsync(Page);
    }

    private ILocator Filters => Page.Locator(".nav-filters-toggle");
    private ILocator Box => Page.Locator("#filtering-box");
    private ILocator Readout => Page.Locator(".nav-readout");

    // whether the bar's Filters group is on a line of its own under the tabs, or on the tabs' own line
    private Task<string> FiltersLineAsync() => Page.EvaluateAsync<string>("""
        () => {
            const tabs = document.querySelector('.report-tabs').getBoundingClientRect();
            const group = document.querySelector('.nav-filters').getBoundingClientRect();
            return group.top >= tabs.bottom - 1 ? 'under the tabs' : 'beside the tabs';
        }
        """);

    // F-n
    [Fact]
    public async Task On_a_phone_Filters_and_the_readout_share_a_line_under_the_tabs()
    {
        await OpenAsync("MobileFilterLine_Readout.html", 400);

        Assert.Equal("under the tabs", await FiltersLineAsync());
        await Expect(Readout).ToBeVisibleAsync();
        var middles = await Page.EvaluateAsync<double[]>("""
            () => ['.nav-filters-toggle', '.nav-readout'].map(s => {
                const r = document.querySelector(s).getBoundingClientRect();
                return r.top + r.height / 2;
            })
            """);
        Assert.True(Math.Abs(middles[0] - middles[1]) <= 2, $"the readout's middle is at {middles[1]}, the button's at {middles[0]}");

        await FillSearchBar("Scenario 1.2");
        await Expect(Page.Locator(".nav-readout-long, .nav-readout-short").First).ToContainTextAsync("Filtered");
        Assert.Equal("under the tabs", await FiltersLineAsync());
    }

    [Fact]
    public async Task Across_the_phone_breakpoint_the_Filters_group_moves_between_the_tabs_line_and_its_own()
    {
        await OpenAsync("MobileFilterLine_Width.html", 1280);
        Assert.Equal("beside the tabs", await FiltersLineAsync());

        await Page.SetViewportSizeAsync(400, 800);
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("under the tabs", await FiltersLineAsync());

        await Page.SetViewportSizeAsync(1280, 800);
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("beside the tabs", await FiltersLineAsync());
    }

    [Fact]
    public async Task The_keyboard_reaches_Filters_and_opens_and_hides_the_box()
    {
        await OpenAsync("MobileFilterLine_Keys.html", 400);
        await Page.Locator("#tab-features").FocusAsync();

        for (var i = 0; i < 6 && !await Filters.EvaluateAsync<bool>("b => document.activeElement === b"); i++)
            await Page.Keyboard.PressAsync("Tab");
        Assert.True(await Filters.EvaluateAsync<bool>("b => document.activeElement === b"), "Tab from the tabs never reached Filters");
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Box).ToBeHiddenAsync();

        await Page.Keyboard.PressAsync("Enter");
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Box).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Box).ToBeHiddenAsync();
        await Expect(Filters).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Space");
        await Expect(Box).ToBeVisibleAsync();
    }

    [Fact]
    public async Task On_a_phone_the_search_tips_open_in_the_builder_s_sheet()
    {
        await OpenAsync("MobileFilterLine_Help.html", 400);
        await Page.Locator("#search-button").ClickAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>("() => document.getElementById('search-dialog').classList.contains('is-sheet')"));

        await Page.Locator(".search-tips-toggle").ClickAsync();
        await Expect(Page.Locator("#search-tips")).ToBeVisibleAsync();
        await Expect(Box).ToBeHiddenAsync();
    }

    [Fact]
    public async Task The_box_opened_on_a_phone_stays_open_on_a_wider_window_and_hidden_there_stays_hidden_on_a_phone()
    {
        await OpenAsync("MobileFilterLine_Across.html", 400);
        await Filters.ClickAsync();
        await Expect(Box).ToBeVisibleAsync();

        await Page.SetViewportSizeAsync(1280, 800);
        await Expect(Box).ToBeVisibleAsync();
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "true");

        await Filters.ClickAsync();
        await Expect(Box).ToBeHiddenAsync();
        await Page.SetViewportSizeAsync(400, 800);
        await Expect(Box).ToBeHiddenAsync();
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task A_page_opened_wide_then_narrowed_opens_its_box_with_Filters()
    {
        await OpenAsync("MobileFilterLine_Narrowed.html", 1280);
        await Page.SetViewportSizeAsync(400, 800);

        await Expect(Filters).ToBeVisibleAsync();
        await Expect(Box).ToBeHiddenAsync();
        await Filters.ClickAsync();
        await Expect(Box).ToBeVisibleAsync();
        await Expect(Page.Locator(".status-toggle").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_page_opened_on_a_phone_then_widened_opens_its_box_with_Filters()
    {
        await OpenAsync("MobileFilterLine_Widened.html", 400);
        await Page.SetViewportSizeAsync(1280, 800);

        await Filters.ClickAsync();
        await Expect(Box).ToBeVisibleAsync();
        await Expect(Filters).ToHaveAttributeAsync("aria-expanded", "true");
    }

    [Fact]
    public async Task In_the_dark_theme_the_phone_s_search_box_has_the_theme_switch_s_ground()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await OpenAsync("MobileFilterLine_Dark.html", 400);

        var grounds = await Page.EvaluateAsync<string[]>(
            "() => ['.search-box', '.theme-toggle'].map(s => getComputedStyle(document.querySelector(s)).backgroundColor)");
        Assert.Equal(grounds[1], grounds[0]);
        Assert.NotEqual("rgb(224, 224, 224)", grounds[0]);
    }
}
