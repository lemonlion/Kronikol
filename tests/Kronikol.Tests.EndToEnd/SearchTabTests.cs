using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The Search tab (plans/SEARCH_PLAN.md 4.5, facts R1 to R28): GitHub's results page in the report's look, shown while a
/// search is applied. The facts run on the bakery of <see cref="SearchReports"/>, whose matches are known: "pancake" is the
/// three Pancakes Creation scenarios, "toast" the twelve Toast slices, "flour" one scenario matched in its name, a step and
/// its diagram, "regression" one scenario in each of two features.
/// </summary>
public partial class SearchResultsTests
{
    private async Task OpenBakeryAsync(string fileName, int width = 1280, int height = 900, Feature[]? features = null, bool includeTestRunData = true, string hash = "")
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoReportAsync(SearchReports.Write(TempDir, OutputDir, fileName, features, includeTestRunData) + hash);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results", null, new() { PollingInterval = 200 });
    }

    // Enter in the builder: the search applied, the Search tab current
    private async Task SearchAsync(string query)
    {
        await FillSearchBar(query, toSearchTab: true);
        await ResultsShownAsync();
    }

    private async Task ResultsShownAsync()
    {
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden && document.getElementById('search-count').textContent !== ''",
            null, new() { PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    private Task<string> SelectedTabAsync() => Page.EvaluateAsync<string>(
        "() => document.querySelector('.report-tabs > .report-tab[aria-selected=\"true\"]').getAttribute('data-tab')");

    private Task<string> CountAsync() => Page.EvaluateAsync<string>("() => document.getElementById('search-count').textContent");

    private Task<string[]> TitlesAsync() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('#panel-search .search-result-title')).map(a => a.textContent)");

    private Task<string> HashAsync() => Page.EvaluateAsync<string>("() => decodeURIComponent(location.hash)");

    // a sidebar row by its group's heading and its name: its count, and whether it is the current one
    private Task<string?> SideCountAsync(string group, string name) => Page.EvaluateAsync<string?>(@"([g, n]) => {
        const h = Array.from(document.querySelectorAll('#panel-search .search-side-heading')).find(e => e.textContent === g);
        if (!h) return null;
        const row = Array.from(h.nextElementSibling.querySelectorAll('.search-side-row')).find(r => r.querySelector('.search-side-name').textContent === n);
        if (!row) return null;
        const c = row.querySelector('.search-side-pill, .search-side-count');
        return c ? c.firstChild.textContent : '';
    }", new[] { group, name });

    private Task<bool> SideCurrentAsync(string name) => Page.EvaluateAsync<bool>(
        "n => Array.from(document.querySelectorAll('#panel-search .search-side-row')).some(r => r.querySelector('.search-side-name').textContent === n && r.classList.contains('is-current'))", name);

    private ILocator SideLink(string name) =>
        Page.Locator($"#panel-search .search-side-row:has(> .search-side-link > .search-side-name:text-is('{name}')) > .search-side-link").First;

    private async Task ClickSideAsync(string name)
    {
        await SideLink(name).ClickAsync();
        await TwoAnimationFramesAsync(Page);
    }

    // R1
    [Fact]
    public async Task The_Search_tab_shows_while_a_query_is_applied_and_goes_with_it()
    {
        await OpenBakeryAsync("Tab_R1.html");
        Assert.True(await Page.Locator("#tab-search").IsHiddenAsync());

        await SearchAsync("pancake");
        Assert.True(await Page.Locator("#tab-search").IsVisibleAsync());
        Assert.Equal("search", await SelectedTabAsync());
        Assert.Equal("3", await Page.Locator("#tab-search .tab-n").TextContentAsync());

        await Page.Locator("#search-clear").ClickAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('tab-search').hidden", null, new() { PollingInterval = 200 });
        Assert.Equal("features", await SelectedTabAsync());
    }

    // R2
    [Fact]
    public async Task Clearing_on_the_Search_tab_returns_to_the_tab_the_search_was_run_from()
    {
        await OpenBakeryAsync("Tab_R2.html");
        var other = Page.Locator(".report-tabs > .report-tab:not([hidden]):not([data-tab='features']):not([data-tab='search'])").First;
        var from = await other.GetAttributeAsync("data-tab");
        await other.ClickAsync();

        await SearchAsync("waffle");
        Assert.Equal("search", await SelectedTabAsync());
        await Page.Locator("#search-clear").ClickAsync();

        await Page.WaitForFunctionAsync("t => document.querySelector('.report-tabs > .report-tab[aria-selected=\"true\"]').getAttribute('data-tab') === t",
            from, new() { PollingInterval = 200 });
        Assert.True(await Page.Locator("#tab-search").IsHiddenAsync());
    }

    // R3
    [Fact]
    public async Task Show_lists_Scenarios_and_Features_with_their_counts_zeros_included()
    {
        await OpenBakeryAsync("Tab_R3.html");
        await SearchAsync("toast");

        Assert.Equal("12", await SideCountAsync("Show", "Scenarios"));
        Assert.Equal("1", await SideCountAsync("Show", "Features"));
        Assert.Equal("0", await SideCountAsync("Matched in", "Errors"));
    }

    // R4: a result matched in its name, a step and its diagram counts in each
    [Fact]
    public async Task Matched_in_counts_where_each_result_matched()
    {
        await OpenBakeryAsync("Tab_R4.html");
        await SearchAsync("flour");

        Assert.Equal("1 result", await CountAsync());
        Assert.Equal("1", await SideCountAsync("Matched in", "Names"));
        Assert.Equal("1", await SideCountAsync("Matched in", "Steps"));
        Assert.Equal("1", await SideCountAsync("Matched in", "Diagrams"));
        Assert.Equal("0", await SideCountAsync("Matched in", "Errors"));
    }

    // R5
    [Fact]
    public async Task Features_lists_the_features_holding_a_result_and_writes_type()
    {
        await OpenBakeryAsync("Tab_R5a.html");
        await SearchAsync("regression");

        await ClickSideAsync("Features");

        Assert.Equal("2 features", await CountAsync());
        Assert.Equal(["Pancakes Creation", "Waffles"], await TitlesAsync());
        Assert.Contains("type=features", await HashAsync());
    }

    // R5
    [Fact]
    public async Task A_matched_in_place_keeps_its_results_and_writes_in()
    {
        await OpenBakeryAsync("Tab_R5b.html");
        await SearchAsync("flour || toast");
        Assert.Equal("13 results", await CountAsync());

        await ClickSideAsync("Steps");

        Assert.Equal("1 result", await CountAsync());
        Assert.Equal(["Pancakes without flour are rejected"], await TitlesAsync());
        Assert.Contains("in=steps", await HashAsync());
        Assert.True(await SideCurrentAsync("Steps"));
    }

    // R6
    [Fact]
    public async Task The_current_type_has_the_accent_bar_and_aria_current()
    {
        await OpenBakeryAsync("Tab_R6.html");
        await SearchAsync("toast");

        Assert.Equal("page", await SideLink("Scenarios").GetAttributeAsync("aria-current"));
        Assert.Null(await SideLink("Features").GetAttributeAsync("aria-current"));
        // the bar's colour, and the accent's, read off a probe painted with the token
        var colours = await Page.EvaluateAsync<string[]>(@"() => {
            const row = Array.from(document.querySelectorAll('#panel-search .search-side-row')).find(r => r.querySelector('.search-side-name').textContent === 'Scenarios');
            const probe = document.createElement('span');
            probe.style.color = 'var(--kron-accent)';
            document.body.appendChild(probe);
            const accent = getComputedStyle(probe).color;
            probe.remove();
            return [getComputedStyle(row, '::before').backgroundColor, accent];
        }");
        Assert.Equal(colours[1], colours[0]);
    }

    // R7, 5.12.0: a status row narrows the search, as GitHub's sidebar does, and leaves the box's Status as it was (the
    // owner: a search is no filter)
    [Fact]
    public async Task A_status_row_narrows_the_search_and_leaves_the_box_alone()
    {
        await OpenBakeryAsync("Tab_R7.html");
        await SearchAsync("pancake");
        Assert.Equal("2", await SideCountAsync("Status", "Passed"));
        Assert.Equal("1", await SideCountAsync("Status", "Failed"));

        await ClickSideAsync("Failed");
        Assert.Equal("1 result", await CountAsync());
        Assert.Equal("pancake status:failed", await Page.EvaluateAsync<string>("() => search_query()"));
        Assert.True(await SideCurrentAsync("Failed"));
        Assert.False(await Page.EvaluateAsync<bool>("() => document.querySelector('#filtering-box .status-toggle[data-status=\"Failed\"]').classList.contains('status-active')"));

        await ClickSideAsync("Failed");
        Assert.Equal("3 results", await CountAsync());
        Assert.Equal("pancake", await Page.EvaluateAsync<string>("() => search_query()"));
    }

    // R8, 5.12.0: Exclude writes -tag: into the search; the box's chip stays off
    [Fact]
    public async Task Exclude_leaves_them_out_of_the_search()
    {
        await OpenBakeryAsync("Tab_R8.html");
        await SearchAsync("pancake");

        await Page.Locator("#panel-search .search-side-row:has(.search-side-name:text-is('smoke'))").HoverAsync();
        await Page.Locator("#panel-search .search-side-exclude[aria-label='Exclude smoke']").ClickAsync();
        await TwoAnimationFramesAsync(Page);

        Assert.Equal("pancake -tag:smoke", await Page.EvaluateAsync<string>("() => search_query()"));
        Assert.Equal("off", await Page.EvaluateAsync<string>("() => chip_state(find_chip('tag', 'smoke'))"));
        Assert.Equal("1 result", await CountAsync());
        Assert.Equal(["Pancakes without flour are rejected"], await TitlesAsync());
    }

    // R9
    [Fact]
    public async Task More_tags_shows_the_rest_in_place()
    {
        Feature[] bread =
        [
            new Feature
            {
                DisplayName = "Bread",
                Scenarios = Enumerable.Range(1, 12).Select(i => new Scenario
                {
                    Id = $"b{i}", DisplayName = $"Bread loaf {i} rises", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10),
                    Tags = [$"oven{i:00}"],
                }).ToArray()
            }
        ];
        await OpenBakeryAsync("Tab_R9.html", features: bread);
        await SearchAsync("bread");

        var tagRows = "() => { const h = Array.from(document.querySelectorAll('#panel-search .search-side-heading')).find(e => e.textContent === 'Tags');"
                      + " return Array.from(h.nextElementSibling.querySelectorAll('.search-side-name')).map(n => n.textContent); }";
        var rows = await Page.EvaluateAsync<string[]>(tagRows);
        Assert.Equal(11, rows.Length);
        Assert.Equal("More tags…", rows[^1]);

        await ClickSideAsync("More tags…");

        rows = await Page.EvaluateAsync<string[]>(tagRows);
        Assert.Equal(13, rows.Length);
        Assert.Equal("Fewer", rows[^1]);
    }

    // R10
    [Fact]
    public async Task An_advanced_row_opens_the_builder_at_its_values()
    {
        await OpenBakeryAsync("Tab_R10.html");
        await SearchAsync("pancake");

        await ClickSideAsync("Tag");
        await Page.WaitForFunctionAsync("() => document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });

        Assert.Equal("pancake tag:", await Page.Locator("#search-input").InputValueAsync());
        var options = await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('#search-suggestions [role=option]')).map(o => o.getAttribute('aria-label'))");
        Assert.Contains(options, o => o.StartsWith("tag:smoke", StringComparison.Ordinal));
    }

    // R11
    [Fact]
    public async Task The_header_shows_the_count_and_the_time()
    {
        await OpenBakeryAsync("Tab_R11.html");
        await SearchAsync("toast");

        Assert.Equal("12 results", await CountAsync());
        Assert.Matches(@"^\(\d+(\.\d)? (ms|s)\)$", await Page.Locator("#panel-search .search-time").TextContentAsync());
    }

    // R12
    [Fact]
    public async Task Longest_first_orders_by_own_time_and_writes_s_and_o()
    {
        await OpenBakeryAsync("Tab_R12.html");
        await SearchAsync("toast");

        await Page.Locator("#panel-search .search-sort-select").SelectOptionAsync("duration:desc");
        await TwoAnimationFramesAsync(Page);

        var titles = await TitlesAsync();
        Assert.Equal("Toast slice 12 is browned", titles[0]);
        Assert.Equal("Toast slice 11 is browned", titles[1]);
        var hash = await HashAsync();
        Assert.Contains("s=duration", hash);
        Assert.Contains("o=desc", hash);
    }

    // R13
    [Fact]
    public async Task Best_match_puts_a_scenario_named_after_the_word_first()
    {
        Feature[] cafe =
        [
            new Feature
            {
                DisplayName = "Cafe",
                Scenarios =
                [
                    new Scenario { Id = "c1", DisplayName = "Cancel an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10),
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "the menu is shown", Status = ExecutionResult.Passed }] },
                    new Scenario { Id = "c2", DisplayName = "The menu lists the drinks", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) },
                ]
            }
        ];
        await OpenBakeryAsync("Tab_R13.html", features: cafe);
        await SearchAsync("menu");

        Assert.Equal(["The menu lists the drinks", "Cancel an order"], await TitlesAsync());

        await Page.Locator("#panel-search .search-sort-select").SelectOptionAsync("order");
        await TwoAnimationFramesAsync(Page);
        Assert.Equal(["Cancel an order", "The menu lists the drinks"], await TitlesAsync());
    }

    // R14
    [Fact]
    public async Task Ten_results_a_page_and_each_page_writes_p()
    {
        await OpenBakeryAsync("Tab_R14.html");
        await SearchAsync("toast");
        Assert.Equal(10, await Page.Locator("#panel-search .search-result").CountAsync());

        await Page.Locator("#panel-search .search-pages .search-page:text-is('2')").ClickAsync();
        await TwoAnimationFramesAsync(Page);

        Assert.Equal(2, await Page.Locator("#panel-search .search-result").CountAsync());
        Assert.Contains("p=2", await HashAsync());
        Assert.Equal("page", await Page.Locator("#panel-search .search-pages .search-page:text-is('2')").GetAttributeAsync("aria-current"));
    }

    // R16
    [Fact]
    public async Task A_result_shows_its_feature_its_time_its_tags_and_up_to_three_lines()
    {
        await OpenBakeryAsync("Tab_R16.html");
        await SearchAsync("flour");

        var card = Page.Locator("#panel-search .search-result").First;
        Assert.Equal("Pancakes Creation", await card.Locator(".search-result-feature").TextContentAsync());
        Assert.Equal("2.4 s", await card.Locator(".search-result-time").TextContentAsync());
        Assert.Equal(["regression"], await card.Locator(".search-result-tag").AllTextContentsAsync());
        Assert.Equal(["NAME", "STEP", "DIAGRAM"], await card.Locator(".search-line-kind").AllTextContentsAsync());
        Assert.Contains("an order without flour", (await card.Locator(".search-line-text").AllTextContentsAsync())[1]);
        // the matched word at 700 in a name or a step, in a mark in a diagram's line
        Assert.Equal("flour", await card.Locator(".search-result-title b").First.TextContentAsync());
        Assert.Equal("flour", await card.Locator(".search-line-text.is-mono mark.search-mark").First.TextContentAsync());
    }

    // R17
    [Fact]
    public async Task Show_more_lists_up_to_twenty_and_Show_less_folds_them()
    {
        Feature[] eggs =
        [
            new Feature
            {
                DisplayName = "Breakfast",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "e1", DisplayName = "Breakfast is cooked", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10),
                        Steps = new[] { "an egg is cracked", "a second egg is cracked", "the eggs are whisked", "the eggs are fried", "the egg is plated", "an egg is eaten" }
                            .Select(t => new ScenarioStep { Keyword = "Given", Text = t, Status = ExecutionResult.Passed }).ToArray(),
                    },
                ]
            }
        ];
        await OpenBakeryAsync("Tab_R17.html", features: eggs);
        await SearchAsync("egg");

        var card = Page.Locator("#panel-search .search-result").First;
        Assert.Equal(3, await card.Locator(".search-line").CountAsync());
        Assert.Equal("Show 3 more matches", await card.Locator(".search-lines-more").TextContentAsync());

        await card.Locator(".search-lines-more").ClickAsync();
        card = Page.Locator("#panel-search .search-result").First;
        Assert.Equal(6, await card.Locator(".search-line").CountAsync());
        Assert.Equal("Show less", await card.Locator(".search-lines-more").TextContentAsync());

        await card.Locator(".search-lines-more").ClickAsync();
        Assert.Equal(3, await Page.Locator("#panel-search .search-result").First.Locator(".search-line").CountAsync());
    }

    // R18
    [Fact]
    public async Task Nothing_found_names_the_type_and_the_types_that_did_match()
    {
        await OpenBakeryAsync("Tab_R18.html");
        await SearchAsync("flour");

        await ClickSideAsync("Errors");

        Assert.Equal("Your search did not match any errors", await Page.Locator("#panel-search .search-nothing-title").TextContentAsync());
        Assert.Equal("However, it matched 1 name, 1 step and 1 diagram.", await Page.Locator("#panel-search .search-nothing-other").TextContentAsync());

        await Page.Locator("#panel-search .search-nothing-other .search-nothing-link:text-is('1 step')").ClickAsync();
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("1 result", await CountAsync());
        Assert.Contains("in=steps", await HashAsync());
    }

    // R19
    [Fact]
    public async Task An_unreadable_qualifier_is_warned_about_and_the_search_still_runs()
    {
        await OpenBakeryAsync("Tab_R19.html");
        await SearchAsync("toast duration:abc");

        var warning = Page.Locator("#panel-search .search-warning");
        Assert.True(await warning.IsVisibleAsync());
        Assert.Equal("duration:abc was searched as text: a duration is a time such as 2s, a range such as 1s..5s, or a percentile such as p90.",
            await warning.TextContentAsync());
        Assert.Equal("0 results", await CountAsync());
    }

    // R19: a value quoted for its space is one qualifier, and is warned about as one
    [Fact]
    public async Task An_unreadable_qualifier_with_a_quoted_space_is_warned_about_whole()
    {
        await OpenBakeryAsync("Tab_R19_quoted.html");
        await SearchAsync("toast duration:\"two weeks\"");

        var lines = await Page.Locator("#panel-search .search-warning > div").AllTextContentsAsync();
        Assert.Equal(["duration:\"two weeks\" was searched as text: a duration is a time such as 2s, a range such as 1s..5s, or a percentile such as p90."],
            lines);
    }

    // R19: a qualifier inside a group that !! negates cannot narrow the search whatever its value: the warning says so,
    // not what its value should be
    [Fact]
    public async Task A_qualifier_in_a_negated_group_is_warned_about_for_its_place()
    {
        await OpenBakeryAsync("Tab_R19_negated.html");
        await SearchAsync("toast !!(dep:payment)");

        Assert.Equal("dep:payment was searched as text: a qualifier inside a group that !! or not negates cannot narrow the search.",
            await Page.Locator("#panel-search .search-warning").TextContentAsync());
    }

    // R20, 5.12.0: the search reads every scenario whatever the filters hide (the owner, 2026-10-27), so no note counts
    // what they hide
    [Fact]
    public async Task The_box_s_filters_leave_the_results_whole()
    {
        await OpenBakeryAsync("Tab_R20.html");
        await SearchAsync("pancake");
        Assert.Equal("3 results", await CountAsync());

        await Page.Locator(".report-tabs > .report-tab[data-tab='features']").ClickAsync();
        await OpenFilterRowsAsync();
        await Page.Locator("#filtering-box .status-toggle[data-status='Failed']").ClickAsync();
        await Page.Locator("#tab-search").ClickAsync();
        await ResultsShownAsync();

        Assert.Equal("3 results", await CountAsync());
        Assert.Equal(0, await Page.Locator("#panel-search .search-filtered-note").CountAsync());
    }

    // R21
    [Fact]
    public async Task The_kebab_exports_exactly_the_results()
    {
        await OpenBakeryAsync("Tab_R21.html");
        await SearchAsync("regression");

        await Page.Locator("#panel-search .search-kebab").ClickAsync();
        var csv = await Page.RunAndWaitForDownloadAsync(() => Page.Locator("#panel-search .search-menu-item:text-is('Export results as CSV')").ClickAsync());
        var lines = (await File.ReadAllTextAsync((await csv.PathAsync())!)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains(lines, l => l.Contains("Pancakes without flour are rejected", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("A waffle is made", StringComparison.Ordinal));

        // the HTML export is a compressed page of its own: opened, it holds the two results and nothing else
        await Page.Locator("#panel-search .search-kebab").ClickAsync();
        var html = await Page.RunAndWaitForDownloadAsync(() => Page.Locator("#panel-search .search-menu-item:text-is('Export results as HTML')").ClickAsync());
        var path = Path.Combine(TempDir, "Tab_R21_export.html");
        await html.SaveAsAsync(path);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        // the export keeps each result's whole feature, the other scenarios in it hidden, as Export Filtered HTML always did
        var names = await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('details.scenario')).filter(s => s.style.display !== 'none').map(s => s.getAttribute('data-search') || '')");
        Assert.Equal(2, names.Length);
        Assert.Contains(names, n => n.Contains("pancakes without flour are rejected", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("a waffle is made", StringComparison.Ordinal));
    }

    // R23
    [Fact]
    public async Task Down_and_Up_on_the_Search_tab_scroll_and_stay_on_it()
    {
        await OpenBakeryAsync("Tab_R23.html", height: 500);
        await SearchAsync("toast");

        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("ArrowUp");
        await TwoAnimationFramesAsync(Page);

        Assert.Equal("search", await SelectedTabAsync());
    }

    // R25, 5.12.0: a row is on exactly while the query names it, however the qualifier got there, and a click on it takes it
    // out (until then a row was the box's chip, read when the sidebar was drawn, so it could stick)
    [Fact]
    public async Task A_sidebar_row_is_on_while_the_query_names_it()
    {
        await OpenBakeryAsync("Tab_R25.html");
        await SearchAsync("pancake tag:menu");
        Assert.True(await SideCurrentAsync("menu"));
        Assert.Equal("off", await Page.EvaluateAsync<string>("() => chip_state(find_chip('tag', 'menu'))"));

        await ClickSideAsync("menu");
        Assert.Equal("pancake", await Page.EvaluateAsync<string>("() => search_query()"));
        Assert.False(await SideCurrentAsync("menu"));
    }

    // R26
    [Fact]
    public async Task The_focus_goes_to_the_count_after_a_search_a_type_and_a_page_and_stays_on_sort_after_a_sort()
    {
        await OpenBakeryAsync("Tab_R26.html");
        await SearchAsync("toast");
        Assert.Equal("search-count", await Page.EvaluateAsync<string>("() => document.activeElement.id"));

        await ClickSideAsync("Features");
        Assert.Equal("search-count", await Page.EvaluateAsync<string>("() => document.activeElement.id"));
        await ClickSideAsync("Scenarios");

        await Page.Locator("#panel-search .search-pages .search-page:text-is('2')").ClickAsync();
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("search-count", await Page.EvaluateAsync<string>("() => document.activeElement.id"));

        await Page.Locator("#panel-search .search-sort-select").SelectOptionAsync("name");
        await TwoAnimationFramesAsync(Page);
        Assert.True(await Page.EvaluateAsync<bool>("() => document.activeElement.classList.contains('search-sort-select')"));
    }

    // R28
    [Fact]
    public async Task A_Specifications_page_searches_as_TestRunReport_does()
    {
        await OpenBakeryAsync("Tab_R28.html", includeTestRunData: false);
        await SearchAsync("pancake");

        Assert.Equal("search", await SelectedTabAsync());
        Assert.Equal("3 results", await CountAsync());
    }

    // R31: a reload of the Search tab shows its results once; q= is a 5.11.0 link's search, read as search= (5.12.0)
    [Theory]
    [InlineData("#tab=search&search=pancake")]
    [InlineData("#tab=search&q=pancake")]
    public async Task A_reload_of_tab_search_shows_the_results_once(string hash)
    {
        await OpenBakeryAsync($"Tab_R31_{hash.Length}.html", hash: hash);
        await ResultsShownAsync();

        Assert.Equal("search", await SelectedTabAsync());
        Assert.Equal("3 results", await CountAsync());
        Assert.True(await Page.Locator("#tab-search").IsVisibleAsync());
    }

    // R32
    [Fact]
    public async Task tab_search_with_no_q_opens_the_Features_tab()
    {
        await OpenBakeryAsync("Tab_R32.html", hash: "#tab=search");
        await TwoAnimationFramesAsync(Page);

        Assert.Equal("features", await SelectedTabAsync());
        Assert.True(await Page.Locator("#tab-search").IsHiddenAsync());
    }

    // R15
    [Fact]
    public async Task Back_from_a_result_returns_to_its_page_type_sort_and_scroll_with_the_focus_on_it()
    {
        await OpenBakeryAsync("Tab_R15.html");
        await SearchAsync("toast");
        await Page.Locator("#panel-search .search-sort-select").SelectOptionAsync("duration:desc");
        await Page.Locator("#panel-search .search-pages .search-page:text-is('2')").ClickAsync();
        await TwoAnimationFramesAsync(Page);
        var titles = await TitlesAsync();
        var link = Page.Locator("#panel-search .search-result-title").Last;
        await link.ClickAsync();
        await Page.WaitForFunctionAsync("() => document.querySelector('.report-tabs > .report-tab[aria-selected=\"true\"]').getAttribute('data-tab') === 'features'",
            null, new() { PollingInterval = 200 });

        await Page.GoBackAsync();
        await ResultsShownAsync();

        Assert.Equal("search", await SelectedTabAsync());
        Assert.Equal(titles, await TitlesAsync());
        Assert.Equal("duration:desc", await Page.Locator("#panel-search .search-sort-select").InputValueAsync());
        Assert.Contains("p=2", await HashAsync());
        await Page.WaitForFunctionAsync("() => document.activeElement && document.activeElement.classList.contains('search-result-title')",
            null, new() { PollingInterval = 200 });
        Assert.Equal(titles[^1], await Page.EvaluateAsync<string>("() => document.activeElement.textContent"));
    }
}
