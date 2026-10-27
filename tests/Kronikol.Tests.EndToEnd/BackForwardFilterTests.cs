using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Back and Forward through the filters (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md section 7.1, F1 to F9): each filter
/// control used adds an entry and a run of changes to one control is one (a word typed in the search, a chip cycled
/// through its states), so Back undoes a filter as a site's faceted search does. An address reached by Back, or typed into
/// an open report, is applied exactly: until 5.2.0 its filters were ignored, and on a load they were only ever added.
/// </summary>
[Collection(PlaywrightCollections.Search)]
public class BackForwardFilterTests : BackForwardTestBase
{
    public BackForwardFilterTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1400;
    protected override int ViewportHeight => 800;

    private ILocator Chip(string tag) => Page.Locator($".filtering-box .tag-toggle[data-tag='{tag}']");

    private Task<string> ChipStateAsync(string tag) =>
        Page.EvaluateAsync<string>("v => chip_state(find_chip('tag', v))", tag);

    private Task<bool> FailedChosenAsync() =>
        Page.Locator(".status-toggle[data-status='Failed']").EvaluateAsync<bool>("b => b.classList.contains('status-active')");

    private Task<int> HiddenScenariosAsync() =>
        Page.EvaluateAsync<int>("() => Array.from(document.querySelectorAll('details.scenario')).filter(s => s.style.display === 'none').length");

    private async Task OpenLongAsync(string fileName, params string[] failing)
    {
        await OpenAsync(GenerateLongReport(fileName, failing));
        await OpenFilterRowsAsync();
    }

    [Fact]
    public async Task F1_Back_takes_a_chip_off_and_Forward_puts_it_on_again()
    {
        await OpenLongAsync("BackNav_F1.html");
        var readout = await Page.Locator(".filter-readout").TextContentAsync();

        await Chip("area-03").ClickAsync();
        Assert.Equal("#tags=area-03", await HashSoonAsync("#tags=area-03"));

        await BackAsync();
        await StillInTheReportAsync("Back after choosing a chip");
        Assert.Equal("off", await ChipStateAsync("area-03"));
        Assert.Equal(readout, await Page.Locator(".filter-readout").TextContentAsync());
        Assert.Equal("", await HashAsync());

        await ForwardAsync();
        Assert.Equal("in", await ChipStateAsync("area-03"));
        Assert.Equal("#tags=area-03", await HashAsync());
    }

    [Fact]
    public async Task F2_A_word_typed_in_the_filter_field_is_one_entry()
    {
        await OpenLongAsync("BackNav_F2.html");
        await OpenFilterRowsAsync();
        var length = await HistoryLengthAsync();

        // the field filters as it is typed, one entry for the word and not one a letter (5.2.0; the field is back in the
        // box in 5.12.0)
        await Page.Locator("#filtering-box #searchbar").PressSequentiallyAsync("Scenario 07", new() { Delay = 40 });
        Assert.Equal("#q=Scenario%2007", await HashSoonAsync("#q=Scenario%2007"));
        Assert.Equal(length + 1, await HistoryLengthAsync());

        await BackAsync();
        await StillInTheReportAsync("Back after a filter");
        Assert.Equal("", await Page.Locator("#searchbar").InputValueAsync());
        Assert.Equal(0, await HiddenScenariosAsync());
    }

    [Fact]
    public async Task F3_A_chip_cycled_through_its_states_is_one_entry_and_another_chip_a_second()
    {
        await OpenLongAsync("BackNav_F3.html");
        // the cycle is a three-way row's (5.10.0): the switch is an entry of its own
        await Page.Locator(".filtering-box .tag-ways-toggle").ClickAsync();
        Assert.Equal("#tagstates=3", await HashSoonAsync("#tagstates=3"));
        var length = await HistoryLengthAsync();

        await Chip("area-03").ClickAsync();
        await Chip("area-03").ClickAsync();
        Assert.Equal("#tagx=area-03&tagstates=3", await HashSoonAsync("#tagx=area-03&tagstates=3"));
        Assert.Equal(length + 1, await HistoryLengthAsync());

        await Chip("area-05").ClickAsync();
        Assert.Equal("#tags=area-05&tagx=area-03&tagstates=3", await HashSoonAsync("#tags=area-05&tagx=area-03&tagstates=3"));
        Assert.Equal(length + 2, await HistoryLengthAsync());

        await BackAsync();
        await StillInTheReportAsync("Back after the second chip");
        Assert.Equal("off", await ChipStateAsync("area-05"));
        Assert.Equal("out", await ChipStateAsync("area-03"));
    }

    // ── 5.10.0 (plans/REPORT_UI_FIXES_PLAN.md I1, C7): the rows' ways switches and Back ──

