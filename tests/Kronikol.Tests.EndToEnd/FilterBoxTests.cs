using System.Text.RegularExpressions;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The filter box behind the bar's Filters button (plans/SEARCH_PLAN.md 4.7, F1 to F10, facts F-a to F-p). The box is
/// hidden when the page opens; Filters shows at rest, pinned and on a phone, opens the box at the top of the bar with
/// every row showing, closes it again (so do Escape and the box's close, each handing the focus back), and counts the
/// filters on; the box stays open when the bar comes to rest; what was being read stays put; a pinned box takes at most
/// 60% of the window; a page with only its features has the bar; the filters are announced from the bar's own status
/// element; a link's filters open with the box closed; the rows lay out by measure; the exports are unchanged; and the
/// box holds the filter field, hidden with it until Filters opens it.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class FilterBoxTests : PlaywrightTestBase
{
    public FilterBoxTests(PlaywrightFixture fixture) : base(fixture) { }

    // thirty scenarios in three features, so the page scrolls past the bar; every third tagged smoke, one a happy path,
    // one failed; manyTags gives each scenario a tag of its own, so the box is taller than a short window
    private string Generate(string fileName, bool durations = true, bool manyTags = false)
    {
        var features = Enumerable.Range(1, 3).Select(f => new Feature
        {
            DisplayName = $"Feature {f}",
            Scenarios = Enumerable.Range(1, 10).Select(i => new Scenario
            {
                Id = $"f{f}s{i}", DisplayName = $"Scenario {f}.{i}", Result = i == 5 && f == 2 ? ExecutionResult.Failed : ExecutionResult.Passed,
                Duration = durations ? TimeSpan.FromMilliseconds(10 * i + f) : null,
                Tags = manyTags ? [$"area-{f}-{i}-with-a-longer-name", i % 3 == 0 ? "smoke" : "regression"] : i % 3 == 0 ? ["smoke"] : null,
                IsHappyPath = f == 1 && i == 1,
                ErrorMessage = i == 5 && f == 2 ? "boom" : null
            }).ToArray()
        }).ToArray();
        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenAsync(string fileName, string hash = "", int width = 1400, int height = 700, bool durations = true, bool manyTags = false)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoReportAsync(Generate(fileName, durations, manyTags) + hash);
        // attached, not visible: a link's filters can hide the first feature
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await TwoAnimationFramesAsync(Page);
    }

    private ILocator Box => Page.Locator("#filtering-box");
    private ILocator Toggle => Page.Locator(".nav-filters-toggle");
    private ILocator Field => Page.Locator("#filtering-box #searchbar");
    private ILocator NavStatus => Page.Locator(".report-nav .nav-status[role='status']");

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
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.open = true)");
        await Page.EvaluateAsync("() => window.scrollTo(0, document.querySelector('.nav-mark').getBoundingClientRect().top + window.scrollY + 300)");
        await Page.WaitForFunctionAsync("() => document.querySelector('.report-nav').classList.contains('is-pinned')", null, new() { PollingInterval = 200 });
    }

    // what is just below the bar, and how far below its bottom edge
    private const string Below = "() => { const r = document.querySelector('.report-nav').getBoundingClientRect(); const e = document.elementFromPoint(r.left + r.width / 2, r.bottom + 8); return e ? Math.round(e.getBoundingClientRect().top - r.bottom) + ' ' + (e.id || e.className) : ''; }";

    private Task<bool> Shows(string selector) => Page.EvaluateAsync<bool>(
        "s => { const e = document.querySelector(s); return !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden'; }", selector);

    // F-a
    [Fact]
    public async Task The_box_is_hidden_when_the_page_opens()
    {
        await OpenAsync("FilterBox_Hidden.html");

        Assert.False(await Shows("#filtering-box"));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.getElementById('filtering-box').hidden"));
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "false");
    }

    // F-b
    [Fact]
    public async Task Filters_shows_at_rest_when_pinned_and_on_a_phone()
    {
        await OpenAsync("FilterBox_Shows.html");
        await Expect(Toggle).ToBeVisibleAsync();

        await ScrollPastTheBar();
        await Expect(Toggle).ToBeVisibleAsync();

        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await Page.SetViewportSizeAsync(390, 800);
        await TwoAnimationFramesAsync(Page);
        await Expect(Toggle).ToBeVisibleAsync();
    }

    // F-c
    [Theory]
    [InlineData(1400)]
    [InlineData(390)]
    public async Task Filters_opens_the_box_at_the_bar_s_top_with_every_row_showing(int width)
    {
        await OpenAsync($"FilterBox_Opens{width}.html", width: width);

        await Toggle.ClickAsync();

        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.True(await Shows("#filtering-box"));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector('.toolbar-row.report-nav').firstElementChild === document.getElementById('filtering-box')"));
        foreach (var row in new[] { ".status-filters", ".duration-filters", ".happy-filters", ".tag-filters" })
            Assert.True(await Shows($"#filtering-box {row}"), row);

        await Toggle.ClickAsync();
        Assert.False(await Shows("#filtering-box"));
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "false");
    }

    // F-e
    [Fact]
    public async Task Filters_counts_the_filters_on_and_says_so_in_its_name()
    {
        await OpenAsync("FilterBox_Count.html");
        await Expect(Toggle).ToHaveAttributeAsync("aria-label", "Filters");

        await Toggle.ClickAsync();
        await Page.Locator("#filtering-box .status-toggle[data-status='Failed']").ClickAsync();
        await Page.Locator("#filtering-box .tag-toggle[data-tag='smoke']").First.ClickAsync();
        await Toggle.ClickAsync();

        await Expect(Toggle).ToHaveAttributeAsync("aria-label", "Filters, 2 on");
        await Expect(Page.Locator(".nav-filters-toggle .filters-count")).ToHaveTextAsync("2");
        Assert.True(await Shows(".nav-filters-toggle .filters-count"));
    }

    // F-f
    [Fact]
    public async Task Escape_and_the_close_button_close_the_box_and_focus_Filters()
    {
        await OpenAsync("FilterBox_Close.html");

        await Toggle.ClickAsync();
        await Expect(Box).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Page.Keyboard.PressAsync("Escape");
        Assert.False(await Shows("#filtering-box"));
        await Expect(Toggle).ToBeFocusedAsync();

        await Toggle.ClickAsync();
        await Page.Locator("#filtering-box .filters-close").ClickAsync();
        Assert.False(await Shows("#filtering-box"));
        await Expect(Toggle).ToBeFocusedAsync();
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "false");
    }

    // F-g
    [Fact]
    public async Task The_box_stays_open_when_the_bar_comes_to_rest()
    {
        await OpenAsync("FilterBox_Rest.html");
        await ScrollPastTheBar();
        await Toggle.ClickAsync();
        Assert.True(await Shows("#filtering-box"));

        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await Page.WaitForFunctionAsync("() => !document.querySelector('.report-nav').classList.contains('is-pinned')", null, new() { PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        Assert.True(await Shows("#filtering-box"));
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "true");
    }

    // F-h
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Opening_and_closing_keep_what_was_being_read_in_place(bool pinned)
    {
        await OpenAsync($"FilterBox_Keep{(pinned ? "Pinned" : "Rest")}.html");
        if (pinned) await ScrollPastTheBar();
        else await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.open = true)");

        var reading = await Settled(Below);
        await Toggle.ClickAsync();
        Assert.Equal(reading, await Settled(Below));
        await Toggle.ClickAsync();
        Assert.Equal(reading, await Settled(Below));
    }

    // F-i
    [Fact]
    public async Task A_pinned_box_takes_at_most_60_percent_of_the_window_and_scrolls_inside()
    {
        await OpenAsync("FilterBox_Cap.html", width: 900, height: 600, manyTags: true);
        await ScrollPastTheBar();
        await Toggle.ClickAsync();
        await TwoAnimationFramesAsync(Page);

        var bar = await Page.EvaluateAsync<double>("() => document.querySelector('.report-nav').getBoundingClientRect().height");
        var window = await Page.EvaluateAsync<double>("() => window.innerHeight");
        Assert.True(bar <= window * 0.6 + 1, $"the pinned bar is {bar} px tall in a {window} px window");
        Assert.True(await Page.EvaluateAsync<bool>("() => { const b = document.getElementById('filtering-box'); return b.scrollHeight > b.clientHeight + 1; }"));
    }

    // F-j
    [Fact]
    public async Task A_page_with_only_its_features_has_the_bar_with_Features_and_Filters()
    {
        await OpenAsync("FilterBox_FeaturesOnly.html", durations: false);

        var tabs = await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('.report-tabs > .report-tab:not([hidden])')).map(t => t.getAttribute('data-tab'))");
        Assert.Equal(["features"], tabs);
        await Expect(Toggle).ToBeVisibleAsync();
        await Toggle.ClickAsync();
        Assert.True(await Shows("#filtering-box .status-filters"));
    }

    // F-k
    [Fact]
    public async Task The_filters_are_announced_with_the_box_closed_open_and_the_bar_short()
    {
        await OpenAsync("FilterBox_Announce.html");
        var status = Page.Locator(".report-nav .nav-status[role='status']");
        await Expect(status).ToHaveCountAsync(1);
        Assert.Equal("true", await Page.Locator("#filtering-box .filter-readout").GetAttributeAsync("aria-hidden"));
        Assert.Equal("true", await Page.Locator(".nav-readout").GetAttributeAsync("aria-hidden"));
        await Expect(status).ToHaveTextAsync("All 30 scenarios, 3 features");

        // closed: a link's filters
        await Page.EvaluateAsync("() => { document.querySelector('.status-toggle[data-status=\"Failed\"]').click(); }");
        await Expect(status).ToHaveTextAsync(new Regex("^Filtered 1 of 30 scenarios"));

        // open
        await Toggle.ClickAsync();
        await Page.Locator("#filtering-box .status-toggle[data-status='Failed']").ClickAsync();
        await Expect(status).ToHaveTextAsync("All 30 scenarios, 3 features");

        // a bar too short for its readout
        await Toggle.ClickAsync();
        await Page.SetViewportSizeAsync(769, 700);
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync("() => { document.querySelector('.nav-row').classList.add('nav-minimal'); document.querySelector('.status-toggle[data-status=\"Failed\"]').click(); }");
        await Expect(status).ToHaveTextAsync(new Regex("^Filtered 1 of 30 scenarios"));
        Assert.NotEqual("none", await status.EvaluateAsync<string>("e => getComputedStyle(e).display"));
    }

    // F-l
    [Fact]
    public async Task A_link_with_filters_opens_with_the_box_closed_and_Filters_counting_them()
    {
        await OpenAsync("FilterBox_Link.html", hash: "#status=Failed&tags=smoke");

        Assert.False(await Shows("#filtering-box"));
        await Expect(Toggle).ToHaveAttributeAsync("aria-label", "Filters, 2 on");
    }

    // F-m
    [Fact]
    public async Task The_rows_lay_out_by_measure_the_first_time_the_box_opens()
    {
        await OpenAsync("FilterBox_Measure.html");

        await Toggle.ClickAsync();

        var row = Page.Locator(".filter-row");
        await Expect(row).ToHaveClassAsync(new Regex("\\bpair-fits\\b"));
        await Expect(row).ToHaveClassAsync(new Regex("\\btrio-fits\\b"));
    }

    // F-o
    [Fact]
    public async Task The_box_s_exports_export_what_they_did()
    {
        await OpenAsync("FilterBox_Export.html");
        await Toggle.ClickAsync();
        await Page.Locator("#filtering-box .status-toggle[data-status='Failed']").ClickAsync();

        var download = await Page.RunAndWaitForDownloadAsync(() => Page.Locator("#filtering-box .export-btn[onclick='export_csv()']").ClickAsync());
        var csv = await File.ReadAllTextAsync((await download.PathAsync())!);

        Assert.Contains("Scenario 2.5", csv);
        Assert.DoesNotContain("Scenario 1.1", csv);
        await Expect(Page.Locator("#filtering-box .export-btn[onclick='export_html()']")).ToBeVisibleAsync();
    }

    // 5.12.0 (the owner, 2026-10-27: "the filter bar should be completely visible INSIDE the filtering section, but the
    // filtering section should be hidden on load"): the field is back in the box, wholly in view once Filters opens it
    [Theory]
    [InlineData(1400)]
    [InlineData(390)]
    public async Task The_filter_field_is_in_the_box_and_wholly_in_view_once_Filters_opens_it(int width)
    {
        await OpenAsync($"FilterBox_Field{width}.html", width: width);
        Assert.False(await Shows("#filtering-box #searchbar"));

        await Toggle.ClickAsync();

        await Expect(Field).ToBeVisibleAsync();
        await Expect(Field).ToHaveAttributeAsync("placeholder", new Regex(@"^Filter\.\.\."));
        Assert.True(await Field.EvaluateAsync<bool>("""
            e => { const r = e.getBoundingClientRect(), b = document.getElementById('filtering-box').getBoundingClientRect();
                   return e.type === 'text' && r.width >= 160 && r.left >= b.left && r.right <= b.right && r.left >= 0 && r.right <= window.innerWidth
                       && document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2) === e; }
            """));
    }

    // as 5.10.1 did: typed, the field filters the Features list where it is, no Enter, no Search tab
    [Fact]
    public async Task The_filter_field_filters_as_it_is_typed()
    {
        await OpenAsync("FilterBox_FieldTyped.html");
        await Toggle.ClickAsync();
        await Field.ClickAsync();

        await Page.Keyboard.TypeAsync("@smoke");

        await Expect(NavStatus).ToHaveTextAsync(new Regex("^Filtered 9 of 30 scenarios"));
        await Expect(Page.Locator(".report-tabs > .report-tab[data-tab='features']")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.False(await Page.EvaluateAsync<bool>("() => document.getElementById('search-dialog').open"));
        await Expect(Field).ToBeFocusedAsync();
        // the field's text is a filter on, so Filters counts it, the box being mostly closed
        await Expect(Toggle).ToHaveAttributeAsync("aria-label", "Filters, 1 on");
    }

    // B2 for the field: text put in with no key (a paste with the mouse, a drop, an input method) filters at once
    [Fact]
    public async Task Text_put_in_the_filter_field_with_no_key_filters_without_leaving_it()
    {
        await OpenAsync("FilterBox_FieldInsert.html");
        await Toggle.ClickAsync();
        await Field.FocusAsync();

        await Page.Keyboard.InsertTextAsync("@smoke");

        await Expect(NavStatus).ToHaveTextAsync(new Regex("^Filtered 9 of 30 scenarios"));
        await Expect(Field).ToBeFocusedAsync();
    }

    // B1 for the field: Enter filters once (until 5.11.0 its keydown ran the filter and its keyup again)
    [Fact]
    public async Task Enter_in_the_filter_field_filters_once()
    {
        await OpenAsync("FilterBox_FieldEnter.html");
        await Toggle.ClickAsync();
        await Page.EvaluateAsync("() => { window.__runs = 0; const run = window.run_search_scenarios; window.run_search_scenarios = function () { window.__runs++; return run.apply(this, arguments); }; }");

        // keys typed, as a reader types them: Enter runs at once what the last key would have run 300 ms later, and a field
        // that ran on keyup (5.10.1) ran it again on Enter's own keyup (review: FillAsync types no key, so the fact could
        // not see it). The count starts at the last key, after the run the keys before it brought
        await Field.ClickAsync();
        await Field.PressSequentiallyAsync("toas");
        await Page.WaitForTimeoutAsync(600);
        await Page.EvaluateAsync("() => { window.__runs = 0; }");
        await Field.PressSequentiallyAsync("t");
        await Field.PressAsync("Enter");
        await Page.WaitForTimeoutAsync(600);

        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__runs"));
    }

    // 5.12.0 (review): the field's ? opens its syntax at the top of the box, and a pinned box is capped again, as when it
    // opens: until then the syntax took a pinned bar past 60% of the window, with the rows under it out of reach
    [Fact]
    public async Task The_field_s_syntax_opened_in_a_pinned_box_keeps_the_bar_within_60_percent_of_the_window()
    {
        await OpenAsync("FilterBox_HelpCap.html");
        await ScrollPastTheBar();
        await Toggle.ClickAsync();
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("", await Box.EvaluateAsync<string>("b => b.style.maxHeight"));

        await Page.Locator("#filtering-box .search-help-toggle").ClickAsync();
        await Expect(Page.Locator("#filtering-box .search-help-panel")).ToBeVisibleAsync();
        await TwoAnimationFramesAsync(Page);

        var bar = await Page.EvaluateAsync<double>("() => document.querySelector('.report-nav').getBoundingClientRect().height");
        var window = await Page.EvaluateAsync<double>("() => window.innerHeight");
        Assert.True(bar <= window * 0.6 + 1, $"the pinned bar is {bar} px tall in a {window} px window");
        Assert.True(await Box.EvaluateAsync<bool>("b => b.scrollHeight > b.clientHeight + 1"));
    }

    [Fact]
    public async Task The_question_mark_beside_the_field_shows_and_hides_the_syntax()
    {
        await OpenAsync("FilterBox_FieldHelp.html");
        await Toggle.ClickAsync();
        var help = Page.Locator("#filtering-box .search-help-panel");
        var toggle = Page.Locator("#filtering-box .search-help-toggle");

        await toggle.ClickAsync();
        await Expect(help).ToBeVisibleAsync();
        await Expect(help).ToContainTextAsync("tag:name");
        await Expect(help).ToContainTextAsync("dep:name");

        await toggle.ClickAsync();
        await Expect(help).ToBeHiddenAsync();
    }

    // Clear All empties the field with the rest of the filters, and a search applied stays as it was
    [Fact]
    public async Task Clear_All_empties_the_field_and_leaves_the_search()
    {
        await OpenAsync("FilterBox_FieldClear.html");
        await FillSearchBar("tag:smoke", toSearchTab: true);
        await Page.Locator(".report-tabs > .report-tab[data-tab='features']").ClickAsync();
        await Toggle.ClickAsync();
        await Field.FillAsync("Scenario 1.");
        await Expect(NavStatus).ToHaveTextAsync(new Regex("^Filtered"));

        await Page.Locator("#filtering-box .export-btn[onclick='clear_all_filters()']").ClickAsync();

        await Expect(Field).ToHaveValueAsync("");
        await Expect(NavStatus).ToHaveTextAsync("All 30 scenarios, 3 features");
        await Expect(Page.Locator(".search-button-query")).ToHaveTextAsync("tag:smoke");
    }
}
