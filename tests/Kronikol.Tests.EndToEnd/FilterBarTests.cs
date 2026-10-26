using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The filter box folded under More filters, and the report bar carrying the filters (5.0.0, plans/OPTION_C_PLAN.md S7,
/// D15, D17, D18, D24, D25): More filters opens and folds the rows on a desktop and counts the filters on in them while
/// they are folded; the search help opens on its own; the bar's copy of the readout and its Clear All follow the box;
/// Filters, shown while the bar floats, takes the box into the bar and Esc or the close puts it back; the empty state
/// says when no scenario is left and its Clear All returns to the search; the bar says when it is pinned; Duration and
/// Happy paths join the Status line when they fit whole; and the deep search's chip sits inside the field.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class FilterBarTests : PlaywrightTestBase
{
    public FilterBarTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string ZebraSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Zebra Service\" as z\ncaller -> z : GET /zebra\n@enduml";

    // thirty scenarios in three features, so the page scrolls past the bar; every third tagged smoke, one a happy path,
    // and one with a diagram, for the deep search
    private string Generate(string fileName, bool durations = true)
    {
        var features = Enumerable.Range(1, 3).Select(f => new Feature
        {
            DisplayName = $"Feature {f}",
            Scenarios = Enumerable.Range(1, 10).Select(i => new Scenario
            {
                Id = $"f{f}s{i}", DisplayName = $"Scenario {f}.{i}", Result = i == 5 && f == 2 ? ExecutionResult.Failed : ExecutionResult.Passed,
                Duration = durations ? TimeSpan.FromMilliseconds(10 * i + f) : null, Tags = i % 3 == 0 ? ["smoke"] : null, IsHappyPath = f == 1 && i == 1,
                ErrorMessage = i == 5 && f == 2 ? "boom" : null
            }).ToArray()
        }).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("f1s2", "", ZebraSource)], features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenAsync(string fileName, string hash = "", int width = 1400, int height = 700)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoReportAsync(Generate(fileName) + hash);
        await Page.Locator("details.feature").First.WaitForAsync();
        await TwoAnimationFramesAsync(Page);
    }

    private ILocator Box => Page.Locator("#filtering-box");
    private ILocator Toggle => Page.Locator(".nav-filters-toggle");

    // a reading taken once the page has stopped moving: opening the features brings a scrollbar, and the layouts measured
    // on the page's width (the run band, the filter rows) follow it a frame or two later
    private async Task<string> Settled(string script)
    {
        var last = await Page.EvaluateAsync<string>(script);
        for (var i = 0; i < 10; i++)
        {
            await TwoAnimationFramesAsync(Page);
            var now = await Page.EvaluateAsync<string>(script);
            if (now == last) return now;
            last = now;
        }
        return last;
    }

    private async Task ScrollPastTheBar()
    {
        // the features open, so the page runs on past the bar
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.open = true)");
        await Page.EvaluateAsync("() => window.scrollTo(0, document.querySelector('.nav-mark').getBoundingClientRect().top + window.scrollY + 300)");
        await Page.WaitForFunctionAsync("() => document.querySelector('.report-nav').classList.contains('k-floating')", null, new() { PollingInterval = 200 });
    }

    // plans/SEARCH_PLAN.md F4, F5: the box has no fold and no More filters; the bar's Filters shows and hides it whole
    [Fact]
    public async Task Filters_opens_and_hides_the_box_on_a_desktop()
    {
        await OpenAsync("FilterBar_Fold.html");
        var passed = Page.Locator(".status-toggle[data-status='Passed']");
        await Expect(Box).ToBeHiddenAsync();
        await Expect(Page.Locator(".filtering-box .more-filters")).ToHaveCountAsync(0);
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Toggle).ToHaveAttributeAsync("title", "Show the filters");

        await Toggle.ClickAsync();
        await Expect(passed).ToBeVisibleAsync();
        await Expect(Page.Locator(".duration-filters")).ToBeVisibleAsync();
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Toggle).ToHaveAttributeAsync("title", "Hide the filters");

        await Toggle.ClickAsync();
        await Expect(Box).ToBeHiddenAsync();
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "false");
    }

    // F6: Filters counts the filters on, the box open or not, and Clear All takes the count away
    [Fact]
    public async Task Filters_counts_the_filters_on_as_they_change_and_Clear_All_empties_it()
    {
        await OpenAsync("FilterBar_Count.html", "#status=Passed&tags=smoke");
        // the bar's Filters; the Search tab's foot has a Filters of its own, with its own count (plans/SEARCH_PLAN.md 4.5)
        var count = Page.Locator(".nav-filters-toggle .filters-count");
        await Expect(count).ToHaveTextAsync("2");
        await Expect(Toggle).ToHaveAttributeAsync("aria-label", "Filters, 2 on");

        await Toggle.ClickAsync();
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        await Expect(count).ToHaveTextAsync("3");
        await Expect(Toggle).ToHaveAttributeAsync("aria-label", "Filters, 3 on");

        await Page.Locator(".filtering-box-export .export-btn", new() { HasTextString = "Clear All" }).ClickAsync();
        await Expect(count).ToBeHiddenAsync();
        await Expect(Toggle).ToHaveAttributeAsync("aria-label", "Filters");
    }

    // 4.3.5: the search's help is the query builder's tips; the box keeps no help of its own
    [Fact]
    public async Task The_search_help_is_the_builder_s_tips_and_not_in_the_box()
    {
        await OpenAsync("FilterBar_Help.html");
        await Expect(Page.Locator(".filtering-box .search-help-toggle, .filtering-box .search-help-panel")).ToHaveCountAsync(0);

        await Page.Locator("#search-button").ClickAsync();
        await Page.Locator(".search-tips-toggle").ClickAsync();
        await Expect(Page.Locator("#search-tips")).ToBeVisibleAsync();
        await Expect(Box).ToBeHiddenAsync();
    }

    // F3, F5: a phone has the bar's Filters on its line under the tabs, and no Filters button of the box's own
    [Fact]
    public async Task A_phone_has_the_bar_s_Filters_and_no_toggle_of_the_box_s_own()
    {
        await OpenAsync("FilterBar_Phone.html", width: 390, height: 844);
        await Expect(Page.Locator(".mobile-filter-toggle, .filtering-box .more-filters")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".nav-filters")).ToBeVisibleAsync();

        await Toggle.ClickAsync();
        await Expect(Page.Locator(".status-toggle").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_bar_copies_the_readout_and_offers_Clear_All_while_filtered()
    {
        await OpenAsync("FilterBar_Readout.html");
        var longReadout = Page.Locator(".nav-readout-long");
        await Expect(longReadout).ToHaveTextAsync("All 30 scenarios, 3 features");
        await Expect(Page.Locator(".nav-clear")).ToBeHiddenAsync();

        await FillSearchBar("@smoke");
        await Expect(longReadout).ToHaveTextAsync("Filtered 9 of 30 scenarios, 3 of 3 features");
        await Expect(Page.Locator(".nav-readout-short")).ToHaveTextAsync("Filtered 9 of 30");
        await Expect(Toggle).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-active\\b"));

        await Page.Locator(".nav-clear").ClickAsync();
        await Expect(longReadout).ToHaveTextAsync("All 30 scenarios, 3 features");
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
        await Expect(Page.Locator(".nav-clear")).ToBeHiddenAsync();
    }

    // F2, F4: the box is written at the bar's top and goes with it; opened on a floating bar, what was being read stays
    // where it was, the focus goes to the box, and Escape closes it and gives the focus back to Filters
    [Fact]
    public async Task Filters_on_a_floating_bar_opens_the_box_in_it_and_Escape_gives_the_focus_back()
    {
        await OpenAsync("FilterBar_Ride.html");
        await ScrollPastTheBar();
        await Expect(Toggle).ToBeVisibleAsync();
        // what is just below the bar, which opening and closing keep where it is
        const string below = "() => { const r = document.querySelector('.report-nav').getBoundingClientRect(); const e = document.elementFromPoint(r.left + r.width / 2, r.bottom + 8); return e ? Math.round(e.getBoundingClientRect().top - r.bottom) + ' ' + (e.id || e.className) : ''; }";
        var reading = await Settled(below);

        await Toggle.ClickAsync();
        await Expect(Page.Locator(".report-nav > #filtering-box")).ToHaveCountAsync(1);
        await Expect(Box).ToBeVisibleAsync();
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Box).ToBeFocusedAsync();
        await Expect(Page.Locator(".filters-close")).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Box).ToBeHiddenAsync();
        await Expect(Toggle).ToBeFocusedAsync();
        Assert.Equal(reading, await Settled(below));
    }

    // F3: Filters shows at rest too, and the box opened on a floating bar stays open as the bar comes to rest
    [Fact]
    public async Task Filters_shows_at_rest_and_the_box_stays_open_as_the_bar_comes_to_rest()
    {
        await OpenAsync("FilterBar_Rest.html");
        await Expect(Toggle).ToBeVisibleAsync();
        await ScrollPastTheBar();
        await Toggle.ClickAsync();
        await Expect(Box).ToBeVisibleAsync();

        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await Page.WaitForFunctionAsync("() => !document.querySelector('.report-nav').classList.contains('k-floating')", null, new() { PollingInterval = 200 });
        await Expect(Box).ToBeVisibleAsync();
        await Expect(Toggle).ToBeVisibleAsync();
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "true");
    }

    [Fact]
    public async Task No_scenario_left_shows_the_empty_state_whose_Clear_All_returns_to_the_search()
    {
        await OpenAsync("FilterBar_Empty.html");
        var empty = Page.Locator(".filters-empty");
        await Expect(empty).ToBeHiddenAsync();

        await FillSearchBar("nothing-matches-this");
        await Expect(empty).ToBeVisibleAsync();
        await Expect(empty).ToContainTextAsync("No scenarios match the filters.");

        // Clear All, and the focus goes to the search, which is the search button since the search left the box (4.2)
        await Page.Locator(".filters-empty-clear").ClickAsync();
        await Expect(empty).ToBeHiddenAsync();
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
        await Expect(Page.Locator("#search-button")).ToBeFocusedAsync();
    }

    // A page with nothing beside its features (here a run without durations, as the Specifications report is where no
    // scenario recorded one) had no report bar, and its empty state answered the filters only through the bar, so it never
    // showed (S14, found by the review of the wiki's 5.0.0 pages); since plans/SEARCH_PLAN.md F7 it has the bar too
    [Fact]
    public async Task On_a_page_of_features_alone_no_scenario_left_still_shows_the_empty_state()
    {
        await Page.SetViewportSizeAsync(1400, 700);
        await Page.GotoReportAsync(Generate("FilterBar_Empty_NoBar.html", durations: false));
        await Page.Locator("details.feature").First.WaitForAsync();
        // since plans/SEARCH_PLAN.md F7 such a page has the bar too, with its Features tab and Filters
        await Expect(Page.Locator(".toolbar-row.report-nav")).ToHaveCountAsync(1);
        var empty = Page.Locator(".filters-empty");
        await Expect(empty).ToBeHiddenAsync();

        await FillSearchBar("nothing-matches-this");
        await Expect(empty).ToBeVisibleAsync();
        await Expect(empty).ToContainTextAsync("No scenarios match the filters.");

        await Page.Locator(".filters-empty-clear").ClickAsync();
        await Expect(empty).ToBeHiddenAsync();
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
        await Expect(Page.Locator("#search-button")).ToBeFocusedAsync();
    }

    // the report reads the filters from the link before the filter bar's script starts, so a link that leaves no scenario
    // has already answered when the empty state is wired: the page opens on it, with the bar and without it
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_link_whose_filters_leave_no_scenario_opens_on_the_empty_state(bool durations)
    {
        await Page.SetViewportSizeAsync(1400, 700);
        await Page.GotoReportAsync(Generate(durations ? "FilterBar_Empty_Link.html" : "FilterBar_Empty_Link_NoBar.html", durations: durations) + "#q=nothing-matches-this");
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("nothing-matches-this");
        await Expect(Page.Locator(".toolbar-row.report-nav")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".filters-empty")).ToBeVisibleAsync();
        await Expect(Page.Locator(".filter-readout")).ToContainTextAsync("0 of 30 scenarios");
    }

    [Fact]
    public async Task The_bar_says_it_is_pinned_only_while_it_is()
    {
        await OpenAsync("FilterBar_Pinned.html");
        var nav = Page.Locator(".toolbar-row.report-nav");
        await Expect(nav).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-pinned\\b"));
        await ScrollPastTheBar();
        await Expect(nav).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-pinned\\b"));
        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await Expect(nav).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-pinned\\b"));
    }

    [Fact]
    public async Task Duration_and_Happy_paths_join_the_Status_line_only_when_they_fit_whole()
    {
        await OpenAsync("FilterBar_Pairs.html");
        await Toggle.ClickAsync();
        var row = Page.Locator(".filter-row");
        await Expect(row).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bpair-fits\\b"));
        await Expect(row).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\btrio-fits\\b"));
        Assert.True(await Page.EvaluateAsync<bool>("() => { const s = document.querySelector('.status-filters').getBoundingClientRect(), d = document.querySelector('.duration-filters').getBoundingClientRect(); return Math.abs(s.top - d.top) < 2; }"));

        // a box too narrow for the pair: each on a line of its own
        await Page.EvaluateAsync("() => { document.getElementById('filtering-box').style.maxWidth = '700px'; window.dispatchEvent(new Event('resize')); }");
        await Expect(row).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bpair-fits\\b"));
        await Expect(row).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\btrio-fits\\b"));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector('.duration-filters').getBoundingClientRect().top > document.querySelector('.status-filters').getBoundingClientRect().bottom - 1"));
    }

    // Firefox takes a flex item's max-content contribution from its width, not its flex basis: the Status group was sized
    // from its label's text (about 42 px) while the label was laid out at 92 px, so its buttons ran 50 px past the group
    // and under Duration where the pair shares the line (the preview's round 6; found missing by S14's parity run).
    // Playwright's own Firefox, skipped where it is not installed (CI installs Chromium only; StylesheetRulesTests holds
    // the rule there).
    [Fact]
    public async Task In_Firefox_the_Status_group_holds_its_buttons_and_Duration_starts_after_them()
    {
        using var playwright = await Playwright.CreateAsync();
        IBrowser? firefox = null;
        try { firefox = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }); }
        catch (PlaywrightException) { firefox = null; }
        Assert.SkipWhen(firefox is null, "Firefox is not installed (CI installs chromium only)");
        await using var _ = firefox!;
        var page = await OpenPageAsync(firefox!, new BrowserNewPageOptions { ViewportSize = new() { Width = 1400, Height = 700 } });
        await page.GotoReportAsync(Generate("FilterBar_Firefox_Status.html"));
        await page.Locator("details.feature").First.WaitForAsync();
        await page.Locator(".nav-filters-toggle").ClickAsync();
        await Expect(page.Locator(".filter-row")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bpair-fits\\b"));
        await TwoAnimationFramesAsync(page);

        var m = await page.EvaluateAsync<double[]>("""
            () => {
                const group = document.querySelector('.filter-row > .status-filters').getBoundingClientRect();
                const ends = Array.from(document.querySelectorAll('.filter-row > .status-filters button')).map(b => b.getBoundingClientRect().right);
                const duration = document.querySelector('.filter-row > .duration-filters').getBoundingClientRect();
                return [group.right, Math.max(...ends), duration.left];
            }
            """);

        Assert.True(m[1] <= m[0] + 0.5, $"the Status buttons end at {m[1]}, past the group's edge at {m[0]}");
        Assert.True(m[2] >= m[1], $"Duration starts at {m[2]}, under the Status buttons, which end at {m[1]}");
    }

    // the row's width as the tabs' own (measured without their stretch) plus the filter group (when slack is not -1) plus
    // the slack; with -1, the tabs and 26 px, room for the tabs alone
    private const string Narrow = """
        slack => {
            const row = document.querySelector('.nav-row'), tabs = row.querySelector('.report-tabs'), group = row.querySelector('.nav-filters');
            tabs.style.flex = 'none';
            const content = tabs.getBoundingClientRect().width;
            tabs.style.flex = '';
            row.style.width = (slack === -1 ? content + 14 : content + group.offsetWidth + slack) + 'px';
            window.dispatchEvent(new Event('resize'));
        }
        """;

    [Fact]
    public async Task A_narrow_bar_shortens_its_readout_then_drops_it()
    {
        await OpenAsync("FilterBar_Narrow.html");
        var row = Page.Locator(".nav-row");
        await Expect(row).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bnav-compact\\b"));

        // the row 30 px short of the tabs and the whole readout, then only 2 px more than the tabs and the slack
        await Page.EvaluateAsync(Narrow, 24 - 30);
        await Expect(row).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bnav-compact\\b"));
        await Expect(Page.Locator(".nav-readout-short")).ToBeVisibleAsync();
        await Expect(Page.Locator(".nav-readout-long")).ToBeHiddenAsync();

        await Page.EvaluateAsync(Narrow, -1);
        await Expect(row).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bnav-minimal\\b"));
        await Expect(Page.Locator(".nav-readout")).ToBeHiddenAsync();
    }

    // 4.5.7: the chip that sat inside the search field went with the field; deep search says where it is on the Search
    // tab, whose bar runs only while it reads, and the page keeps no chip
    [Fact]
    public async Task Deep_search_says_where_it_is_on_the_Search_tab_and_no_chip_is_left()
    {
        await OpenAsync("FilterBar_Chip.html");
        await FillSearchBar("zebra", toSearchTab: true);
        await Page.WaitForFunctionAsync("() => window.__kronikolSearch.deep === 'done'", null, new() { PollingInterval = 200 });
        await Expect(Page.Locator(".kron-deep-chip")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#panel-search .search-progress")).ToBeHiddenAsync();
    }
}
