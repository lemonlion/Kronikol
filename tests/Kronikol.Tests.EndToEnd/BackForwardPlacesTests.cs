using Microsoft.Playwright;
using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Back and Forward between the report's places (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md section 7.1, P1 to P12): a
/// tab, the Full or Summary view, a scenario reached by any link, a row of the Summary table and Next Failure each add an
/// entry, and Back puts back the place before, scrolled where the reader was, with the control they had used focused. Until
/// 5.2.0 nothing in the report added an entry, so Back left the report from wherever the reader was (the owner's case:
/// a History tab link to a scenario, then Back).
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class BackForwardPlacesTests : BackForwardTestBase
{
    public BackForwardPlacesTests(PlaywrightFixture fixture) : base(fixture) { }

    // short enough for the History tab and the failure clusters to scroll
    protected override int ViewportWidth => 1400;
    protected override int ViewportHeight => 520;

    private string EverySection(string fileName) => ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, fileName);

    private const string HistoryLinks = "#history-section a.history-link";

    // the owner's case, in each engine: the browsers differ in how they restore a position and when Back works (plan 7.5)
    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public Task P1_Back_from_the_scenario_a_History_link_opened_shows_the_History_tab_where_the_reader_was(string engine) => InEngineAsync(engine, async page =>
    {
        await OpenAsync(page, EverySection($"BackNav_P1_{engine}.html"));
        // a real interaction first: Firefox's Back does nothing on a page that has had none (plan fact 2)
        await page.Locator("h1").ClickAsync();
        await ActAsync(page, () => page.Locator("#tab-history").ClickAsync());
        var link = page.Locator(HistoryLinks).Nth(1);
        await link.ScrollIntoViewIfNeededAsync();
        await WheelAsync(page, 60);
        var href = (await link.GetAttributeAsync("href"))!;
        var linkSelector = $"{HistoryLinks}[href='{href}']";
        var top = await TopAsync(page, linkSelector);
        var scenario = $"details.scenario[data-stable-id='{href["#sid-".Length..]}']";

        await ActAsync(page, () => link.ClickAsync());
        Assert.Equal("features", await SelectedTabAsync(page));
        Assert.True(await IsOpenAsync(page, scenario), "the link opens its scenario");
        Assert.Equal(href, await HashAsync(page));

        await BackAsync(page);
        await StillInTheReportAsync(page, "Back from the scenario a History link opened");
        Assert.Equal("history", await SelectedTabAsync(page));
        Assert.InRange(await TopAsync(page, linkSelector) - top, -2, 2);
        Assert.Equal("href=" + href, await FocusedAsync(page));

        await ForwardAsync(page);
        Assert.Equal("features", await SelectedTabAsync(page));
        Assert.True(await IsOpenAsync(page, scenario));
        Assert.True(await TitleOnScreenAsync(page, scenario), "Forward shows the scenario where the reader left it");
    });

    [Fact]
    public async Task P2_Back_and_Forward_walk_through_the_tabs_a_reader_opened()
    {
        await OpenAsync(EverySection("BackNav_P2.html"));
        foreach (var tab in new[] { "timeline", "components", "history" })
            await ActAsync(() => Page.Locator($"#tab-{tab}").ClickAsync());
        Assert.Equal("#tab=history", await HashAsync());

        foreach (var tab in new[] { "components", "timeline", "features" })
        {
            await BackAsync();
            await StillInTheReportAsync($"Back to the {tab} tab");
            Assert.Equal(tab, await SelectedTabAsync());
        }
        Assert.Equal("", await HashAsync());
        foreach (var tab in new[] { "timeline", "components", "history" })
        {
            await ForwardAsync();
            Assert.Equal(tab, await SelectedTabAsync());
        }
    }

    [Fact]
    public async Task P3_The_tab_already_selected_and_the_view_already_shown_add_no_entry()
    {
        await OpenAsync(EverySection("BackNav_P3.html"));
        var length = await HistoryLengthAsync();

        await Page.Locator("#tab-features").ClickAsync();
        await Page.Locator("#view-full").ClickAsync();
        await TwoAnimationFramesAsync(Page);

        Assert.Equal(length, await HistoryLengthAsync());
    }

    [Fact]
    public async Task P4_A_run_of_arrow_keys_across_the_tabs_is_one_entry()
    {
        await OpenAsync(EverySection("BackNav_P4_Tabs.html"));
        var length = await HistoryLengthAsync();
        await Page.Locator("#tab-features").FocusAsync();

        for (var i = 0; i < 3; i++)
            await ActAsync(() => Page.Keyboard.PressAsync("ArrowRight"));
        Assert.Equal("history", await SelectedTabAsync());
        Assert.Equal(length + 1, await HistoryLengthAsync());

        await BackAsync();
        await StillInTheReportAsync("Back after the arrow keys");
        Assert.Equal("features", await SelectedTabAsync());
    }

    [Fact]
    public async Task P4_A_run_of_arrow_keys_across_the_views_is_one_entry()
    {
        await OpenAsync(EverySection("BackNav_P4_Views.html"));
        var length = await HistoryLengthAsync();
        await Page.Locator("#view-full").FocusAsync();

        foreach (var key in new[] { "ArrowRight", "ArrowLeft", "ArrowRight" })
            await ActAsync(() => Page.Keyboard.PressAsync(key));
        Assert.Equal("summary", await SelectedViewAsync());
        Assert.Equal(length + 1, await HistoryLengthAsync());

        await BackAsync();
        await StillInTheReportAsync("Back after the arrow keys");
        Assert.Equal("full", await SelectedViewAsync());
    }

    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public Task P5_Back_from_a_card_a_Summary_row_opened_shows_the_table_then_the_Full_view(string engine) => InEngineAsync(engine, async page =>
    {
        await OpenAsync(page, EverySection($"BackNav_P5_{engine}.html"));
        await ActAsync(page, () => page.Locator("#view-summary").ClickAsync());
        Assert.Equal("#view=summary", await HashAsync(page));
        const string row = "#features-table-view tbody tr:nth-child(2)";
        // where the reader is as they click: the click brings the row into view first, and Back returns to that
        await page.Locator(row).ScrollIntoViewIfNeededAsync();
        await ScrollSettledAsync(page);
        var top = await TopAsync(page, row);

        await ActAsync(page, () => page.Locator(row).ClickAsync());
        Assert.Equal("full", await SelectedViewAsync(page));

        await BackAsync(page);
        await StillInTheReportAsync(page, "Back from the card");
        Assert.Equal("summary", await SelectedViewAsync(page));
        Assert.InRange(await TopAsync(page, row) - top, -2, 2);

        await BackAsync(page);
        Assert.Equal("full", await SelectedViewAsync(page));
        Assert.Equal("", await HashAsync(page));
    });

    [Fact]
    public async Task P6_Back_from_a_failure_clusters_scenario_returns_to_the_link_where_it_was()
    {
        await OpenAsync(EverySection("BackNav_P6.html"));
        await Page.EvaluateAsync("() => document.querySelectorAll('.failure-clusters, .failure-clusters details').forEach(d => d.open = true)");
        var link = Page.Locator("a.failure-cluster-scenario-link").Nth(2);
        await link.ScrollIntoViewIfNeededAsync();
        await WaitForScrollToSettleAsync();
        var href = (await link.GetAttributeAsync("href"))!;
        var linkSelector = $"a.failure-cluster-scenario-link[href='{href}']";
        var top = await TopAsync(linkSelector);

        await ActAsync(() => link.ClickAsync());
        Assert.Equal(href, await HashAsync());

        await BackAsync();
        await StillInTheReportAsync("Back from a failure cluster's scenario");
        Assert.InRange(await TopAsync(linkSelector) - top, -2, 2);
        Assert.Equal("href=" + href, await FocusedAsync());
    }

    [Fact]
    public async Task P7_The_link_button_keeps_the_filters_in_the_address_and_Back_returns()
    {
        await OpenAsync(GenerateLongReport("BackNav_P7.html", "Scenario 02.1", "Scenario 05.2"));
        await OpenFilterRowsAsync();
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        Assert.Equal("#status=Failed", await HashSoonAsync("#status=Failed"));

        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(f => f.open = true)");
        var scenario = Page.Locator("details.scenario[data-status='Failed']").Nth(1);
        var id = (await scenario.GetAttributeAsync("id"))!;
        await ActAsync(() => scenario.Locator(":scope > summary a.scenario-link").ClickAsync());
        Assert.Equal($"#{id}&status=Failed", await HashSoonAsync($"#{id}&status=Failed"));

        await BackAsync();
        await StillInTheReportAsync("Back from the link button");
        Assert.Equal("#status=Failed", await HashAsync());
        await Expect(Page.Locator(".status-toggle[data-status='Failed']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bstatus-active\\b"));
    }

    [Fact]
    public async Task P8_Next_Failure_pressed_from_the_History_tab_steps_back_through_the_failures_then_to_the_tab()
    {
        await OpenAsync(EverySection("BackNav_P8.html"));
        await ActAsync(() => Page.Locator("#tab-history").ClickAsync());
        var addresses = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            await ActAsync(() => Page.Locator(".jump-to-failure").ClickAsync());
            addresses.Add(await HashAsync());
        }
        Assert.Equal("features", await SelectedTabAsync());
        Assert.All(addresses, a => Assert.StartsWith("#sid-", a));
        Assert.Equal(3, addresses.Distinct().Count());

        await BackAsync();
        await StillInTheReportAsync("Back from the third failure");
        Assert.Equal(addresses[1], await HashAsync());
        await Expect(Page.Locator("#failure-counter")).ToHaveTextAsync("(2/3)");
        await BackAsync();
        Assert.Equal(addresses[0], await HashAsync());
        await Expect(Page.Locator("#failure-counter")).ToHaveTextAsync("(1/3)");
        await BackAsync();
        Assert.Equal("history", await SelectedTabAsync());
    }

    [Fact]
    public async Task P9_A_reload_opens_the_same_tab_view_scenario_and_filters_scrolled_where_the_reader_was()
    {
        await OpenAsync(EverySection("BackNav_P9.html"));
        await ActAsync(() => Page.Locator("#tab-history").ClickAsync());
        await WheelAsync(200);
        var y = await Page.EvaluateAsync<double>("() => scrollY");
        await ReloadAsync();
        Assert.Equal("history", await SelectedTabAsync());
        Assert.InRange(await Page.EvaluateAsync<double>("() => scrollY") - y, -2, 2);

        var link = Page.Locator(HistoryLinks).First;
        var href = (await link.GetAttributeAsync("href"))!;
        await ActAsync(() => link.ClickAsync());
        var scenario = $"details.scenario[data-stable-id='{href["#sid-".Length..]}']";
        var top = await TopAsync(scenario);
        await ReloadAsync();
        Assert.Equal("features", await SelectedTabAsync());
        Assert.True(await IsOpenAsync(scenario));
        Assert.InRange(await TopAsync(scenario) - top, -2, 2);

        await ActAsync(() => Page.Locator("#view-summary").ClickAsync());
        await OpenFilterRowsAsync();
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        Assert.Equal("#view=summary&status=Failed", await HashSoonAsync("#view=summary&status=Failed"));
        await ReloadAsync();
        Assert.Equal("summary", await SelectedViewAsync());
        await Expect(Page.Locator(".status-toggle[data-status='Failed']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bstatus-active\\b"));
    }

    private async Task ReloadAsync()
    {
        await Page.ReloadAsync();
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await SettledAsync(Page, 0);
    }

    [Fact]
    public async Task P10_An_address_that_names_a_tab_opens_it()
    {
        await OpenAsync(EverySection("BackNav_P10.html"), "#tab=history");
        Assert.Equal("history", await SelectedTabAsync());
        await Expect(Page.Locator("#panel-history")).ToBeVisibleAsync();
        await Expect(Page.Locator("#panel-features")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task P11_Addresses_written_before_5_2_0_open_as_they_did()
    {
        // a guard: version 2 addresses, and 4.x's, hold no tab= or view=
        var url = EverySection("BackNav_P11.html");
        await OpenAsync(url);
        var failed = Page.Locator("details.scenario[data-status='Failed']").First;
        var sid = (await failed.GetAttributeAsync("data-stable-id"))!;
        var dependency = (await Page.Locator(".filtering-box .dependency-toggle").First.GetAttributeAsync("data-dependency"))!;

        await OpenAsync(url, $"#sid-{sid}&status=Failed&deps={Uri.EscapeDataString(dependency)}");
        Assert.Equal("features", await SelectedTabAsync());
        Assert.True(await IsOpenAsync($"details.scenario[data-stable-id='{sid}']"));
        await Expect(Page.Locator(".status-toggle[data-status='Failed']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bstatus-active\\b"));
        Assert.Equal("in", await Page.EvaluateAsync<string>("v => chip_state(find_chip('dependency', v))", dependency));

        var slug = ReportGenerator.GenerateScenarioAnchorId("Pay by card");
        await OpenAsync(url, "#" + slug);
        Assert.True(await IsOpenAsync($"#{slug}"));
        Assert.True(await SummaryIsOnScreenAsync($"#{slug}"));
    }

    [Theory]
    [InlineData(KeyboardModifier.Control)]
    [InlineData(KeyboardModifier.Shift)]
    public async Task P12_Ctrl_or_Shift_click_on_a_History_link_or_a_cluster_link_opens_the_scenario_in_a_new_page(KeyboardModifier modifier)
    {
        await OpenAsync(EverySection($"BackNav_P12_{modifier}.html"));
        await ActAsync(() => Page.Locator("#tab-history").ClickAsync());
        var link = Page.Locator(HistoryLinks).First;
        var href = (await link.GetAttributeAsync("href"))!;
        var here = Page.Url;

        var opened = await Context.RunAndWaitForPageAsync(() => link.ClickAsync(new() { Modifiers = [modifier] }),
            new() { Timeout = 10000 });
        // a new tab starts at about:blank and goes on to the link
        await opened.WaitForURLAsync(u => u.EndsWith(href, StringComparison.Ordinal), new() { Timeout = 10000 });
        Assert.EndsWith(href, opened.Url);
        Assert.Equal(here, Page.Url);
        Assert.Equal("history", await SelectedTabAsync());
        await opened.CloseAsync();

        await ActAsync(() => Page.Locator("#tab-features").ClickAsync());
        await Page.EvaluateAsync("() => document.querySelectorAll('.failure-clusters, .failure-clusters details').forEach(d => d.open = true)");
        var cluster = Page.Locator("a.failure-cluster-scenario-link").First;
        var clusterHref = (await cluster.GetAttributeAsync("href"))!;
        here = Page.Url;
        opened = await Context.RunAndWaitForPageAsync(() => cluster.ClickAsync(new() { Modifiers = [modifier] }),
            new() { Timeout = 10000 });
        // a new tab starts at about:blank and goes on to the link
        await opened.WaitForURLAsync(u => u.EndsWith(clusterHref, StringComparison.Ordinal), new() { Timeout = 10000 });
        Assert.EndsWith(clusterHref, opened.Url);
        Assert.Equal(here, Page.Url);
        await opened.CloseAsync();
    }
}
