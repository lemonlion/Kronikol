using System.Text.RegularExpressions;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A search and a filter are two things (the owner, 2026-10-27): a search gives a page of results, and a result picked
/// goes to it; a filter narrows the Features list. 5.11.0 ran the search through the filter (its query was the filter
/// field's), so a search left the Features list filtered, its sidebar was the filter box's buttons, and the filter box's
/// text field was gone. These facts hold them apart: the search never shows or hides a scenario on the Features tab, the
/// filter field never changes the search, the search reads every scenario whatever the filters hide, the Search tab's
/// sidebar narrows the search by writing qualifiers into its query, the filters are out of sight while the Search tab
/// shows, the address keeps the two under keys of their own, and the Search tab's exports hold its results.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class SearchApartTests : PlaywrightTestBase
{
    private readonly PlaywrightFixture _fixture;

    public SearchApartTests(PlaywrightFixture fixture) : base(fixture) => _fixture = fixture;

    private const string PaymentSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Payment Service\" as pay\ncaller -> pay : POST /pay\n@enduml";
    private const string StoreSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Clickhouse\" as ch\ncaller -> ch : INSERT\n@enduml";
    private const string ArchiveSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Payment Service Archive\" as arc\ncaller -> arc : GET /old\n@enduml";

    private const string PaymentAndStoreSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Payment Service\" as pay\nparticipant \"Clickhouse\" as ch\ncaller -> pay : POST /pay\npay -> ch : INSERT\n@enduml";

    // thirty scenarios in three features: every third tagged smoke, Scenario 2.5 failed; the even ones call Payment
    // Service, 3 and 7 call Clickhouse. With more, 4 and 8 are tagged api too, and 9 calls Payment Service Archive, a
    // name with Payment Service inside it. With both, 6 and 10 call Clickhouse as well as Payment Service
    private string Generate(string fileName, bool more = false, bool both = false)
    {
        var diagrams = new List<DefaultDiagramsFetcher.DiagramAsCode>();
        var features = Enumerable.Range(1, 3).Select(f => new Feature
        {
            DisplayName = $"Feature {f}",
            Scenarios = Enumerable.Range(1, 10).Select(i =>
            {
                var id = $"f{f}s{i}";
                if (both && i is 6 or 10) diagrams.Add(new DefaultDiagramsFetcher.DiagramAsCode(id, "", PaymentAndStoreSource));
                else if (i % 2 == 0) diagrams.Add(new DefaultDiagramsFetcher.DiagramAsCode(id, "", PaymentSource));
                else if (i is 3 or 7) diagrams.Add(new DefaultDiagramsFetcher.DiagramAsCode(id, "", StoreSource));
                else if (more && i == 9) diagrams.Add(new DefaultDiagramsFetcher.DiagramAsCode(id, "", ArchiveSource));
                return new Scenario
                {
                    Id = id, DisplayName = $"Scenario {f}.{i}", Result = i == 5 && f == 2 ? ExecutionResult.Failed : ExecutionResult.Passed,
                    Duration = TimeSpan.FromMilliseconds(10 * i + f), Tags = Tagged(i, more),
                    ErrorMessage = i == 5 && f == 2 ? "boom" : null
                };
            }).ToArray()
        }).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(diagrams.ToArray(), features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private static string[]? Tagged(int i, bool more)
    {
        var tags = new List<string>();
        if (i % 3 == 0) tags.Add("smoke");
        if (more && i is 4 or 8) tags.Add("api");
        return tags.Count > 0 ? [.. tags] : null;
    }

    private async Task OpenAsync(string fileName, string hash = "", bool more = false, bool both = false)
    {
        await Page.SetViewportSizeAsync(1400, 800);
        await Page.GotoReportAsync(Generate(fileName, more, both) + hash);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await TwoAnimationFramesAsync(Page);
    }

    private ILocator Status => Page.Locator(".report-nav .nav-status[role='status']");
    private ILocator Field => Page.Locator("#filtering-box #searchbar");
    private ILocator SearchTab => Page.Locator("#tab-search");
    private ILocator FeaturesTab => Page.Locator(".report-tabs > .report-tab[data-tab='features']");

    private Task<bool> EveryScenarioShows() =>
        Page.EvaluateAsync<bool>("() => fc().items.every(i => i.el.style.display !== 'none') && Array.from(document.querySelectorAll('details.feature')).every(f => f.style.display !== 'none')");

    private Task<bool> Shows(string selector) => Page.EvaluateAsync<bool>(
        "s => { const e = document.querySelector(s); return !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden'; }", selector);

    private async Task FilterFailedAsync()
    {
        await OpenFilterRowsAsync();
        await Page.Locator("#filtering-box .status-toggle[data-status='Failed']").ClickAsync();
        await Expect(Status).ToHaveTextAsync(new Regex("^Filtered 1 of 30 scenarios"));
    }

    private ILocator Facet(string name) => Page.Locator(".search-sidebar .search-side-link", new() { HasText = name }).First;
    // the row whose name is exactly this one
    private ILocator Row(string name) => Page.Locator(".search-sidebar .search-side-link")
        .Filter(new() { Has = Page.Locator(".search-side-name", new() { HasTextRegex = new Regex("^" + Regex.Escape(name) + "$") }) });
    private ILocator Query => Page.Locator(".search-button-query");
    // the marks a way to a match draws: the words, highlighted (CSS.highlights), and the place outlined
    private const string Marks = "() => { const h = window.CSS && CSS.highlights && CSS.highlights.get('kron-mark'); return (h ? h.size : 0) + document.querySelectorAll('#report-content .kron-mark-place').length; }";
    private ILocator Count => SearchTab.Locator(".tab-n");

    [Fact]
    public async Task A_search_leaves_the_Features_list_whole()
    {
        await OpenAsync("Apart_Whole.html");

        await FillSearchBar("tag:smoke", toSearchTab: true);
        await Expect(SearchTab).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(SearchTab.Locator(".tab-n")).ToHaveTextAsync("9");

        Assert.True(await EveryScenarioShows());
        await FeaturesTab.ClickAsync();
        Assert.True(await EveryScenarioShows());
        await Expect(Status).ToHaveTextAsync("All 30 scenarios, 3 features");
        await Expect(Page.Locator(".nav-filters-toggle")).ToHaveAttributeAsync("aria-label", "Filters");
        Assert.Equal("", await Page.EvaluateAsync<string>("() => document.getElementById('searchbar').value"));
    }

    [Fact]
    public async Task A_result_picked_goes_to_its_scenario_with_the_Features_list_whole()
    {
        await OpenAsync("Apart_Pick.html");
        await FillSearchBar("Scenario 2.5", toSearchTab: true);

        await Page.Locator("#panel-search .search-result-title", new() { HasText = "Scenario 2.5" }).First.ClickAsync();

        await Expect(FeaturesTab).ToHaveAttributeAsync("aria-selected", "true");
        await Page.WaitForFunctionAsync("""
            () => { const s = document.querySelector("details.scenario .copy-scenario-name[data-scenario-name='Scenario 2.5']").closest('details.scenario');
                    const r = s.getBoundingClientRect(); return s.open && r.top >= 0 && r.top < window.innerHeight; }
            """, null, new() { PollingInterval = 200 });
        Assert.True(await EveryScenarioShows());
        await Expect(Status).ToHaveTextAsync("All 30 scenarios, 3 features");
    }

    [Fact]
    public async Task The_filter_field_leaves_the_search_alone()
    {
        await OpenAsync("Apart_Field.html");
        await OpenFilterRowsAsync();

        await Field.FillAsync("@smoke");

        await Expect(Status).ToHaveTextAsync(new Regex("^Filtered 9 of 30 scenarios"));
        await Expect(Page.Locator(".search-button-hint")).ToBeVisibleAsync();
        await Expect(Page.Locator(".search-button-query")).ToBeHiddenAsync();
        await Expect(SearchTab).ToBeHiddenAsync();
    }

    [Fact]
    public async Task The_search_reads_every_scenario_whatever_the_filters_hide()
    {
        await OpenAsync("Apart_Every.html");
        await FilterFailedAsync();

        await FillSearchBar("Scenario", toSearchTab: true);

        await Expect(SearchTab.Locator(".tab-n")).ToHaveTextAsync("30");
        await Expect(Page.Locator("#panel-search .search-filtered-note")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task A_result_the_filters_hide_lands_on_their_note()
    {
        await OpenAsync("Apart_HiddenNote.html");
        await FilterFailedAsync();
        await FillSearchBar("Scenario 1.4", toSearchTab: true);

        await Page.Locator("#panel-search .search-result-title", new() { HasText = "Scenario 1.4" }).First.ClickAsync();

        await Expect(FeaturesTab).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.Locator(".filtered-out-note").First).ToBeVisibleAsync();
        await Expect(Status).ToHaveTextAsync(new Regex("^Filtered 1 of 30 scenarios"));
    }

    [Fact]
    public async Task The_filters_are_out_of_sight_while_the_Search_tab_shows()
    {
        await OpenAsync("Apart_Hidden.html");
        await FilterFailedAsync();
        // the bar's own Clear All shows while the box is closed and a filter is on
        await Page.Locator(".nav-filters-toggle").ClickAsync();
        Assert.True(await Shows(".nav-clear"));

        await FillSearchBar("Scenario", toSearchTab: true);

        await Expect(SearchTab).ToHaveAttributeAsync("aria-selected", "true");
        foreach (var part in new[] { ".nav-filters-toggle", ".nav-readout", ".nav-clear" })
            Assert.False(await Shows(part), part);

        // back on the Features tab, as they were
        await FeaturesTab.ClickAsync();
        foreach (var part in new[] { ".nav-filters-toggle", ".nav-readout", ".nav-clear" })
            Assert.True(await Shows(part), part);

        // an open box is out of sight too, and open again on the Features tab
        await Page.Locator(".nav-filters-toggle").ClickAsync();
        Assert.True(await Shows("#filtering-box"));
        await SearchTab.ClickAsync();
        await Expect(SearchTab).ToHaveAttributeAsync("aria-selected", "true");
        Assert.False(await Shows("#filtering-box"));
        await FeaturesTab.ClickAsync();
        Assert.True(await Shows("#filtering-box"));
    }

    [Theory]
    [InlineData("Payment Service", "dep:\"Payment Service\"", "15")]
    [InlineData("Clickhouse", "dep:Clickhouse", "6")]
    [InlineData("smoke", "tag:smoke", "9")]
    [InlineData("Failed", "status:failed", "1")]
    public async Task A_sidebar_row_narrows_the_search_and_a_second_click_takes_it_out(string row, string written, string count)
    {
        await OpenAsync($"Apart_Facet_{row.Replace(' ', '_')}.html");
        await FillSearchBar("Scenario", toSearchTab: true);
        await Expect(SearchTab.Locator(".tab-n")).ToHaveTextAsync("30");

        // on, off, on, off: a row never sticks
        for (var round = 0; round < 2; round++)
        {
            await Facet(row).ClickAsync();
            await Expect(Page.Locator(".search-button-query")).ToHaveTextAsync("Scenario " + written);
            await Expect(SearchTab.Locator(".tab-n")).ToHaveTextAsync(count);
            await Expect(Facet(row)).ToHaveAttributeAsync("aria-pressed", "true");

            await Facet(row).ClickAsync();
            await Expect(Page.Locator(".search-button-query")).ToHaveTextAsync("Scenario");
            await Expect(SearchTab.Locator(".tab-n")).ToHaveTextAsync("30");
            await Expect(Facet(row)).ToHaveAttributeAsync("aria-pressed", "false");
        }

        // the filters untouched
        Assert.Equal(0, await Page.Locator("#filtering-box .status-active, #filtering-box [aria-pressed='true']").CountAsync());
        Assert.True(await EveryScenarioShows());
    }

    // 5.12.0 (review): a row narrows the whole search whatever operators the query holds, and a second click puts the
    // query back. Until then a row was written after the query as text: status:passed after tag:smoke || status:failed
    // took the status:failed out of its or, and the rest read as smoke or passed (29); after a legacy @smoke or @api it
    // made the query advanced, where or is a word, and nothing matched
    [Theory]
    [InlineData("tag:smoke || status:failed", "Passed", "(tag:smoke || status:failed) status:passed", "10", "9", "tag:smoke || status:failed")]
    [InlineData("@smoke or @api", "Passed", "(@smoke || @api) status:passed", "15", "15", "@smoke || @api")]
    public async Task A_sidebar_row_narrows_a_query_with_operators_and_a_second_click_puts_it_back(string query, string row, string narrowed, string before, string after, string back)
    {
        await OpenAsync($"Apart_Operators_{before}_{after}.html", more: true);
        await FillSearchBar(query, toSearchTab: true);
        await Expect(Count).ToHaveTextAsync(before);

        for (var round = 0; round < 2; round++)
        {
            await Row(row).ClickAsync();
            await Expect(Query).ToHaveTextAsync(narrowed);
            await Expect(Count).ToHaveTextAsync(after);
            await Expect(Row(row)).ToHaveAttributeAsync("aria-pressed", "true");

            await Row(row).ClickAsync();
            await Expect(Query).ToHaveTextAsync(back);
            await Expect(Count).ToHaveTextAsync(before);
            await Expect(Row(row)).ToHaveAttributeAsync("aria-pressed", "false");
        }
    }

    // 5.12.0 (review): a row the query names beside an operator, or leaves out with !!, comes out whole. Until then the
    // operator stayed (scenario && left scenario &&, which the search could not read, so nothing matched), and a !!
    // before a tag read as the row on, whose click left scenario !!
    [Theory]
    [InlineData("scenario && tag:smoke", "smoke", "9")]
    [InlineData("scenario !!tag:smoke", "smoke", "21")]
    [InlineData("scenario && dep:Clickhouse", "Clickhouse", "6")]
    public async Task A_row_the_query_names_beside_an_operator_comes_out_whole(string query, string row, string before)
    {
        await OpenAsync($"Apart_Whole_{row}_{before}.html", more: true);
        await FillSearchBar(query, toSearchTab: true);
        await Expect(Count).ToHaveTextAsync(before);
        await Expect(Row(row)).ToHaveAttributeAsync("aria-pressed", "true");

        await Row(row).ClickAsync();

        await Expect(Query).ToHaveTextAsync("scenario");
        await Expect(Count).ToHaveTextAsync("30");
        await Expect(Row(row)).ToHaveAttributeAsync("aria-pressed", "false");
    }

    // 5.12.0 (review): a tag the query leaves out with !! or a legacy not reads as left out, as one after a - does: its
    // row offers to stop leaving it out, with no Exclude (until then only a - read so, and a row after !! or not read as
    // the search narrowed to the tag)
    [Theory]
    [InlineData("scenario -tag:smoke")]
    [InlineData("scenario !!tag:smoke")]
    [InlineData("not @smoke")]
    public async Task A_tag_left_out_with_a_minus_a_double_bang_or_a_not_reads_as_left_out(string query)
    {
        await OpenAsync($"Apart_LeftOut_{query.Length}.html");
        await FillSearchBar(query, toSearchTab: true);
        await Expect(Count).ToHaveTextAsync("21");

        await Expect(Row("smoke")).ToHaveAttributeAsync("title", "Stop leaving these out of the search");
        await Expect(SideRow("smoke")).ToHaveCountAsync(1);
        await Expect(SideRow("smoke").Locator(".search-side-exclude")).ToHaveCountAsync(0);
    }

    // 5.12.0 (review): a status row is one result at a time, and a history verdict is no result: Passed put in beside
    // status:flaky keeps it (until then every status the query named went as another went in, the verdict with them)
    [Fact]
    public async Task A_status_row_keeps_a_history_verdict_the_search_names()
    {
        await Page.SetViewportSizeAsync(1400, 800);
        await Page.GotoReportAsync(HistoryReportHelper.Generate(TempDir, OutputDir, "Apart_Verdict.html"));
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await FillSearchBar("status:flaky", toSearchTab: true);
        await Expect(Count).ToHaveTextAsync("1");

        await Row("Passed").ClickAsync();

        await Expect(Query).ToHaveTextAsync("status:flaky status:passed");
        await Expect(Count).ToHaveTextAsync("1");
    }

    // 5.12.0 (review): dep: matches a name inside a name, so Payment Service's click keeps the Payment Service Archive
    // callers too; its row counts what the click keeps (until then it counted its own name, 15, and its click kept 18)
    [Fact]
    public async Task A_dependency_row_counts_the_results_its_click_keeps()
    {
        await OpenAsync("Apart_DepCount.html", more: true);
        await FillSearchBar("Scenario", toSearchTab: true);

        await Expect(Row("Payment Service").Locator(".search-side-count")).ToHaveTextAsync("18");
        await Row("Payment Service").ClickAsync();
        await Expect(Count).ToHaveTextAsync("18");
    }

    // the sidebar's row, its Exclude with it, whose own control is Row(name). The name is found inside the row: an inner
    // locator is read from the row down, so Row(name), which starts at .search-sidebar, matches nothing in it
    private ILocator SideRow(string name) => Page.Locator(".search-sidebar .search-side-row")
        .Filter(new() { Has = Page.Locator(".search-side-name", new() { HasTextRegex = new Regex("^" + Regex.Escape(name) + "$") }) });

    // a reader's click where a row's count is drawn: the pointer moved there, then pressed, as Playwright's click on the
    // row does not, aiming at its middle. The deep search has answered first, as it draws the sidebar again when it does
    private async Task ClickCountAsync(string name)
    {
        await SidebarSettledAsync();
        var box = await Row(name).Locator(".search-side-count").BoundingBoxAsync();
        Assert.NotNull(box);
        await Page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    private async Task SidebarSettledAsync()
    {
        await Page.WaitForFunctionAsync("() => !window.__kronikolSearch || window.__kronikolSearch.deep !== 'working'", null, new() { PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    // 5.12.1: a row is one control wherever it is clicked. Until then its Exclude stood over the row's count, unseen until
    // the row was hovered and clickable all the while, so a click on the count left the scenarios out where it meant to
    // narrow the search to them
    [Theory]
    [InlineData("Clickhouse", "dep:Clickhouse", "6")]
    [InlineData("smoke", "tag:smoke", "9")]
    public async Task A_click_on_a_row_s_count_narrows_the_search_and_a_second_takes_it_out(string row, string written, string count)
    {
        await OpenAsync($"Apart_CountClick_{row}.html");
        await FillSearchBar("Scenario", toSearchTab: true);
        await Expect(Count).ToHaveTextAsync("30");

        for (var round = 0; round < 2; round++)
        {
            await ClickCountAsync(row);
            await Expect(Query).ToHaveTextAsync("Scenario " + written);
            await Expect(Count).ToHaveTextAsync(count);

            await ClickCountAsync(row);
            await Expect(Query).ToHaveTextAsync("Scenario");
            await Expect(Count).ToHaveTextAsync("30");
        }
    }

    // 5.12.1: a row that is on has no Exclude, so a click anywhere on it takes it out. Until then its Exclude turned it into
    // a row leaving them out: here, narrowed to two dependencies and one clicked off on its count, the search left out the
    // very scenarios it had narrowed to, and nothing was left (the owner, 2026-10-27)
    [Fact]
    public async Task A_row_that_is_on_has_no_Exclude_and_a_click_on_its_count_takes_it_out()
    {
        await OpenAsync("Apart_OnRow.html", both: true);
        await FillSearchBar("Scenario", toSearchTab: true);
        await Row("Payment Service").ClickAsync();
        await Row("Clickhouse").ClickAsync();
        await Expect(Query).ToHaveTextAsync("Scenario dep:\"Payment Service\" dep:Clickhouse");
        await Expect(Count).ToHaveTextAsync("6");

        await Row("Payment Service").HoverAsync();
        await Expect(SideRow("Payment Service")).ToHaveCountAsync(1);
        await Expect(SideRow("Payment Service").Locator(".search-side-exclude")).ToHaveCountAsync(0);
        await ClickCountAsync("Payment Service");

        await Expect(Query).ToHaveTextAsync("Scenario dep:Clickhouse");
        await Expect(Count).ToHaveTextAsync("12");
    }

    // 5.12.1: Exclude stands beside its row's control, where nothing of the row is drawn, and takes no click until its
    // row is hovered (until then it stood over the count, at opacity 0, and took every click there)
    [Fact]
    public async Task Exclude_stands_beside_its_row_and_takes_no_click_until_the_row_is_hovered()
    {
        await OpenAsync("Apart_ExcludePlace.html");
        await FillSearchBar("Scenario", toSearchTab: true);
        await SidebarSettledAsync();
        await Page.Mouse.MoveAsync(0, 0);
        var exclude = SideRow("Clickhouse").Locator(".search-side-exclude");
        await Expect(exclude).ToHaveCountAsync(1);

        Assert.False(await exclude.EvaluateAsync<bool>("x => { const r = x.getBoundingClientRect(); return document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2) === x; }"));

        await Row("Clickhouse").HoverAsync();
        var link = await Row("Clickhouse").BoundingBoxAsync();
        var box = await exclude.BoundingBoxAsync();
        Assert.NotNull(link);
        Assert.NotNull(box);
        Assert.True(box.X >= link.X + link.Width, $"Exclude at {box.X} stands over the row, which ends at {link.X + link.Width}");
        Assert.True(await exclude.EvaluateAsync<bool>("x => { const r = x.getBoundingClientRect(); return document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2) === x; }"));
    }

    // 5.12.1: where nothing hovers, a touch screen, a row that is off shows its Exclude, and a tap on it leaves the
    // scenarios out: shown only on hover, it could never be reached there
    [Fact]
    public async Task On_a_touch_screen_a_row_s_Exclude_shows_and_a_tap_takes_it()
    {
        await using var touch = await OpenContextAsync(_fixture.Browser, new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1024, Height = 800 },
            HasTouch = true
        });
        var page = await OpenPageAsync(touch);
        await page.GotoReportAsync(Generate("Apart_TouchExclude.html"));
        await page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        Assert.True(await page.EvaluateAsync<bool>("() => matchMedia('(hover: none)').matches"),
            "the context hovers; without (hover: none) this fact proves nothing");
        await FillSearchBar(page, "Scenario", toSearchTab: true);
        await page.WaitForFunctionAsync("() => !window.__kronikolSearch || window.__kronikolSearch.deep !== 'working'", null, new() { PollingInterval = 200 });
        var exclude = page.Locator(".search-sidebar .search-side-row")
            .Filter(new() { Has = page.Locator(".search-side-name", new() { HasTextRegex = new Regex("^Clickhouse$") }) })
            .Locator(".search-side-exclude");

        await Expect(exclude).ToHaveCSSAsync("opacity", "1");
        await exclude.TapAsync();

        await Expect(page.Locator(".search-button-query")).ToHaveTextAsync("Scenario -dep:Clickhouse");
    }

    // 5.12.0 (review): a result the filters hide shows their note and none of the way to its match, which waits for the
    // note's button. Until then the way went on beside the note: here it marked the name of the feature, which the
    // filters leave showing for its smoke scenarios, took the focus to it and said so
    [Fact]
    public async Task The_way_to_a_match_the_filters_hide_waits_for_the_note_s_button()
    {
        await OpenAsync("Apart_HiddenWay.html");
        await FillSearchBar("@smoke");
        await Expect(Status).ToHaveTextAsync(new Regex("^Filtered 9 of 30 scenarios"));
        await FillSearchBar("Feature", toSearchTab: true);
        const string said = "() => (document.querySelector('.kron-nav-status') || {}).textContent || ''";

        await Page.Locator("#panel-search .search-result-title", new() { HasText = "Scenario 1.2" }).First.ClickAsync();

        var note = Page.Locator(".filtered-out-note").First;
        await Expect(note).ToBeVisibleAsync();
        await Expect(note.Locator("button")).ToBeFocusedAsync();
        await Page.WaitForTimeoutAsync(1000);
        Assert.Equal(0, await Page.EvaluateAsync<int>(Marks));
        await Expect(note.Locator("button")).ToBeFocusedAsync();
        Assert.DoesNotMatch("^Match in", await Page.EvaluateAsync<string>(said));

        // the button shows the scenario, and the way goes on to the match
        await note.Locator("button").ClickAsync();
        await Expect(Status).Not.ToHaveTextAsync(new Regex("^Filtered"));
        await Page.WaitForFunctionAsync(said + ".startsWith('Match in the feature')", null, new() { PollingInterval = 200 });
        await Page.WaitForFunctionAsync("() => (" + Marks + ")() > 0", null, new() { PollingInterval = 200 });
    }

    // 5.12.0 (review): a 5.11.0 link to a match the Search tab listed carried its search as q=, as that tab's own link
    // did: it opens as that search, the words marked where it lands, the filter field empty (until then its q= went to
    // the filter field, and nothing was marked)
    [Fact]
    public async Task A_5_11_0_link_to_a_match_opens_as_its_search()
    {
        await OpenAsync("Apart_OldHit.html");
        await FillSearchBar("Scenario 1.4", toSearchTab: true);
        await Page.Locator("#panel-search .search-result-title", new() { HasText = "Scenario 1.4" }).First.ClickAsync();
        await Expect(FeaturesTab).ToHaveAttributeAsync("aria-selected", "true");
        var hash = await Page.EvaluateAsync<string>("() => location.hash");
        Assert.Contains("hit=", hash);
        Assert.Contains("search=", hash);
        var url = Page.Url.Split('#')[0];

        await Page.GotoAsync("about:blank");
        await Page.GotoReportAsync(url + hash.Replace("search=", "q="));

        await Page.WaitForFunctionAsync("() => typeof search_query === 'function' && search_query() === 'Scenario 1.4'", null, new() { PollingInterval = 200 });
        await Expect(Field).ToHaveValueAsync("");
        await Page.WaitForFunctionAsync("() => (" + Marks + ")() > 0", null, new() { PollingInterval = 200 });
    }

    // 5.12.0 (review): the list under an open filter box keeps a window's height, so a short result cannot pull a pinned
    // bar back down; on the Search tab the box is out of sight, and a short list of results has no blank window under it
    [Fact]
    public async Task The_Search_tab_has_no_blank_window_under_its_results()
    {
        await OpenAsync("Apart_NoBlank.html");
        await OpenFilterRowsAsync();
        var minHeight = "() => getComputedStyle(document.querySelector('.report-panels')).minHeight";
        Assert.NotEqual("0px", await Page.EvaluateAsync<string>(minHeight));

        await FillSearchBar("Scenario 1.4", toSearchTab: true);
        await Expect(SearchTab).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Equal("0px", await Page.EvaluateAsync<string>(minHeight));

        await FeaturesTab.ClickAsync();
        Assert.NotEqual("0px", await Page.EvaluateAsync<string>(minHeight));
    }

    [Fact]
    public async Task The_builder_has_no_row_that_filters()
    {
        await OpenAsync("Apart_Builder.html");
        await Page.EvaluateAsync("() => window.kronikol_search.open()");
        await Page.Locator("#search-input").FillAsync("Scenario");
        await Expect(Page.Locator("#search-suggestions .search-option").First).ToBeVisibleAsync();

        Assert.Equal(0, await Page.Locator("#search-suggestions .search-option-filter").CountAsync());
    }

    [Fact]
    public async Task The_address_keeps_the_search_and_the_filter_field_under_keys_of_their_own()
    {
        await OpenAsync("Apart_Address.html");
        await FillSearchBar("toast", toSearchTab: true);
        await Page.WaitForFunctionAsync("() => /(^#|&)search=toast(&|$)/.test(location.hash)", null, new() { PollingInterval = 200 });
        Assert.DoesNotMatch("(^#|&)q=", await Page.EvaluateAsync<string>("() => location.hash"));

        await FeaturesTab.ClickAsync();
        await OpenFilterRowsAsync();
        await Field.FillAsync("@smoke");
        await Page.WaitForFunctionAsync("() => /(^#|&)q=%40smoke(&|$)/.test(location.hash)", null, new() { PollingInterval = 200 });
    }

    [Theory]
    [InlineData("#tab=search&search=tag%3Asmoke", "tag:smoke")]
    [InlineData("#tab=search&q=tag%3Asmoke", "tag:smoke")] // a 5.11.0 link: its q= was the search
    public async Task A_link_with_a_search_opens_its_results_and_leaves_the_filter_field_empty(string hash, string query)
    {
        await OpenAsync($"Apart_Link{hash.Length}.html", hash);

        await Expect(SearchTab).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(SearchTab.Locator(".tab-n")).ToHaveTextAsync("9");
        await Expect(Page.Locator(".search-button-query")).ToHaveTextAsync(query);
        Assert.Equal("", await Page.EvaluateAsync<string>("() => document.getElementById('searchbar').value"));
        Assert.True(await EveryScenarioShows());
    }

    [Fact]
    public async Task A_link_with_q_filters_the_Features_list_and_runs_no_search()
    {
        await OpenAsync("Apart_LinkQ.html", "#q=%40smoke");

        await Expect(Status).ToHaveTextAsync(new Regex("^Filtered 9 of 30 scenarios"));
        Assert.Equal("@smoke", await Page.EvaluateAsync<string>("() => document.getElementById('searchbar').value"));
        await Expect(SearchTab).ToBeHiddenAsync();
    }

    [Fact]
    public async Task The_Search_tab_s_export_holds_its_results_whatever_the_filters_show()
    {
        await OpenAsync("Apart_Export.html");
        await FilterFailedAsync();
        await FillSearchBar("tag:smoke", toSearchTab: true);
        await Expect(SearchTab.Locator(".tab-n")).ToHaveTextAsync("9");

        await Page.Locator("#panel-search .search-kebab").ClickAsync();
        var download = await Page.RunAndWaitForDownloadAsync(() =>
            Page.Locator("#panel-search .search-menu-item", new() { HasText = "Export results as CSV" }).ClickAsync());
        var csv = await File.ReadAllTextAsync((await download.PathAsync())!);

        Assert.Contains("Scenario 1.3\"", csv);
        Assert.Contains("Scenario 3.9\"", csv);
        Assert.DoesNotContain("Scenario 2.5", csv);
        Assert.Equal(10, csv.Split('\n').Length);
        // the page as the filters left it
        await Expect(Status).ToHaveTextAsync(new Regex("^Filtered 1 of 30 scenarios"));
    }

    // 5.12.0: the magnifier before the Search tab's name sat on the text's baseline, 2.5 px above the name's middle, and
    // touched its first letter
    [Fact]
    public async Task The_Search_tab_s_magnifier_sits_level_with_its_name_and_apart_from_it()
    {
        await OpenAsync("Apart_TabIcon.html");
        await FillSearchBar("Scenario", toSearchTab: true);
        await Expect(SearchTab).ToBeVisibleAsync();

        var m = await SearchTab.EvaluateAsync<double[]>("""
            t => { const i = t.querySelector('.search-icon').getBoundingClientRect(), r = document.createRange();
                   r.selectNodeContents(Array.from(t.childNodes).find(n => n.nodeType === 3 && n.nodeValue.trim()));
                   const x = r.getBoundingClientRect();
                   return [(i.top + i.bottom) / 2 - (x.top + x.bottom) / 2, x.left - i.right]; }
            """);
        Assert.InRange(m[0], -1, 1);
        Assert.InRange(m[1], 4, 10);
    }
}
