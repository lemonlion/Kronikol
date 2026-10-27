using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The query builder (plans/SEARCH_PLAN.md 4.3, 4.8; facts QB1 to QB25, B1, B2, B7): GitHub's, a dialog over the page that
/// the search button or / opens, holding the applied query; suggestions as you type (the search, scenarios and features to
/// jump to, the keys and their values with counts), recent searches, syntax tips; a combobox over a listbox with nothing
/// active until Down; Enter runs the search once; Esc drops the draft; a layer of the session history.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class SearchBuilderTests : PlaywrightTestBase
{
    public SearchBuilderTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenAsync(string fileName, int width = 1280, int height = 800, string hash = "")
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoReportAsync(SearchReports.Write(TempDir, OutputDir, fileName) + hash);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await TwoAnimationFramesAsync(Page);
    }

    private ILocator Input => Page.Locator("#search-input");
    private ILocator Options => Page.Locator("#search-suggestions [role='option']");

    private Task<bool> BuilderOpen() => Page.EvaluateAsync<bool>("() => document.getElementById('search-dialog').open");

    private async Task OpenBuilderAsync()
    {
        await Page.Locator("#search-button").ClickAsync();
        // open, and done growing in: the panel slides 8 px as it opens, which a measure taken on the way would read
        await Page.WaitForFunctionAsync("() => document.getElementById('search-dialog').open"
            + " && document.querySelector('.search-panel').getAnimations().every(a => a.playState === 'finished')", null, new() { PollingInterval = 200 });
    }

    private Task<string[]> OptionNames() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('#search-suggestions [role=option]')).map(o => o.getAttribute('aria-label'))");

    // QB1
    [Fact]
    public async Task Clicking_the_search_box_opens_the_builder_with_the_focus_in_its_input()
    {
        await OpenAsync("Builder_Click.html");
        await OpenBuilderAsync();

        await Expect(Input).ToBeFocusedAsync();
        await Expect(Page.Locator("#search-button")).ToHaveAttributeAsync("aria-expanded", "true");
    }

    // QB2
    [Fact]
    public async Task Slash_opens_the_builder_from_a_tab_and_a_scenario_but_not_from_a_field()
    {
        await OpenAsync("Builder_Slash.html");
        await Page.Locator(".report-tab[data-tab='features']").FocusAsync();
        await Page.Keyboard.PressAsync("/");
        Assert.True(await BuilderOpen());
        await Page.Keyboard.PressAsync("Escape");
        await Page.WaitForFunctionAsync("() => !document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });

        await Page.Locator("details.scenario > summary").First.FocusAsync();
        await Page.Keyboard.PressAsync("/");
        Assert.True(await BuilderOpen());
        await Page.Keyboard.PressAsync("Escape");
        await Page.WaitForFunctionAsync("() => !document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });

        // a field keeps its slash
        await OpenFilterRowsAsync();
        await Page.Locator(".percentile-btn[data-custom='1']").ClickAsync();
        await Page.Locator("#duration-threshold").FocusAsync();
        await Page.Keyboard.PressAsync("/");
        Assert.False(await BuilderOpen());
    }

    // QB3
    [Theory]
    [InlineData(1440)]
    [InlineData(1280)]
    [InlineData(1012)]
    [InlineData(769)]
    [InlineData(768)]
    [InlineData(390)]
    public async Task The_builder_s_input_lands_on_the_search_box_and_is_a_top_sheet_on_a_phone(int width)
    {
        await OpenAsync($"Builder_Place_{width}.html", width);
        var box = (await Page.Locator(".search-box").BoundingBoxAsync())!;
        await OpenBuilderAsync();
        var panel = (await Page.Locator(".search-panel").BoundingBoxAsync())!;

        if (width > 768)
        {
            // the panel grows from the box by 14 px: its right edge 14 px past the box's, its top 14 px above
            Assert.InRange(panel.X + panel.Width - (box.X + box.Width), 13, 15);
            Assert.InRange(box.Y - panel.Y, 13, 15);
            Assert.True(panel.Width <= Math.Min(720, width - 32) + 0.5);
        }
        else
        {
            Assert.InRange(panel.Y, -0.5, 0.5);
            Assert.InRange(panel.X, -0.5, 0.5);
            Assert.InRange(panel.Width, width - 0.5, width + 0.5);
            Assert.True(panel.Height <= 800 * 0.8 + 1);
        }
    }

    // QB4
    [Fact]
    public async Task The_page_behind_the_builder_is_inert_and_a_click_outside_closes_it()
    {
        await OpenAsync("Builder_Inert.html");
        await OpenBuilderAsync();

        // a click at the page's corner lands on the dialog's own backdrop, not the page
        var hit = await Page.EvaluateAsync<string>("() => { const e = document.elementFromPoint(5, window.innerHeight - 5); return e ? e.id || e.className : ''; }");
        Assert.Equal("search-dialog", hit);
        await Page.Mouse.ClickAsync(5, 795);
        await Page.WaitForFunctionAsync("() => !document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
    }

    // QB5
    [Fact]
    public async Task Typing_lists_scenarios_and_features_to_jump_to()
    {
        await OpenAsync("Builder_Jump.html");
        await OpenBuilderAsync();
        await Input.FillAsync("pancake");

        var names = await OptionNames();
        // each option's name ends with its group, since a group inside a listbox is poorly supported (4.3.4)
        Assert.Contains("A valid pancake request returns a batch, Pancakes Creation, Jump to", names);
        Assert.Contains("Pancakes Creation, 3 scenarios, Jump to", names);
        Assert.True(names.Count(n => n.EndsWith(", Jump to", StringComparison.Ordinal)) <= 8);
        // the word typed is at 700 in each name, as GitHub's jump rows show what matched
        var bolded = await Page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('#search-suggestions .search-option-scenario .search-option-label b, #search-suggestions .search-option-feature .search-option-label b')).map(b => b.textContent.toLowerCase())");
        Assert.NotEmpty(bolded);
        Assert.All(bolded, b => Assert.Equal("pancake", b));
    }

    // QB6
    [Fact]
    public async Task Jumping_opens_the_scenario_and_leaves_the_applied_query()
    {
        await OpenAsync("Builder_JumpGo.html", hash: "#search=pancake");
        await OpenBuilderAsync();
        await Input.FillAsync("toppings");
        await Options.Filter(new() { HasText = "Pancake toppings are listed" }).First.ClickAsync();

        await Page.WaitForFunctionAsync("() => !document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>("() => document.getElementById('scenario-p3') ? document.getElementById('scenario-p3').open : [...document.querySelectorAll('details.scenario')].some(s => s.open && s.textContent.includes('Pancake toppings'))"));
        Assert.Equal("pancake", await Page.EvaluateAsync<string>("() => search_query()"));
    }

    // QB7
    [Fact]
    public async Task Down_activates_the_first_row_and_the_focus_stays_in_the_input()
    {
        await OpenAsync("Builder_Down.html");
        await OpenBuilderAsync();
        await Input.FillAsync("waffle");
        Assert.Null(await Input.GetAttributeAsync("aria-activedescendant"));

        await Page.Keyboard.PressAsync("ArrowDown");

        var active = await Input.GetAttributeAsync("aria-activedescendant");
        Assert.Equal(await Options.First.GetAttributeAsync("id"), active);
        await Expect(Input).ToBeFocusedAsync();
        await Expect(Options.First).ToHaveAttributeAsync("aria-selected", "true");
        // Up on the first row does not wrap
        await Page.Keyboard.PressAsync("ArrowUp");
        Assert.Equal(active, await Input.GetAttributeAsync("aria-activedescendant"));
    }

    // QB8
    [Fact]
    public async Task Enter_with_no_active_row_runs_the_search_and_opens_the_Search_tab_in_one_entry()
    {
        await OpenAsync("Builder_Enter.html");
        var length = await Page.EvaluateAsync<int>("() => history.length");
        await OpenBuilderAsync();
        await Input.FillAsync("waffle");
        await Page.Keyboard.PressAsync("Enter");

        await Page.WaitForFunctionAsync("() => !document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
        Assert.Equal("waffle", await Page.EvaluateAsync<string>("() => search_query()"));
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
        await Expect(Page.Locator(".report-tab[data-tab='search']")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Equal(length + 1, await Page.EvaluateAsync<int>("() => history.length"));
        Assert.Contains("search=waffle", await Page.EvaluateAsync<string>("() => location.hash"));
    }

    // B1
    [Fact]
    public async Task Enter_runs_one_search()
    {
        await OpenAsync("Builder_EnterOnce.html");
        // the search's own run (5.12.0); the filter field's Enter is FilterBoxTests' Enter_in_the_filter_field_filters_once
        await Page.EvaluateAsync("() => { window.__runs = 0; const run = window.run_search_query; window.run_search_query = function () { window.__runs++; return run.apply(this, arguments); }; }");

        await OpenBuilderAsync();
        await Input.FillAsync("toast");
        await Page.Keyboard.PressAsync("Enter");
        await Page.WaitForTimeoutAsync(600);

        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__runs"));
    }

    // B2
    [Fact]
    public async Task A_query_put_in_without_a_keyup_is_read_without_leaving_the_field()
    {
        await OpenAsync("Builder_Insert.html");
        await OpenBuilderAsync();
        await Input.FocusAsync();

        await Page.Keyboard.InsertTextAsync("waffle");

        await Expect(Options.First).ToBeVisibleAsync();
        Assert.Contains(await OptionNames(), n => n.StartsWith("A waffle is made", StringComparison.Ordinal));
        await Expect(Input).ToBeFocusedAsync();
    }

    // B7
    [Fact]
    public async Task Slash_does_nothing_under_a_lightbox()
    {
        await OpenAsync("Builder_SlashLayer.html");
        await Page.EvaluateAsync("() => { window.kronikol_layers = window.kronikol_layers || {}; window.kronikol_layers.fake = { isOpen: () => true, open: () => {}, close: () => {} }; }");

        var prevented = await Page.EvaluateAsync<bool>("() => { const e = new KeyboardEvent('keydown', { key: '/', bubbles: true, cancelable: true }); document.body.dispatchEvent(e); return e.defaultPrevented; }");

        Assert.False(prevented);
        Assert.False(await BuilderOpen());
    }

    // 4.3.2: the draft is drawn coloured over the input (a key, a value, an operator), and a qualifier the report cannot read
    // in --kron-warn with the field's edge, but not while its value is still being typed at the caret
    [Fact]
    public async Task The_draft_is_coloured_and_an_unreadable_qualifier_turns_the_field_amber()
    {
        await OpenAsync("Builder_Colours.html");
        await OpenBuilderAsync();

        await Input.FillAsync("-tag:smoke && duration:abc");
        Assert.False(await Page.EvaluateAsync<bool>("() => document.querySelector('.search-input-row').classList.contains('has-bad')"));

        await Input.PressAsync("End");
        await Page.Keyboard.TypeAsync(" toast");
        var spans = await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('.search-mirror span')).map(s => s.className + '=' + s.textContent)");
        Assert.Contains("q-key=tag:", spans);
        Assert.Contains("q-value=smoke", spans);
        Assert.Contains("q-op=&&", spans);
        Assert.Contains("q-bad=abc", spans);
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector('.search-input-row').classList.contains('has-bad')"));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector('.search-input-wrap').classList.contains('is-coloured')"));
    }

    // 5.12.0: the coloured copy over the input draws each word where the input has it, every space kept. Laid out as a flex
    // box, the copy dropped the space after a coloured part, so the word after dep:Clickhouse sat against the value
    [Theory]
    [InlineData("dep:Clickhouse foo", "foo")]
    [InlineData("toast && status:failed waffle", "waffle")]
    [InlineData("tag:smoke -tag:slow pancake", "pancake")]
    public async Task The_coloured_draft_draws_each_word_where_the_input_has_it(string draft, string last)
    {
        await OpenAsync($"Builder_Spaces{draft.Length}.html");
        await OpenBuilderAsync();
        await Input.FillAsync(draft);
        await Expect(Page.Locator(".search-input-wrap")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-coloured\\b"));

        // the input's own place for the last word, measured in its font, less the copy's
        var off = await Page.EvaluateAsync<double>("""
            ([draft, last]) => {
                const input = document.getElementById('search-input'), mirror = document.querySelector('.search-mirror');
                const cs = getComputedStyle(input), ctx = document.createElement('canvas').getContext('2d');
                ctx.font = cs.fontWeight + ' ' + cs.fontSize + ' ' + cs.fontFamily;
                const want = ctx.measureText(draft.slice(0, draft.lastIndexOf(last))).width;
                const walk = document.createTreeWalker(mirror, NodeFilter.SHOW_TEXT);
                let node = null;
                for (let n = walk.nextNode(); n; n = walk.nextNode()) if (n.nodeValue.endsWith(last)) node = n;
                const r = document.createRange();
                r.setStart(node, node.nodeValue.length - last.length);
                r.setEnd(node, node.nodeValue.length);
                return r.getBoundingClientRect().left - mirror.getBoundingClientRect().left + mirror.scrollLeft - want;
            }
            """, new object[] { draft, last });
        Assert.InRange(off, -1, 1);
    }

    // B7: an exported page has no search to open, so / is the browser's (its quick find in Firefox), not swallowed
    [Fact]
    public async Task Slash_is_left_to_the_browser_on_an_exported_page()
    {
        await OpenAsync("Builder_SlashExport.html");
        var download = await Page.RunAndWaitForDownloadAsync(() => Page.EvaluateAsync("() => export_html()"));
        var path = Path.Combine(TempDir, "Builder_SlashExport_exported.html");
        await download.SaveAsAsync(path);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });

        var prevented = await Page.EvaluateAsync<bool>("() => { const e = new KeyboardEvent('keydown', { key: '/', bubbles: true, cancelable: true }); document.body.dispatchEvent(e); return e.defaultPrevented; }");

        Assert.False(prevented);
    }

    // QB9
    [Fact]
    public async Task A_partial_key_offers_the_key_and_Tab_completes_it()
    {
        await OpenAsync("Builder_Key.html");
        await OpenBuilderAsync();
        await Input.FillAsync("sta");

        var row = Options.Filter(new() { HasText = "status:" }).First;
        await Expect(row).ToBeVisibleAsync();
        while (await Input.GetAttributeAsync("aria-activedescendant") != await row.GetAttributeAsync("id"))
            await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Tab");

        await Expect(Input).ToHaveValueAsync("status:");
        await Expect(Input).ToBeFocusedAsync();
    }

    // QB10
    [Fact]
    public async Task After_status_colon_the_statuses_show_with_their_counts()
    {
        await OpenAsync("Builder_Statuses.html");
        await OpenBuilderAsync();
        await Input.FillAsync("status:");

        var names = await OptionNames();
        // a scenario skipped after a failure counts as skipped, as the Status buttons count it since 5.9.1 (B4, E3)
        Assert.Contains("status:passed, 15 scenarios, Statuses", names);
        Assert.Contains("status:failed, 1 scenario, Statuses", names);
        Assert.Contains("status:skipped, 2 scenarios, Statuses", names);
    }

    // QB11
    [Fact]
    public async Task After_tag_colon_the_tags_show_with_their_chips_counts()
    {
        await OpenAsync("Builder_Tags.html");
        await OpenBuilderAsync();
        await Input.FillAsync("tag:sm");

        var names = await OptionNames();
        Assert.Contains("tag:smoke, 2 scenarios, Tags", names);
        Assert.DoesNotContain(names, n => n.StartsWith("tag:regression", StringComparison.Ordinal));
    }

    // QB12
    [Fact]
    public async Task After_duration_colon_this_report_s_percentiles_show_with_their_times()
    {
        await OpenAsync("Builder_Durations.html");
        await OpenBuilderAsync();
        await Input.FillAsync("duration:");

        var names = await OptionNames();
        Assert.Contains(names, n => Regex.IsMatch(n, @"^duration:>p90 \(.+ and over\)"));
        Assert.Contains(names, n => n.StartsWith("duration:>2s", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("duration:1s..5s", StringComparison.Ordinal));
    }

    // QB13
    [Fact]
    public async Task Escape_closes_the_builder_drops_the_draft_and_focuses_the_search_box()
    {
        await OpenAsync("Builder_Escape.html", hash: "#search=toast");
        await OpenBuilderAsync();
        await Input.FillAsync("waffle");
        await Page.Keyboard.PressAsync("Escape");

        await Page.WaitForFunctionAsync("() => !document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
        Assert.Equal("toast", await Page.EvaluateAsync<string>("() => search_query()"));
        await Expect(Page.Locator("#search-button")).ToBeFocusedAsync();
        await OpenBuilderAsync();
        await Expect(Input).ToHaveValueAsync("toast");
    }

    // QB14
    [Fact]
    public async Task The_live_region_counts_the_suggestions()
    {
        await OpenAsync("Builder_Live.html");
        await OpenBuilderAsync();
        await Input.FillAsync("waffle");

        var count = await Options.CountAsync();
        await Expect(Page.Locator(".search-live")).ToHaveTextAsync(new Regex($"^{count} suggestions?\\.\\u00a0?$"));
    }

    // QB15
    [Fact]
    public async Task The_syntax_tips_open_in_the_builder_and_typing_brings_the_suggestions_back()
    {
        await OpenAsync("Builder_Tips.html");
        await OpenBuilderAsync();
        await Page.Locator(".search-tips-toggle").ClickAsync();

        await Expect(Page.Locator("#search-tips")).ToBeVisibleAsync();
        await Expect(Page.Locator("#search-suggestions")).ToBeHiddenAsync();
        await Expect(Page.Locator(".search-tips-toggle")).ToHaveAttributeAsync("aria-expanded", "true");

        await Input.FocusAsync();
        await Page.Keyboard.TypeAsync("w");
        await Expect(Page.Locator("#search-tips")).ToBeHiddenAsync();
        await Expect(Page.Locator("#search-suggestions")).ToBeVisibleAsync();
    }

    // QB16
    [Fact]
    public async Task Keys_in_the_builder_never_reach_the_page()
    {
        await OpenAsync("Builder_Keys.html");
        await OpenBuilderAsync();
        await Input.FillAsync("waffle");
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("ArrowDown");

        Assert.Equal(0, await Page.Locator(".scenario-focused").CountAsync());
        Assert.Equal("features", await Page.EvaluateAsync<string>("() => document.querySelector('.report-tab[aria-selected=true]').dataset.tab"));
    }

    // QB17
    [Fact]
    public async Task The_builder_is_a_layer_Back_closes_it_and_a_search_takes_its_entry()
    {
        await OpenAsync("Builder_Layer.html");
        var length = await Page.EvaluateAsync<int>("() => history.length");
        await OpenBuilderAsync();
        Assert.Equal(length + 1, await Page.EvaluateAsync<int>("() => history.length"));

        await Page.GoBackAsync();
        await Page.WaitForFunctionAsync("() => !document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });

        await OpenBuilderAsync();
        await Input.FillAsync("toast");
        await Page.Keyboard.PressAsync("Enter");
        await Page.WaitForFunctionAsync("() => location.hash.includes('search=toast')", null, new() { PollingInterval = 200 });
        // the search took the builder's entry: Back leaves the search, and does not reopen the builder
        await Page.GoBackAsync();
        await Page.WaitForFunctionAsync("() => !location.hash.includes('search=')", null, new() { PollingInterval = 200 });
        Assert.False(await BuilderOpen());
    }

    // QB18
    [Fact]
    public async Task Recent_lists_this_page_s_searches_and_nothing_is_stored()
    {
        await OpenAsync("Builder_Recent.html");
        var before = await Page.EvaluateAsync<string>("() => JSON.stringify(Object.keys(localStorage).concat(Object.keys(sessionStorage)).sort())");
        await FillSearchBar("toast", toSearchTab: true);
        await FillSearchBar("waffle", toSearchTab: true);
        await OpenBuilderAsync();
        await Input.FillAsync("");

        var names = await OptionNames();
        var recent = names.Where(n => n.EndsWith(", Recent", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, recent.Length);
        Assert.StartsWith("waffle", recent[0]);
        Assert.Equal(before, await Page.EvaluateAsync<string>("() => JSON.stringify(Object.keys(localStorage).concat(Object.keys(sessionStorage)).sort())"));
    }

    // QB19
    [Fact]
    public async Task Under_reduced_motion_the_builder_does_not_move()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await OpenAsync("Builder_Motion.html");
        await OpenBuilderAsync();

        Assert.Equal("none", await Page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.search-panel')).animationName"));
    }

    // QB20
    [Fact]
    public async Task A_draft_never_reaches_the_address()
    {
        await OpenAsync("Builder_Draft.html");
        await OpenBuilderAsync();
        await Input.FillAsync("waffle");
        // the applied search's field holds what was applied (nothing yet), never the draft
        Assert.Equal("", await Page.EvaluateAsync<string>("() => document.getElementById('searchbar').value"));
        await Page.Keyboard.PressAsync("Escape");
        await Page.WaitForFunctionAsync("() => !document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
        await OpenFilterRowsAsync();
        await Page.Locator("#filtering-box .status-toggle[data-status='Passed']").ClickAsync();

        await Page.WaitForFunctionAsync("() => location.hash.includes('status=Passed')", null, new() { PollingInterval = 200 });
        Assert.DoesNotContain("q=", await Page.EvaluateAsync<string>("() => location.hash"));
    }

    // QB21
    [Fact]
    public async Task Composed_text_stays_visible_while_an_input_method_composes()
    {
        await OpenAsync("Builder_Compose.html");
        await OpenBuilderAsync();
        await Input.FillAsync("pan");
        await Input.DispatchEventAsync("compositionstart");

        Assert.NotEqual("rgba(0, 0, 0, 0)", await Input.EvaluateAsync<string>("e => getComputedStyle(e).color"));
        await Input.DispatchEventAsync("compositionend");
        Assert.Equal("rgba(0, 0, 0, 0)", await Input.EvaluateAsync<string>("e => getComputedStyle(e).color"));
    }

    // QB24, 5.12.0: a jump leaves the search as it is, since a search hides nothing (the owner: a search is no filter);
    // one the box's filters hide lands on the note for a hidden scenario
    [Fact]
    public async Task A_jump_leaves_the_search_and_one_the_filters_hide_lands_on_their_note()
    {
        await OpenAsync("Builder_JumpHidden.html", hash: "#search=toast");
        await OpenBuilderAsync();
        await Input.FillAsync("waffle is made");
        await Options.Filter(new() { HasText = "A waffle is made" }).First.ClickAsync();

        await Page.WaitForFunctionAsync("() => [...document.querySelectorAll('details.scenario')].some(s => s.open && s.textContent.includes('A waffle is made') && s.style.display !== 'none')", null, new() { PollingInterval = 200 });
        Assert.Equal("toast", await Page.EvaluateAsync<string>("() => search_query()"));

        // one the box's filters hide lands on the note for a hidden scenario
        await OpenAsync("Builder_JumpFiltered.html", hash: "#status=Failed");
        await OpenBuilderAsync();
        await Input.FillAsync("waffle is made");
        await Options.Filter(new() { HasText = "A waffle is made" }).First.ClickAsync();
        await Expect(Page.Locator(".filtered-out-note")).ToBeVisibleAsync();
    }

    // QB25
    [Fact]
    public async Task The_builder_opens_holding_the_applied_query_selected()
    {
        await OpenAsync("Builder_Holds.html", hash: "#search=toast");
        await OpenBuilderAsync();

        await Expect(Input).ToHaveValueAsync("toast");
        Assert.Equal("toast", await Input.EvaluateAsync<string>("e => e.value.substring(e.selectionStart, e.selectionEnd)"));
    }
}