    // a small report with a happy path, for Happy paths' Hide and Show
    private string GenerateHappyReport(string fileName)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new() { Id = "h1", DisplayName = "Create order", Result = ExecutionResult.Passed, IsHappyPath = true, Tags = ["api"] },
                    new() { Id = "h2", DisplayName = "Reject order", Result = ExecutionResult.Failed, ErrorMessage = "boom", Tags = ["api"] },
                    new() { Id = "h3", DisplayName = "Refund", Result = ExecutionResult.Passed, Tags = ["smoke"] }
                ]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, fileName), "Bakery", true, diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    // a flip of a row's switch is a control of its own: one entry, which Back turns back
    [Fact]
    public async Task A_switch_flip_is_one_step_back()
    {
        await OpenLongAsync("BackNav_Ways.html");
        var ways = Page.Locator(".filtering-box .tag-ways-toggle");
        var length = await HistoryLengthAsync();
        await ways.ClickAsync();
        Assert.Equal("#tagstates=3", await HashSoonAsync("#tagstates=3"));
        Assert.Equal(length + 1, await HistoryLengthAsync());

        await BackAsync();
        await StillInTheReportAsync("Back after the switch");
        await Expect(ways).ToHaveTextAsync("2-WAY");
        await ForwardAsync();
        await StillInTheReportAsync("Forward after the switch");
        await Expect(ways).ToHaveTextAsync("3-WAY");
    }

    // a guard: Happy paths' Hide and then Show are one entry, as in 5.2.0, though Hide makes the Tags row three-way (C4):
    // a row's ways are never written for a left-out chip
    [Fact]
    public async Task Hide_then_Show_is_one_step_back_as_in_5_2_0()
    {
        await OpenAsync(GenerateHappyReport("BackNav_HideShow.html"));
        await OpenFilterRowsAsync();
        var length = await HistoryLengthAsync();
        await Page.Locator(".happy-seg[data-hp='out']").ClickAsync();
        Assert.Equal("#hpx=1", await HashSoonAsync("#hpx=1"));
        await Page.Locator(".happy-seg[data-hp='off']").ClickAsync();
        Assert.Equal("", await HashSoonAsync(""));
        Assert.Equal(length + 1, await HistoryLengthAsync());
    }

    // a guard: the first chip change after a link that left a chip out (one 5.0.0 to 5.9.0 wrote) is one entry, as it was
    [Fact]
    public async Task The_first_chip_change_after_an_old_link_is_one_step_back()
    {
        await OpenAsync(GenerateLongReport("BackNav_OldLink.html"), "#tagx=area-03");
        await OpenFilterRowsAsync();
        var length = await HistoryLengthAsync();
        await Chip("area-05").ClickAsync();
        Assert.Equal("#tags=area-05&tagx=area-03", await HashSoonAsync("#tags=area-05&tagx=area-03"));
        Assert.Equal(length + 1, await HistoryLengthAsync());

        await BackAsync();
        await StillInTheReportAsync("Back after the first chip");
        Assert.Equal("off", await ChipStateAsync("area-05"));
        Assert.Equal("out", await ChipStateAsync("area-03"));
    }

    [Fact]
    public async Task F4_Back_after_Clear_All_puts_every_filter_back()
    {
        await OpenLongAsync("BackNav_F4.html", "Scenario 02.1");
        await Chip("area-02").ClickAsync();
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        var filtered = await HashSoonAsync("#status=Failed&tags=area-02");
        Assert.Equal("#status=Failed&tags=area-02", filtered);

        // the box's own Clear All, the box being open (the bar's copy steps aside while it is)
        await Page.Locator(".filtering-box-export .export-btn", new() { HasTextString = "Clear All" }).ClickAsync();
        Assert.Equal("", await HashSoonAsync(""));

        await BackAsync();
        await StillInTheReportAsync("Back after Clear All");
        Assert.Equal(filtered, await HashAsync());
        Assert.True(await FailedChosenAsync());
        Assert.Equal("in", await ChipStateAsync("area-02"));
    }

    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public Task F5_Back_to_an_entry_without_a_filter_takes_it_off_rather_than_adding_to_it(string engine) => InEngineAsync(engine, async page =>
    {
        await OpenAsync(page, GenerateLongReport($"BackNav_F5_{engine}.html", "Scenario 02.1"));
        await OpenFilterRowsAsync(page);
        await page.Locator(".filtering-box .tag-toggle[data-tag='area-02']").ClickAsync();
        await page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        await page.WaitForFunctionAsync("() => location.hash === '#status=Failed&tags=area-02'", null, new() { PollingInterval = 200, Timeout = 5000 });

        await BackAsync(page);
        await StillInTheReportAsync(page, "Back to the chip alone");
        Assert.False(await page.Locator(".status-toggle[data-status='Failed']").EvaluateAsync<bool>("b => b.classList.contains('status-active')"),
            "Failed is off on the entry before it was chosen");
        Assert.Equal("in", await page.EvaluateAsync<string>("() => chip_state(find_chip('tag', 'area-02'))"));
        Assert.Equal("#tags=area-02", await HashAsync(page));
    });

    [Fact]
    public async Task F6_An_address_with_filters_typed_into_an_open_report_applies_them()
    {
        var url = GenerateLongReport("BackNav_F6.html", "Scenario 02.1", "Scenario 04.3");
        await OpenAsync(url);
        Assert.False(await FailedChosenAsync());

        await ActAsync(() => Page.GotoReportAsync(url + "#status=Failed"));

        Assert.True(await FailedChosenAsync(), "the typed address's filter is applied");
        Assert.Equal(178, await HiddenScenariosAsync());
    }

    [Fact]
    public async Task F7_A_link_to_a_scenario_the_filters_hide_says_so_and_its_button_clears_them_and_shows_it()
    {
        await OpenAsync(ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, "BackNav_F7.html"));
        await OpenFilterRowsAsync();
        await Page.Locator(".status-toggle[data-status='Passed']").ClickAsync();
        await ActAsync(() => Page.Locator("#tab-history").ClickAsync());
        var link = Page.Locator("#history-section a.history-link").First;
        var sid = (await link.GetAttributeAsync("href"))!["#sid-".Length..];
        var scenario = Page.Locator($"details.scenario[data-stable-id='{sid}']");
        Assert.Equal("Failed", await scenario.GetAttributeAsync("data-status"));

        await ActAsync(() => link.ClickAsync());

        var note = Page.Locator(".filtered-out-note");
        await Expect(note).ToBeVisibleAsync();
        await Expect(note).ToContainTextAsync("hidden by the filters");
        // Clear All and the scenario the address already names: the filters change, the place does not
        await note.Locator("button").ClickAsync();
        await Expect(note).ToHaveCountAsync(0);
        Assert.False(await Page.Locator(".status-toggle[data-status='Passed']").EvaluateAsync<bool>("b => b.classList.contains('status-active')"));
        await Expect(scenario).ToBeVisibleAsync();
        Assert.True(await IsOpenAsync($"details.scenario[data-stable-id='{sid}']"));
    }

    [Fact]
    public async Task F8_Emptying_the_search_leaves_open_a_scenario_the_reader_had_opened()
    {
        await OpenLongAsync("BackNav_F8.html");
        var scenario = Page.Locator("details.scenario", new() { HasTextString = "Scenario 03.1" }).First;
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(f => f.open = true)");
        await scenario.Locator(":scope > summary").ClickAsync();
        Assert.True(await scenario.EvaluateAsync<bool>("d => d.open"));

        await FillSearchBar("Scenario 03.1");
        await Page.WaitForFunctionAsync("() => location.hash.indexOf('q=') >= 0", null, new() { PollingInterval = 200, Timeout = 5000 });
        await FillSearchBar("");
        await Page.WaitForFunctionAsync("() => location.hash === ''", null, new() { PollingInterval = 200, Timeout = 5000 });

        Assert.True(await scenario.EvaluateAsync<bool>("d => d.open"), "the scenario the reader opened stays open");
    }

    [Fact]
    public async Task F9_A_filter_changed_after_going_Back_adds_an_entry_so_Forward_has_nothing_to_go_to()
    {
        await OpenLongAsync("BackNav_F9.html");
        await Chip("area-03").ClickAsync();
        Assert.Equal("#tags=area-03", await HashSoonAsync("#tags=area-03"));
        await BackAsync();
        await StillInTheReportAsync("Back after the first chip");

        await Chip("area-05").ClickAsync();
        Assert.Equal("#tags=area-05", await HashSoonAsync("#tags=area-05"));
        await Page.GoForwardReportAsync();
        await TwoAnimationFramesAsync(Page);

        Assert.Equal("#tags=area-05", await HashAsync());
        Assert.Equal("in", await ChipStateAsync("area-05"));
        Assert.Equal("off", await ChipStateAsync("area-03"));
        await BackAsync();
        Assert.Equal("", await HashAsync());
    }

    [Fact]
    public async Task F10_A_search_with_one_match_shows_it_in_its_feature_and_emptying_the_search_closes_what_it_opened()
    {
        // The long report's features start closed: the one match opened inside a closed feature, out of sight, until 5.2.0.
        // Emptying the search closes what it opened, the feature too, and leaves a feature the reader had opened open.
        await OpenLongAsync("BackNav_F10.html");
        var feature = Page.Locator("details.feature", new() { HasTextString = "Scenario 04.2" }).First;
        var scenario = feature.Locator("details.scenario", new() { HasTextString = "Scenario 04.2" }).First;
        var readersOwn = Page.Locator("details.feature").First;
        await readersOwn.Locator(":scope > summary").ClickAsync();
        Assert.False(await feature.EvaluateAsync<bool>("d => d.open"));

        await FillSearchBar("Scenario 04.2");
        await Expect(scenario).ToBeVisibleAsync();
        Assert.True(await scenario.EvaluateAsync<bool>("d => d.open"));

        await FillSearchBar("");
        await Page.WaitForFunctionAsync("() => location.hash === ''", null, new() { PollingInterval = 200, Timeout = 5000 });
        Assert.False(await feature.EvaluateAsync<bool>("d => d.open"), "the feature the search opened closes");
        Assert.False(await scenario.EvaluateAsync<bool>("d => d.open"), "the scenario the search opened closes");
        Assert.True(await readersOwn.EvaluateAsync<bool>("d => d.open"), "the feature the reader opened stays open");
    }
}
