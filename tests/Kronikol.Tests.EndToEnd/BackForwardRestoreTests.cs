using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Putting the place back (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md section 7.1, R1 to R14): the position Back lands
/// on in a long report in every engine, a tab opened again where the reader left it, the focus never left in a hidden
/// panel, a return from another page, a report inside a frame and inside an Azure DevOps style <c>&lt;iframe
/// srcdoc&gt;</c>, a page without tabs, the load order, the status line, the write budget WebKit enforces, and the defects
/// found on the way (S5, S6, S9, S11, S21).
/// </summary>
[Collection(PlaywrightCollections.Scenarios)]
public class BackForwardRestoreTests : BackForwardTestBase
{
    public BackForwardRestoreTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 700;

    private string EverySection(string fileName) => ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, fileName);

    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public Task R1_Back_lands_on_the_same_feature_far_down_a_long_report(string engine) => InEngineAsync(engine, async page =>
    {
        await OpenAsync(page, GenerateLongReport($"BackNav_R1_{engine}.html"));
        // a real interaction first: Firefox's Back does nothing on a page that has had none (plan fact 2)
        await page.Locator("h1").ClickAsync();
        var title = page.Locator("details.feature > summary", new() { HasTextString = "Feature 40" }).First;
        await title.ScrollIntoViewIfNeededAsync();
        await page.Mouse.MoveAsync(640, 400);
        await page.Mouse.WheelAsync(0, -120);
        await page.WaitForTimeoutAsync(600);
        var top = await title.EvaluateAsync<double>("s => s.getBoundingClientRect().top");

        await ActAsync(page, () => page.Locator("#tab-timeline").ClickAsync());
        await BackAsync(page);

        await StillInTheReportAsync(page, "Back from the Timeline tab");
        Assert.Equal("features", await SelectedTabAsync(page));
        Assert.InRange(await title.EvaluateAsync<double>("s => s.getBoundingClientRect().top") - top, -2, 2);
    });

    [Fact]
    public async Task R2_A_tab_clicked_again_opens_where_the_reader_left_it()
    {
        await Page.SetViewportSizeAsync(1280, 520);
        await OpenAsync(EverySection("BackNav_R2.html"));
        await ActAsync(() => Page.Locator("#tab-history").ClickAsync());
        await WheelAsync(220);
        var history = await Page.EvaluateAsync<double>("() => scrollY");
        Assert.True(history > 50, $"the History tab scrolls ({history})");

        await ActAsync(() => Page.Locator("#tab-features").ClickAsync());
        await WheelAsync(600);
        await ActAsync(() => Page.Locator("#tab-history").ClickAsync());

        Assert.InRange(await Page.EvaluateAsync<double>("() => scrollY") - history, -2, 2);
    }

    private const string FocusInHiddenPanel = "() => { const a = document.activeElement; return !!a && !!a.closest('[hidden]'); }";

    [Fact]
    public async Task R3_After_Back_or_Forward_the_focus_is_never_inside_a_hidden_panel()
    {
        await OpenAsync(EverySection("BackNav_R3.html"));
        await ActAsync(() => Page.Locator("#tab-history").ClickAsync());
        var link = Page.Locator("#history-section a.history-link").First;
        await link.FocusAsync();
        await ActAsync(() => Page.Locator("#tab-timeline").ClickAsync());

        await BackAsync();
        await StillInTheReportAsync("Back to the History tab");
        Assert.False(await Page.EvaluateAsync<bool>(FocusInHiddenPanel));
        // the Timeline tab was pressed to leave: the focus stays on the tab selected now
        Assert.Equal("#tab-history", await FocusedAsync());

        await ForwardAsync();
        Assert.Equal("timeline", await SelectedTabAsync());
        Assert.False(await Page.EvaluateAsync<bool>(FocusInHiddenPanel), "the History link the focus was on is hidden now");
    }

    [Fact]
    public async Task R4_Back_from_a_file_an_attachment_opened_puts_the_scenario_and_the_position_back()
    {
        var url = GenerateReportWithAttachments("BackNav_R4.html");
        await OpenAsync(url);
        var scenario = Page.Locator("details.scenario", new() { Has = Page.Locator("a.step-attachment") }).First;
        var sid = (await scenario.GetAttributeAsync("data-stable-id"))!;
        await OpenAsync(url, "#sid-" + sid);
        var file = Page.Locator("a.step-attachment").First;
        await file.ScrollIntoViewIfNeededAsync();
        await WaitForScrollToSettleAsync();
        var top = await file.EvaluateAsync<double>("a => a.getBoundingClientRect().top");

        await file.ClickAsync();
        // committed is enough: a text file's own load is not this test's, and on Linux Chromium it never came
        await Page.WaitForURLAsync(new Regex("files/"), new() { WaitUntil = WaitUntilState.Commit });
        await Page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Page.Locator("details.feature").First.WaitForAsync();
        await SettledAsync(Page, 0);

        Assert.True(await IsOpenAsync($"details.scenario[data-stable-id='{sid}']"));
        Assert.InRange(await file.EvaluateAsync<double>("a => a.getBoundingClientRect().top") - top, -2, 2);
    }

    [Fact]
    public async Task R5_In_a_frame_Back_goes_through_the_reports_places_then_leaves_the_embedding_page()
    {
        var url = EverySection("BackNav_R5.html");
        var embed = Path.Combine(TempDir, "embed.html");
        File.WriteAllText(embed, $"<!DOCTYPE html><html><head><title>Embedding page</title></head><body style=\"margin:0\"><iframe src=\"{url}\" style=\"width:1260px;height:680px;border:0\"></iframe></body></html>");
        await Page.GotoAsync(BeforePage());
        await Page.GotoAsync(new Uri(embed).AbsoluteUri);
        var frame = Page.Frames.First(f => f.Url.Contains("BackNav_R5"));
        await frame.Locator("details.feature").First.WaitForAsync();
        await frame.WaitForFunctionAsync("() => !!document.documentElement.getAttribute('data-kron-nav')", null, new() { PollingInterval = 200, Timeout = 30000 });

        await frame.Locator("#tab-timeline").ClickAsync();
        await frame.WaitForFunctionAsync("() => location.hash === '#tab=timeline'", null, new() { PollingInterval = 200, Timeout = 10000 });

        // Playwright's GoBack waits for a top-level navigation the frame's entry never makes (plan fact 10)
        await Page.EvaluateAsync("() => history.back()");
        await frame.WaitForFunctionAsync("() => document.querySelector('.report-tab[aria-selected=\"true\"]').dataset.tab === 'features'",
            null, new() { PollingInterval = 200, Timeout = 10000 });
        Assert.Equal("Embedding page", await Page.TitleAsync());

        await Page.EvaluateAsync("() => history.back()");
        await Page.WaitForFunctionAsync($"() => document.title === '{BeforeTitle}'", null, new() { PollingInterval = 200, Timeout = 10000 });
    }

    private string Tabless(string fileName)
    {
        // no durations, no component diagram, no history: a page with nothing beside its features has no tabs
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios = [.. Enumerable.Range(1, 6).Select(i => new Scenario
                {
                    Id = "t" + i, DisplayName = "Pay " + i, Result = i == 2 ? ExecutionResult.Failed : ExecutionResult.Passed,
                    ErrorMessage = i == 2 ? "declined" : null, Tags = [i % 2 == 0 ? "even" : "odd"],
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "it pays", Status = i == 2 ? ExecutionResult.Failed : ExecutionResult.Passed }]
                })]
            }
        };
        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, fileName), "Tabless Report", true, diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    [Fact]
    public async Task R6_Without_tabs_a_chip_and_a_link_button_go_back_as_they_do_with_them()
    {
        await OpenAsync(Tabless("BackNav_R6.html"));
        Assert.Equal(0, await Page.Locator(".report-tabs").CountAsync());
        await OpenFilterRowsAsync();
        await Page.Locator(".filtering-box .tag-toggle[data-tag='odd']").ClickAsync();
        Assert.Equal("#tags=odd", await HashSoonAsync("#tags=odd"));

        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(f => f.open = true)");
        var scenario = Page.Locator("details.scenario").First;
        var id = (await scenario.GetAttributeAsync("id"))!;
        await ActAsync(() => scenario.Locator(":scope > summary a.scenario-link").ClickAsync());
        Assert.Equal($"#{id}&tags=odd", await HashSoonAsync($"#{id}&tags=odd"));

        await BackAsync();
        await StillInTheReportAsync("Back from the link button");
        Assert.Equal("#tags=odd", await HashAsync());
        await BackAsync();
        Assert.Equal("", await HashAsync());
        Assert.Equal("off", await Page.EvaluateAsync<string>("() => chip_state(find_chip('tag', 'odd'))"));
    }

    [Fact]
    public async Task R6_An_exported_page_goes_back_through_its_own_links()
    {
        // An exported Filtered HTML page is the head, a title and the features the filters left: no tabs, no bar and no
        // filters, so its scenarios' link buttons are what it navigates by, with the module its head copies (C1)
        await OpenAsync(GenerateLongReport("BackNav_R6_Export.html", "Scenario 02.1", "Scenario 03.2"));
        await OpenFilterRowsAsync();
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        var download = await Page.RunAndWaitForDownloadAsync(() =>
            Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" }).ClickAsync());
        var export = Path.Combine(TempDir, "export_backnav.html");
        await download.SaveAsAsync(export);

        var errors = new List<string>();
        Page.PageError += (_, e) => errors.Add(e);
        await OpenAsync(new Uri(export).AbsoluteUri);
        Assert.Equal(0, await Page.Locator(".filtering-box, .report-tabs").CountAsync());
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(f => f.open = true)");
        // a failing scenario, which the filter kept: the copy holds each feature whole, with the scenarios it hid still hidden
        var scenario = Page.Locator("details.scenario", new() { HasTextString = "Scenario 03.2" }).First;
        var id = (await scenario.GetAttributeAsync("id"))!;
        await ActAsync(() => scenario.Locator(":scope > summary a.scenario-link").ClickAsync());
        Assert.Equal("#" + id, await HashAsync());

        await BackAsync();
        await StillInTheReportAsync("Back in an exported page");
        Assert.Equal("", await HashAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task R7_With_the_Timeline_as_the_start_tab_a_sid_address_shows_its_scenario_clear_of_the_bar()
    {
        var url = ReportTestHelper.GenerateComponentTimelineToggleDefaultsReport(TempDir, OutputDir, "BackNav_R7.html",
            o => o.TestRunReportToggleDefaults.ScenarioTimelineVisible = true);
        await OpenAsync(url);
        Assert.Equal("timeline", await SelectedTabAsync());
        var sid = (await Page.Locator("details.scenario").Last.GetAttributeAsync("data-stable-id"))!;

        await OpenAsync(url, "#sid-" + sid);
        await WaitForScrollToSettleAsync();
        Assert.Equal("features", await SelectedTabAsync());
        var gap = await Page.EvaluateAsync<double>($$"""
            () => {
                const s = document.querySelector("details.scenario[data-stable-id='{{sid}}'] > summary").getBoundingClientRect();
                const bar = document.querySelector('.toolbar-row.report-nav').getBoundingClientRect();
                return s.top - bar.bottom;
            }
            """);
        Assert.True(gap >= -1, $"the scenario's title is {-gap} px under the bar");
        Assert.True(await SummaryIsOnScreenAsync($"details.scenario[data-stable-id='{sid}']"));
    }

    private string TwoOutlinesOfOneName(string fileName, bool failing = false)
    {
        Scenario Example(string id, string tier, ExecutionResult result) => new()
        {
            Id = id, DisplayName = "Discount applies", Result = result, Duration = TimeSpan.FromSeconds(1), OutlineId = "discount",
            ErrorMessage = result == ExecutionResult.Failed ? "the discount was not applied" : null,
            ExampleValues = new Dictionary<string, string> { ["tier"] = tier },
            ExampleFlatValues = new Dictionary<string, string> { ["tier"] = tier },
            Steps = [new ScenarioStep { Keyword = "Then", Text = "the discount applies", Status = result }]
        };
        var bad = failing ? ExecutionResult.Failed : ExecutionResult.Passed;
        var features = new[]
        {
            new Feature { DisplayName = "Pricing", Scenarios = [Example("p1", "gold", ExecutionResult.Passed), Example("p2", "silver", bad)] },
            new Feature { DisplayName = "Promotions", Scenarios = [Example("q1", "gold", bad), Example("q2", "bronze", ExecutionResult.Passed)] }
        };
        const string source = "@startuml\nactor \"Caller\" as caller\nparticipant \"Service\" as svc\ncaller -> svc : GET /api/test\nsvc --> caller : 200 OK\n@enduml\n";
        var diagrams = features.SelectMany(f => f.Scenarios).Select(s => new DiagramAsCode(s.Id, "", source)).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(diagrams, features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, fileName), "Outlines Report", true, diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs, groupParameterizedTests: true);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    [Fact]
    public async Task R8_Each_of_two_groups_of_one_name_is_reached_by_its_own_link()
    {
        await OpenAsync(TwoOutlinesOfOneName("BackNav_R8.html"));
        var groups = Page.Locator("details.scenario-parameterized");
        Assert.Equal(2, await groups.CountAsync());
        Assert.NotEqual(await groups.Nth(0).GetAttributeAsync("id"), await groups.Nth(1).GetAttributeAsync("id"));

        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(f => f.open = true)");
        await ActAsync(() => groups.Nth(1).Locator(":scope > summary a.scenario-link").ClickAsync());

        Assert.True(await Page.EvaluateAsync<bool>("""
            () => document.getElementById(location.hash.substring(1).split('&')[0]) === document.querySelectorAll('details.scenario-parameterized')[1]
            """), "the second group's link names the second group");
    }

    [Fact]
    public async Task R9_A_failure_clusters_link_to_an_example_row_shown_flat_selects_the_row_shown()
    {
        await OpenAsync(TwoOutlinesOfOneName("BackNav_R9.html", failing: true));
        await Page.EvaluateAsync("() => document.querySelectorAll('.failure-clusters, .failure-clusters details').forEach(d => d.open = true)");
        var link = Page.Locator("a.failure-cluster-scenario-link").First;
        await Expect(link).ToBeVisibleAsync();
        var href = (await link.GetAttributeAsync("href"))!;

        await ActAsync(() => link.ClickAsync());

        // The row selected is the link's own, in the copy on display: the flat table's row with the stable id of the grouped
        // row the link names (only the grouped copy has the id). It is not a table's first row, which each table starts with
        // selected, so the default selection cannot pass for it; 5.1.0's link selected the hidden grouped copy.
        Assert.Equal("ok", await Page.EvaluateAsync<string>("""
            href => {
                const named = document.getElementById(href.substring(1));
                const sid = named && named.getAttribute('data-stable-id');
                if (!sid) return 'the link names no row';
                const shown = Array.from(document.querySelectorAll('tr[data-stable-id="' + sid + '"]')).filter(r => r.getClientRects().length > 0);
                if (shown.length !== 1) return 'copies on display: ' + shown.length;
                const row = shown[0], box = row.getBoundingClientRect();
                if (!row.closest('table.param-table-flat')) return 'not the flat table';
                if (row.closest('table').querySelector('tbody tr') === row) return 'the first row: the test cannot tell';
                if (!row.classList.contains('row-active')) return 'not selected';
                return box.top >= 0 && box.bottom <= innerHeight ? 'ok' : 'off screen at ' + Math.round(box.top);
            }
            """, href));
    }

    [Fact]
    public async Task R10_The_arrow_keys_on_a_tab_leave_the_hidden_scenarios_alone()
    {
        await OpenAsync(EverySection("BackNav_R10.html"));
        await ActAsync(() => Page.Locator("#tab-timeline").ClickAsync());
        await Page.Locator("#tab-timeline").FocusAsync();

        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("ArrowDown");
        await TwoAnimationFramesAsync(Page);

        Assert.Equal(0, await Page.Locator(".scenario-focused").CountAsync());
        Assert.Equal("timeline", await SelectedTabAsync());
    }

    [Theory]
    [InlineData("chromium")]
    [InlineData("webkit")]
    [InlineData("firefox")]
    public async Task R11_In_an_iframe_srcdoc_the_address_is_written_the_search_opens_its_match_and_Back_undoes_a_filter(string engine)
    {
        // How the Azure DevOps viewer extensions show an artifact: a relative address resolves against the embedding page
        // there, so every write until 5.2.0 threw SecurityError and stopped the search before it opened its match (S21).
        var url = GenerateLongReport($"BackNav_R11_{engine}.html", "Scenario 02.1");
        var html = File.ReadAllText(new Uri(url).LocalPath);
        var parent = Path.Combine(TempDir, $"viewer_{engine}.html");
        File.WriteAllText(parent, "<!DOCTYPE html><html><head><title>Viewer</title></head><body style=\"margin:0\"><iframe style=\"width:1260px;height:680px;border:0\" srcdoc=\""
            + System.Net.WebUtility.HtmlEncode(html) + "\"></iframe></body></html>");
        await using var server = LoopbackFileServer.Start(TempDir);

        await InEngineAsync(engine, async page =>
        {
            var errors = new List<string>();
            page.PageError += (_, e) => errors.Add(e);
            await page.GotoAsync(server.UrlOf($"viewer_{engine}.html"));
            var frame = page.Frames.First(f => f != page.MainFrame);
            await frame.Locator("details.feature").First.WaitForAsync(new() { Timeout = 60000 });
            await frame.WaitForFunctionAsync("() => !!document.documentElement.getAttribute('data-kron-nav')", null, new() { PollingInterval = 200, Timeout = 30000 });
            await frame.Locator(".filtering-box .more-filters").ClickAsync();
            await frame.Locator(".status-toggle[data-status='Failed']").ClickAsync();
            await frame.Locator("#searchbar").FillAsync("Scenario 02.1");
            await frame.Locator("#searchbar").PressAsync("Enter");

            var opened = frame.Locator("details.scenario", new() { HasTextString = "Scenario 02.1" }).First;
            await frame.WaitForFunctionAsync("() => [...document.querySelectorAll('details.scenario')].some(s => s.open && s.textContent.includes('Scenario 02.1'))",
                null, new() { PollingInterval = 200, Timeout = 10000 });
            Assert.Empty(errors);
            if (engine == "firefox") return;   // under an http parent Firefox forbids the write: no entry, and nothing thrown
            Assert.Equal("about:srcdoc#q=Scenario%2002.1&status=Failed", await frame.EvaluateAsync<string>("() => location.href"));

            await page.EvaluateAsync("() => history.back()");
            await frame.WaitForFunctionAsync("() => location.hash === '#status=Failed'", null, new() { PollingInterval = 200, Timeout = 10000 });
            Assert.True(await opened.IsVisibleAsync());
        });
    }

    [Fact]
    public async Task R12_The_status_line_names_the_tab_Back_brought_and_says_nothing_after_a_click()
    {
        await OpenAsync(EverySection("BackNav_R12.html"));
        await ActAsync(() => Page.Locator("#tab-history").ClickAsync());
        await ActAsync(() => Page.Locator("#tab-timeline").ClickAsync());
        var status = Page.Locator("[role='status'].kron-nav-status");
        Assert.Equal("", await status.TextContentAsync());

        await BackAsync();
        await StillInTheReportAsync("Back to the History tab");
        Assert.Equal("History tab", await status.TextContentAsync());

        await ActAsync(() => Page.Locator("#tab-components").ClickAsync());
        Assert.Equal("", await status.TextContentAsync());
    }

    [Fact]
    public async Task R13_In_WebKit_a_hundred_and_fifty_clicks_on_one_chip_and_a_held_arrow_key_throw_nothing()
    {
        // WebKit throws SecurityError from the 101st write within ten seconds; Playwright switches Chromium's and Firefox's
        // limits off, so WebKit is the one engine a breach of the write budget shows in (plan fact 7)
        await InEngineAsync("webkit", async page =>
        {
            var errors = new List<string>();
            page.PageError += (_, e) => errors.Add(e);
            await OpenAsync(page, GenerateLongReport("BackNav_R13.html"));
            await page.Locator(".filtering-box .more-filters").ClickAsync();
            var chip = page.Locator(".filtering-box .tag-toggle[data-tag='area-03']");
            // one click, then 150 in one task: a burst no reader's clicks reach in Playwright's time, past WebKit's limit
            // unless the page writes within its budget, and the address then holds the chip's last state (151 clicks: in)
            await chip.ClickAsync();
            await page.EvaluateAsync("() => { const c = document.querySelector(\".filtering-box .tag-toggle[data-tag='area-03']\"); for (let i = 0; i < 150; i++) c.click(); }");
            await page.Locator("#tab-features").FocusAsync();
            for (var i = 0; i < 60; i++) await page.Keyboard.PressAsync("ArrowRight");
            var selected = await SelectedTabAsync(page);

            Assert.Empty(errors);
            await page.WaitForFunctionAsync("t => location.hash === '#tags=area-03' + (t === 'features' ? '' : '&tab=' + t) || location.hash === '#tab=' + t + '&tags=area-03'",
                selected, new() { PollingInterval = 200, Timeout = 15000 });
        });
    }

    [Fact]
    public async Task R15_A_link_to_a_scenario_taller_than_the_window_shows_its_title_under_the_bar()
    {
        // 5.2.0: every link centred its scenario but a failure cluster's, which went to its top; one taller than the window
        // had its title above it. Now centred, or from its top when it is taller than the window below the bar.
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios = [.. Enumerable.Range(1, 3).Select(i => new Scenario
                {
                    Id = "tall" + i, DisplayName = "Pay " + i, Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(20 * i),
                    Steps = [.. Enumerable.Range(1, i == 2 ? 150 : 2).Select(s => new ScenarioStep { Keyword = "And", Text = $"step {s} passes", Status = ExecutionResult.Passed })]
                })]
            }
        };
        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, "BackNav_R15.html"), "Tall Report", true, diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, "BackNav_R15.html"), true);
        var url = new Uri(path).AbsoluteUri;
        await OpenAsync(url);
        var tall = Page.Locator("details.scenario", new() { HasTextString = "Pay 2" }).First;
        var sid = (await tall.GetAttributeAsync("data-stable-id"))!;
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature, details.scenario').forEach(d => d.open = true)");
        Assert.True(await tall.EvaluateAsync<double>("d => d.getBoundingClientRect().height") > ViewportHeight,
            "the scenario is taller than the window");

        await ActAsync(() => Page.GotoAsync(url + "#sid-" + sid));
        await WaitForScrollToSettleAsync();

        var gap = await Page.EvaluateAsync<double>($$"""
            () => {
                const s = document.querySelector("details.scenario[data-stable-id='{{sid}}'] > summary").getBoundingClientRect();
                const bar = document.querySelector('.toolbar-row.report-nav');
                return s.top - (bar ? bar.getBoundingClientRect().bottom : 0);
            }
            """);
        Assert.True(gap >= -1 && gap < 80, $"the title is {gap} px below the bar's edge");
    }

    [Fact]
    public async Task R14_The_search_writes_at_once_on_Enter_so_Back_right_after_it_undoes_the_search()
    {
        await OpenAsync(GenerateLongReport("BackNav_R14.html"));
        await Page.Locator("#searchbar").PressSequentiallyAsync("Scenario 07");
        await Page.Locator("#searchbar").PressAsync("Enter");

        await BackAsync();
        await StillInTheReportAsync("Back right after Enter");
        Assert.Equal("", await Page.Locator("#searchbar").InputValueAsync());
        Assert.Equal("", await HashAsync());
    }

    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public Task R16_Back_lands_on_the_same_scenario_after_the_features_above_it_were_drawn(string engine) => InEngineAsync(engine, async page =>
    {
        // A link far down a long report lands with every scenario above it undrawn, each counted at its placeholder height
        // (content-visibility: auto). Scrolled up through, they are drawn at their own, so the pixel position the entry
        // saved no longer holds the scenario: Back finds it by its element and its distance below the bar (plan fact 4).
        // The features are opened for it: from 5.2.1 a feature card is laid out even far from the screen (the sort places
        // the cards in a flex column), so a closed one no longer counts at a placeholder's height
        var url = GenerateLongReport($"BackNav_R16_{engine}.html");
        await OpenAsync(page, url);
        var sid = (await page.Locator("details.scenario", new() { HasTextString = "Scenario 40.1" }).First.GetAttributeAsync("data-stable-id"))!;
        await OpenAsync(page, url, "#sid-" + sid);
        // a real interaction first, on the page's margin, which moves nothing: Firefox's Back does nothing on a page that
        // has had none (plan fact 2)
        await page.Mouse.ClickAsync(4, 400);
        // opening every feature adds no entry to the session history
        await page.Locator(".fold-btn.fold-expand[data-fold='features']").First.ClickAsync();
        var scenario = page.Locator($"details.scenario[data-stable-id='{sid}']");
        var link = page.Locator("details.scenario", new() { HasTextString = "Scenario 40.2" }).First.Locator(":scope > summary a.scenario-link");
        await InViewAndStillAsync(page, link);
        // where the page stands as the link is pressed: a link the bar covers is scrolled into the open by the click
        // itself, after any reading made before it, and Back rightly returns to where the click was
        await scenario.EvaluateAsync(
            "s => addEventListener('pointerdown', () => { window.__atClick = { top: s.getBoundingClientRect().top, y: scrollY }; }, { capture: true, once: true })");

        await ActAsync(page, () => link.ClickAsync());
        var top = await page.EvaluateAsync<double>("() => window.__atClick.top");
        var before = await page.EvaluateAsync<double>("() => window.__atClick.y");
        // up through every feature above, half a window at a time, two frames for each, so they are drawn
        for (var step = 0; step < 1000; step++)
        {
            var y = await page.EvaluateAsync<double>("() => { scrollTo(0, Math.max(0, scrollY - innerHeight / 2)); return scrollY; }");
            await TwoAnimationFramesAsync(page);
            if (y <= 0) break;
        }

        await BackAsync(page);
        await StillInTheReportAsync(page, "Back after the features above were drawn");
        Assert.InRange(await scenario.EvaluateAsync<double>("s => s.getBoundingClientRect().top") - top, -2, 2);
        // the features drawn moved the scenario up the page, so the pixel position alone would land elsewhere
        Assert.True(before - await page.EvaluateAsync<double>("() => scrollY") > 200,
            $"the scenario moved up the page as the features above it were drawn ({before} then {await page.EvaluateAsync<double>("() => scrollY")})");
    });
}
