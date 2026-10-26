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
/// box shows the applied search with a clear of its own.
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

    // F-p
    [Fact]
    public async Task The_box_shows_the_applied_search_and_its_clear_clears_only_the_search()
    {
        await OpenAsync("FilterBox_Search.html");
        await Toggle.ClickAsync();
        await Page.Locator("#filtering-box .status-toggle[data-status='Passed']").ClickAsync();
        await Page.EvaluateAsync("() => { document.getElementById('searchbar').value = 'scenario 1.'; run_search_scenarios(); }");

        var line = Page.Locator("#filtering-box .box-search");
        await Expect(line).ToBeVisibleAsync();
        await Expect(line).ToContainTextAsync("scenario 1.");

        await line.Locator(".box-search-clear").ClickAsync();
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
        await Expect(line).ToBeHiddenAsync();
        await Expect(Page.Locator("#filtering-box .status-toggle[data-status='Passed']")).ToHaveClassAsync(new Regex("\\bstatus-active\\b"));
    }
}
