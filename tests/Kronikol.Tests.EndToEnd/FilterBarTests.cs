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
        await Page.GotoAsync(Generate(fileName) + hash);
        await Page.Locator("details.feature").First.WaitForAsync();
        await TwoAnimationFramesAsync(Page);
    }

    private ILocator More => Page.Locator(".filtering-box .more-filters");
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

    [Fact]
    public async Task More_filters_opens_and_folds_the_rows_on_a_desktop()
    {
        await OpenAsync("FilterBar_Fold.html");
        var passed = Page.Locator(".status-toggle[data-status='Passed']");
        await Expect(passed).ToBeHiddenAsync();
        await Expect(More).ToHaveAttributeAsync("aria-expanded", "false");

        await More.ClickAsync();
        await Expect(passed).ToBeVisibleAsync();
        await Expect(More).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(More).ToHaveAttributeAsync("title", "Fold the filters away");

        await More.ClickAsync();
        await Expect(passed).ToBeHiddenAsync();
        await Expect(More).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task Folded_More_filters_counts_the_filters_on_in_its_rows()
    {
        await OpenAsync("FilterBar_Count.html", "#status=Passed&tags=smoke");
        var count = Page.Locator(".more-filters-count");
        await Expect(count).ToHaveTextAsync("2");
        await Expect(count).ToBeVisibleAsync();
        await Expect(More).ToHaveAttributeAsync("aria-label", "More filters, 2 on");
        await Expect(More).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bhas-on\\b"));
        await Expect(More).ToHaveAttributeAsync("title", "2 filters here are on: show them");

        // open, the count goes; a filter set and the rows folded again, it counts that one too
        await More.ClickAsync();
        await Expect(count).ToBeHiddenAsync();
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        await More.ClickAsync();
        await Expect(count).ToHaveTextAsync("3");

        // Clear All from the header: nothing is on
        await Page.Locator(".filtering-box-export .export-btn", new() { HasTextString = "Clear All" }).ClickAsync();
        await Expect(count).ToBeHiddenAsync();
        await Expect(More).ToHaveAttributeAsync("aria-label", "More filters");
        await Expect(More).ToHaveAttributeAsync("title", "Status, duration, happy paths, tags and dependencies");
    }

    [Fact]
    public async Task The_search_help_opens_while_the_rows_are_folded()
    {
        await OpenAsync("FilterBar_Help.html");
        await Page.Locator(".search-help-toggle").ClickAsync();
        await Expect(Page.Locator(".search-help-panel")).ToBeVisibleAsync();
        await Expect(Page.Locator(".status-toggle").First).ToBeHiddenAsync();
        await Page.Locator(".search-help-toggle").ClickAsync();
        await Expect(Page.Locator(".search-help-panel")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task A_phone_keeps_its_Filters_toggle_and_has_no_More_filters()
    {
        await OpenAsync("FilterBar_Phone.html", width: 390, height: 844);
        await Expect(More).ToBeHiddenAsync();
        await Expect(Page.Locator(".nav-filters")).ToBeHiddenAsync();
        await Page.Locator(".mobile-filter-toggle").ClickAsync();
        await Expect(Page.Locator(".status-toggle").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_bar_copies_the_readout_and_offers_Clear_All_while_filtered()
    {
        await OpenAsync("FilterBar_Readout.html");
        var longReadout = Page.Locator(".nav-readout-long");
        await Expect(longReadout).ToHaveTextAsync("All 30 scenarios, 3 features");
        await Expect(Page.Locator(".nav-clear")).ToBeHiddenAsync();

        await Page.Locator("#searchbar").FillAsync("@smoke");
        await Page.Locator("#searchbar").DispatchEventAsync("keyup");
        await Expect(longReadout).ToHaveTextAsync("Filtered 9 of 30 scenarios, 3 of 3 features");
        await Expect(Page.Locator(".nav-readout-short")).ToHaveTextAsync("Filtered 9 of 30");
        await Expect(Toggle).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-active\\b"));

        await Page.Locator(".nav-clear").ClickAsync();
        await Expect(longReadout).ToHaveTextAsync("All 30 scenarios, 3 features");
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
        await Expect(Page.Locator(".nav-clear")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Filters_shows_while_the_bar_floats_and_takes_the_box_into_it()
    {
        await OpenAsync("FilterBar_Ride.html");
        await Expect(Toggle).ToBeHiddenAsync();

        await ScrollPastTheBar();
        await Expect(Toggle).ToBeVisibleAsync();
        // what is just below the bar, which opening and closing keep where it is
        const string below = "() => { const r = document.querySelector('.report-nav').getBoundingClientRect(); const e = document.elementFromPoint(r.left + r.width / 2, r.bottom + 8); return e ? Math.round(e.getBoundingClientRect().top - r.bottom) + ' ' + (e.id || e.className) : ''; }";
        var reading = await Settled(below);
        await Toggle.ClickAsync();
        await Expect(Page.Locator(".report-nav > #filtering-box")).ToHaveCountAsync(1);
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.Equal(reading, await Settled(below));
        await Expect(Page.Locator(".report-nav")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bhas-filters\\b"));
        await Expect(Page.Locator("#searchbar")).ToBeFocusedAsync();
        await Expect(Page.Locator(".filters-close")).ToBeVisibleAsync();

        // Esc puts it back above the bar, and focus on Filters
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator(".report-nav > #filtering-box")).ToHaveCountAsync(0);
        Assert.True(await Page.EvaluateAsync<bool>("() => !!(document.getElementById('filtering-box').compareDocumentPosition(document.querySelector('.report-nav')) & Node.DOCUMENT_POSITION_FOLLOWING)"));
        await Expect(Toggle).ToBeFocusedAsync();
        await Expect(Page.Locator(".filters-close")).ToBeHiddenAsync();
        // the bar still floats over what was being read (the preview corrected once, which left it at rest, the box above)
        Assert.Equal(reading, await Settled(below));

        // and the close does too
        await Toggle.ClickAsync();
        await Page.Locator(".filters-close").ClickAsync();
        await Expect(Page.Locator(".report-nav > #filtering-box")).ToHaveCountAsync(0);
        await Expect(Toggle).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task The_box_goes_back_when_the_bar_comes_to_rest()
    {
        await OpenAsync("FilterBar_Rest.html");
        await ScrollPastTheBar();
        await Toggle.ClickAsync();
        await Expect(Page.Locator(".report-nav > #filtering-box")).ToHaveCountAsync(1);

        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await Expect(Page.Locator(".report-nav > #filtering-box")).ToHaveCountAsync(0);
        await Expect(Toggle).ToBeHiddenAsync();
    }

    [Fact]
    public async Task No_scenario_left_shows_the_empty_state_whose_Clear_All_returns_to_the_search()
    {
        await OpenAsync("FilterBar_Empty.html");
        var empty = Page.Locator(".filters-empty");
        await Expect(empty).ToBeHiddenAsync();

        await Page.Locator("#searchbar").FillAsync("nothing-matches-this");
        await Page.Locator("#searchbar").DispatchEventAsync("keyup");
        await Expect(empty).ToBeVisibleAsync();
        await Expect(empty).ToContainTextAsync("No scenarios match the filters.");

        await Page.Locator(".filters-empty-clear").ClickAsync();
        await Expect(empty).ToBeHiddenAsync();
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
        await Expect(Page.Locator("#searchbar")).ToBeFocusedAsync();
    }

    // A page with nothing beside its features has no report bar (here a run without durations, as the Specifications
    // report is where no scenario recorded one), and its empty state answered the filters only through the bar, so it
    // never showed (S14, found by the review of the wiki's 5.0.0 pages)
    [Fact]
    public async Task On_a_page_without_the_bar_no_scenario_left_still_shows_the_empty_state()
    {
        await Page.SetViewportSizeAsync(1400, 700);
        await Page.GotoAsync(Generate("FilterBar_Empty_NoBar.html", durations: false));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Expect(Page.Locator(".toolbar-row.report-nav")).ToHaveCountAsync(0);
        var empty = Page.Locator(".filters-empty");
        await Expect(empty).ToBeHiddenAsync();

        await FillSearchBar("nothing-matches-this");
        await Expect(empty).ToBeVisibleAsync();
        await Expect(empty).ToContainTextAsync("No scenarios match the filters.");

        await Page.Locator(".filters-empty-clear").ClickAsync();
        await Expect(empty).ToBeHiddenAsync();
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
        await Expect(Page.Locator("#searchbar")).ToBeFocusedAsync();
    }

    // the report reads the filters from the link before the filter bar's script starts, so a link that leaves no scenario
    // has already answered when the empty state is wired: the page opens on it, with the bar and without it
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_link_whose_filters_leave_no_scenario_opens_on_the_empty_state(bool bar)
    {
        await Page.SetViewportSizeAsync(1400, 700);
        await Page.GotoAsync(Generate(bar ? "FilterBar_Empty_Link.html" : "FilterBar_Empty_Link_NoBar.html", durations: bar) + "#q=nothing-matches-this");
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("nothing-matches-this");
        await Expect(Page.Locator(".toolbar-row.report-nav")).ToHaveCountAsync(bar ? 1 : 0);
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
        await More.ClickAsync();
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
        await page.GotoAsync(Generate("FilterBar_Firefox_Status.html"));
        await page.Locator("details.feature").First.WaitForAsync();
        await page.Locator(".filtering-box .more-filters").ClickAsync();
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

    [Fact]
    public async Task The_deep_search_chip_sits_inside_the_search_field()
    {
        await OpenAsync("FilterBar_Chip.html");
        await Page.Locator("#searchbar").FillAsync("zebra");
        await Page.Locator("#searchbar").DispatchEventAsync("keyup");
        await Page.WaitForFunctionAsync("() => !!document.querySelector('.kron-deep-chip')", null, new() { PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector('.kron-deep-chip').parentElement.classList.contains('search-field')"));
    }
}
