using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Focus the keyboard moves is never left under the report bar (5.0.0, plans/OPTION_C_PLAN.md S12, D48; WCAG 2.2
/// 2.4.11 Focus Not Obscured): Tab to a link under a scenario's settings, stuck under the pinned bar, brings it 12 px
/// below them, and Tab to a scenario cut by the foot of the window brings it 12 px above the foot; Shift+Tab to a scenario
/// half under the bar ends below it, in Firefox too, which scrolls nothing for a control that shows in part; the search,
/// cut by the top of the window while the bar rests, comes 12 px in; and a scenario a block pushes away in the second
/// after Tab is brought back. Left where they are: a scenario taller than the window that Tab reaches, the page under a
/// click (the owner's round 17 item 20: clicking in and out of a diagram scrolled it), the bar's own controls, the gear
/// of settings stuck under the bar, the internal-flow popup, a control above the bar at rest, and a phone's page, whose
/// bar does not stick. Keys are real key presses; a control a fact starts from is given the focus by script, with no
/// scroll, which the page does not move.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class FocusClearOfTheBarTests : PlaywrightTestBase
{
    public FocusClearOfTheBarTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1400;
    protected override int ViewportHeight => 900;

    // a sequence diagram whose second message is a link, as an internal-flow arrow's label is
    private const string LinkedSource =
        "@startuml\nactor \"Caller\" as caller\nparticipant \"Orders\" as orders\ncaller -> orders : POST /orders\n"
        + "orders --> caller : [[#d48-link 201 Created]]\ncaller -> orders : GET /orders/1\norders --> caller : 200 OK\n@enduml";

    /// <summary>
    /// Twelve features of four scenarios each ("Scenario 01.1" to "Scenario 12.4"), timed, so the page has the report bar
    /// and runs on well past it; every feature starts open. Each scenario has two steps and Scenario 02.2 sixty, so that
    /// open it is taller than the window. With <paramref name="diagram"/>, Scenario 01.1 has a sequence diagram whose
    /// second message is a link, and so its own settings; with <paramref name="flow"/>, the page has the internal-flow
    /// popup.
    /// </summary>
    private string Generate(string fileName, bool diagram = false, bool flow = false)
    {
        static ScenarioStep Step(string keyword, string text) => new() { Keyword = keyword, Text = text, Status = ExecutionResult.Passed };
        var features = Enumerable.Range(1, 12).Select(f => new Feature
        {
            DisplayName = $"Feature {f:00}",
            Scenarios = Enumerable.Range(1, 4).Select(s => new Scenario
            {
                Id = $"f{f}s{s}", DisplayName = $"Scenario {f:00}.{s}", Result = ExecutionResult.Passed,
                Duration = TimeSpan.FromMilliseconds(10 * s + f),
                Steps = f == 2 && s == 2
                    ? Enumerable.Range(1, 60).Select(i => Step("And", $"step {i} of a long scenario")).ToArray()
                    : new[] { Step("Given", "the oven is hot"), Step("Then", "it bakes") }
            }).ToArray()
        }).ToArray();
        var diagrams = diagram
            ? new[] { new DefaultDiagramsFetcher.DiagramAsCode("f1s1", "", LinkedSource) }
            : Array.Empty<DefaultDiagramsFetcher.DiagramAsCode>();
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: flow, toggleDefaults: ResolvedToggleDefaults.BuiltIn with { FeaturesExpanded = true });
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    // the time of the last focus, or of the last mark, which WaitPastTheWatchAsync waits a second past
    private const string TrackFocus =
        "() => { window.__d48At = performance.now(); window.addEventListener('focusin', () => { window.__d48At = performance.now(); }, true); }";

    private async Task OpenAsync(string url)
    {
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync(TrackFocus);
    }

    private ILocator Bar => Page.Locator(".toolbar-row.report-nav");

    // a scenario by its number, "05-2" for Scenario 05.2
    private ILocator Scenario(string number) => Page.Locator($"#scenario-scenario-{number}");

    // the last control in a closed scenario's heading: Tab from it reaches the next scenario
    private static ILocator LinkIcon(ILocator scenario) => scenario.Locator(":scope > summary > a.scenario-link");

    private static Task FocusQuietlyAsync(ILocator control) => control.EvaluateAsync("e => e.focus({ preventScroll: true })");

    private static Task<bool> HasFocusAsync(ILocator control) => control.EvaluateAsync<bool>("e => e === document.activeElement");

    private static Task<double> ScrollYAsync(IPage page) => page.EvaluateAsync<double>("() => window.scrollY");

    private static async Task<(double Top, double Bottom)> BoxAsync(ILocator element)
    {
        var r = await element.EvaluateAsync<double[]>("e => { const r = e.getBoundingClientRect(); return [r.top, r.bottom]; }");
        return (r[0], r[1]);
    }

    /// <summary>
    /// Waits until 1.1 s have passed since the last focus or mark (<see cref="TrackFocus"/>): the page keeps a control in
    /// view for a second after Tab moves focus to it, so whatever it moves has moved by then.
    /// </summary>
    private static Task WaitPastTheWatchAsync(IPage page) =>
        page.WaitForFunctionAsync("() => performance.now() - window.__d48At > 1100", null,
            new() { PollingInterval = 200, Timeout = 15000 });

    private static Task MarkAsync(IPage page) => page.EvaluateAsync("() => { window.__d48At = performance.now(); }");

    /// <summary>
    /// Scrolls the page until <paramref name="element"/>'s top is <paramref name="top"/> px from the top of the window and
    /// stays there over two frames: a feature drawn as it comes into view changes the heights below it.
    /// </summary>
    private static async Task PlaceAsync(IPage page, ILocator element, double top)
    {
        for (var i = 0; i < 12; i++)
        {
            var (at, _) = await BoxAsync(element);
            if (Math.Abs(at - top) <= 1)
            {
                await TwoAnimationFramesAsync(page);
                if (Math.Abs((await BoxAsync(element)).Top - top) <= 1) return;
                continue;
            }
            await page.EvaluateAsync("d => window.scrollBy(0, d)", at - top);
            await TwoAnimationFramesAsync(page);
        }
        Assert.Fail($"the page could not be scrolled to put the element {top} px from the top of the window");
    }

    private static async Task AssertPinnedAsync(IPage page) =>
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.querySelector('.nav-mark').getBoundingClientRect().top < -0.5 && Math.abs(document.querySelector('.toolbar-row.report-nav').getBoundingClientRect().top) < 1"),
            "the bar is pinned to the top of the window");

    // gives the focus, with no scroll, to the control before el in the order Tab goes, so that Tab lands on el
    private const string FocusBefore = """
        el => {
            const all = Array.from(document.querySelectorAll('a[href], button, input, select, textarea, summary, [tabindex]'))
                .filter(e => e.tabIndex >= 0 && !e.disabled && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden');
            const before = all.filter(e => e.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING);
            const prev = before[before.length - 1];
            prev.focus({ preventScroll: true });
            return document.activeElement === prev;
        }
        """;

    /// <summary>
    /// Scenario 01.1 open, its diagram drawn and its settings open, the page scrolled so that the settings stick under the
    /// pinned bar and the diagram's link starts <paramref name="linkUnder"/> px above their foot. Returns the settings and
    /// the link.
    /// </summary>
    private async Task<(ILocator Settings, ILocator Link)> StuckSettingsAsync(string fileName, double linkUnder)
    {
        await OpenAsync(Generate(fileName, diagram: true));
        var scenario = Scenario("01-1");
        await scenario.EvaluateAsync("d => { d.open = true; }");
        await WaitForDiagramSvg(60000);
        // 5.0.0's browser engine draws a link's label inside an <a>, as the Java engine does (Server or Local rendering),
        // and that <a> is what Tab reaches (the 4.x engine drew the label as text, and this fixture wrapped it in one)
        var link = scenario.Locator(".plantuml-browser svg a[href='#d48-link']");
        await link.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await OpenScenarioSettingsAsync(scenario);
        var settings = scenario.Locator(".scenario-settings.is-open");
        await settings.WaitForAsync();
        await TwoAnimationFramesAsync(Page);

        // far enough below the settings that they still stick once the link has been brought below them
        var (sTop, sBottom) = await BoxAsync(settings);
        var (lTop, _) = await BoxAsync(link);
        Assert.True(lTop - sBottom > 30, $"the link starts {lTop - sBottom} px below the settings");

        var barHeight = await Bar.EvaluateAsync<double>("b => b.getBoundingClientRect().height");
        await PlaceAsync(Page, link, barHeight + (sBottom - sTop) - linkUnder);
        await AssertPinnedAsync(Page);
        var (stuckTop, stuckBottom) = await BoxAsync(settings);
        var (_, navBottom) = await BoxAsync(Bar);
        Assert.True(Math.Abs(stuckTop - navBottom) <= 1, $"the settings stick under the bar ({stuckTop} against {navBottom})");
        var (linkTop, _) = await BoxAsync(link);
        Assert.True(linkTop < stuckBottom - 3, $"the link starts {stuckBottom - linkTop} px above the settings' foot");
        return (settings, link);
    }

    [Fact]
    public async Task Tab_to_a_link_under_a_scenarios_stuck_settings_brings_it_12_px_below_them()
    {
        var (settings, link) = await StuckSettingsAsync("Focus_SettingsBand.html", linkUnder: 6);
        await FocusQuietlyAsync(settings.Locator(":scope > .scenario-settings-gear"));

        // since 5.7.0 every drawn diagram has a zoom slider, which Tab reaches first, and shows while it has the focus
        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);
        var slider = Scenario("01-1").Locator(".plantuml-browser > .diagram-zoom-controls .diagram-zoom-slider").First;
        Assert.True(await HasFocusAsync(slider), "Tab went from the settings' gear to the diagram's zoom slider");
        Assert.Equal("1", await slider.EvaluateAsync<string>("s => getComputedStyle(s.closest('.diagram-zoom-controls')).opacity"));

        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await HasFocusAsync(link), "Tab went from the diagram's zoom slider to its link");
        var (sTop, sBottom) = await BoxAsync(settings);
        var (_, navBottom) = await BoxAsync(Bar);
        Assert.True(Math.Abs(sTop - navBottom) <= 1, $"the settings still stick under the bar ({sTop} against {navBottom})");
        var (top, _) = await BoxAsync(link);
        Assert.True(top >= sBottom + 12 - 1, $"the link starts at {top}, less than 12 px below the settings' foot at {sBottom}");
    }

    [Fact]
    public async Task Tab_onto_the_gear_of_settings_stuck_under_the_bar_leaves_them_there()
    {
        var (settings, _) = await StuckSettingsAsync("Focus_SettingsGear.html", linkUnder: 6);
        var gear = settings.Locator(":scope > .scenario-settings-gear");
        // the gear sits within 12 px of the bar's foot, where a control the page could move would be brought down from
        var (gearTop, _) = await BoxAsync(gear);
        var (_, navBottom) = await BoxAsync(Bar);
        Assert.True(gearTop < navBottom + 12 - 2, $"the gear is at {gearTop}, the bar's foot at {navBottom}");
        Assert.True(await gear.EvaluateAsync<bool>(FocusBefore), "the control before the gear has the focus");

        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await HasFocusAsync(gear), "Tab went to the gear");
        // A browser may scroll a few pixels for the gear's scroll margin, which settings stuck under the bar do not follow.
        // Bringing the gear down would scroll the page until they came unstuck.
        var (sTop, _) = await BoxAsync(settings);
        Assert.True(Math.Abs(sTop - navBottom) <= 1, $"the settings still stick under the bar ({sTop} against {navBottom})");
        var (gearNow, _) = await BoxAsync(gear);
        Assert.True(Math.Abs(gearNow - gearTop) <= 1, $"the gear moved from {gearTop} to {gearNow}");
    }

    [Fact]
    public async Task Tab_to_a_scenario_cut_by_the_foot_of_the_window_brings_it_12_px_above_the_foot()
    {
        await OpenAsync(Generate("Focus_Foot.html"));
        var next = Scenario("05-2");
        await PlaceAsync(Page, next, ViewportHeight - 12);
        await FocusQuietlyAsync(LinkIcon(Scenario("05-1")));

        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await HasFocusAsync(next), "Tab went to the next scenario");
        var (top, bottom) = await BoxAsync(next);
        var (_, navBottom) = await BoxAsync(Bar);
        Assert.True(bottom <= ViewportHeight - 12 + 1, $"the scenario ends at {bottom}, in the window's last 12 px");
        Assert.True(top >= navBottom + 12 - 1, $"the scenario starts at {top}, under the bar's foot at {navBottom}");
    }

    /// <summary>Shift+Tab from a scenario to the one above, whose heading is half under the pinned bar.</summary>
    private async Task ShiftTabUnderTheBarAsync(IPage page, string fileName)
    {
        await page.GotoReportAsync(Generate(fileName));
        await page.Locator("details.feature").First.WaitForAsync();
        await TwoAnimationFramesAsync(page);
        await page.EvaluateAsync(TrackFocus);
        var bar = page.Locator(".toolbar-row.report-nav");
        var link = page.Locator("#scenario-scenario-03-2 > summary > a.scenario-link");
        var barHeight = await bar.EvaluateAsync<double>("b => b.getBoundingClientRect().height");
        await PlaceAsync(page, link, barHeight - 6);
        await AssertPinnedAsync(page);
        await FocusQuietlyAsync(page.Locator("#scenario-scenario-03-3"));

        await page.Keyboard.PressAsync("Shift+Tab");
        await WaitPastTheWatchAsync(page);

        Assert.True(await HasFocusAsync(link), "Shift+Tab went to the heading of the scenario above");
        var (top, _) = await BoxAsync(link);
        var (_, navBottom) = await BoxAsync(bar);
        Assert.True(top >= navBottom + 12 - 1, $"the heading's link starts at {top}, less than 12 px below the bar's foot at {navBottom}");
    }

    [Fact]
    public Task Shift_Tab_to_a_scenario_half_under_the_bar_brings_it_below_the_bar() =>
        ShiftTabUnderTheBarAsync(Page, "Focus_ShiftTab.html");

    // Firefox scrolls nothing for a control that already shows in part, so the page has to: Playwright's own Firefox,
    // skipped where it is not installed (CI installs Chromium only), as FormStateRestorationTests is
    [Fact]
    public async Task Shift_Tab_to_a_scenario_half_under_the_bar_brings_it_below_the_bar_in_Firefox()
    {
        using var playwright = await Playwright.CreateAsync();
        IBrowser? firefox = null;
        try { firefox = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }); }
        catch (PlaywrightException) { firefox = null; }
        Assert.SkipWhen(firefox is null, "Firefox is not installed (CI installs chromium only)");
        await using var _ = firefox!;
        var page = await OpenPageAsync(firefox!, new BrowserNewPageOptions { ViewportSize = new() { Width = ViewportWidth, Height = ViewportHeight } });

        await ShiftTabUnderTheBarAsync(page, "Focus_ShiftTab_Firefox.html");
    }

    [Fact]
    public async Task A_scenario_taller_than_the_window_that_Tab_reaches_stays_where_it_is()
    {
        await OpenAsync(Generate("Focus_Tall.html"));
        var tall = Scenario("02-2");
        await tall.EvaluateAsync("d => { d.open = true; }");
        await TwoAnimationFramesAsync(Page);
        var barHeight = await Bar.EvaluateAsync<double>("b => b.getBoundingClientRect().height");
        await PlaceAsync(Page, tall, barHeight - 20);
        await AssertPinnedAsync(Page);
        // its top under the bar, its foot past the window: it shows, and is taller than the room under the bar
        var (_, bottom) = await BoxAsync(tall);
        Assert.True(bottom > ViewportHeight, $"the scenario ends at {bottom}, inside the window");
        await FocusQuietlyAsync(LinkIcon(Scenario("02-1")));
        var y = await ScrollYAsync(Page);

        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await HasFocusAsync(tall), "Tab went to the tall scenario");
        Assert.Equal(y, await ScrollYAsync(Page));
    }

    [Fact]
    public async Task A_scenario_pushed_away_in_the_second_after_Tab_is_brought_back()
    {
        await OpenAsync(Generate("Focus_Watch.html"));
        var next = Scenario("04-2");
        await PlaceAsync(Page, next, 450);
        // A block drawn above the scenario a frame and 40 ms after it has the focus, as a diagram above it can be drawn
        // late. With scroll anchoring off, the block pushes the scenario down the page and no scroll event follows.
        await next.EvaluateAsync("""
            d => {
                const style = document.createElement('style');
                style.textContent = 'html { overflow-anchor: none !important; }';
                document.head.appendChild(style);
                window.__d48Pushed = false;
                d.addEventListener('focus', () => requestAnimationFrame(() => setTimeout(() => {
                    const block = document.createElement('div');
                    block.style.height = '1500px';
                    d.parentNode.insertBefore(block, d);
                    window.__d48Pushed = true;
                }, 40)), { once: true });
            }
            """);
        await FocusQuietlyAsync(LinkIcon(Scenario("04-1")));

        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await Page.EvaluateAsync<bool>("() => window.__d48Pushed"), "the block went in above the scenario");
        Assert.True(await HasFocusAsync(next), "Tab went to the scenario");
        var (top, bottom) = await BoxAsync(next);
        var (_, navBottom) = await BoxAsync(Bar);
        Assert.True(top >= navBottom + 12 - 1 && bottom <= ViewportHeight - 12 + 1,
            $"the scenario is at {top} to {bottom}, outside the window's band below the bar's foot at {navBottom}");
    }

    [Fact]
    public async Task Clicking_in_and_out_of_a_diagram_half_under_the_bar_leaves_the_page_where_it_is()
    {
        await OpenAsync(Generate("Focus_Click.html", diagram: true));
        var scenario = Scenario("01-1");
        await scenario.EvaluateAsync("d => { d.open = true; }");
        await WaitForDiagramSvg(60000);
        await TwoAnimationFramesAsync(Page);
        var barHeight = await Bar.EvaluateAsync<double>("b => b.getBoundingClientRect().height");
        await PlaceAsync(Page, scenario, barHeight - 30);
        await AssertPinnedAsync(Page);
        // the scenario takes the focus a click in it gives, and fits under the bar: were that focus moved, it would be
        var (sTop, sBottom) = await BoxAsync(scenario);
        Assert.True(sBottom - sTop < ViewportHeight - 24 - barHeight, $"the scenario is {sBottom - sTop} px tall");
        var y = await ScrollYAsync(Page);

        // in: the diagram's box, beside the drawing
        var box = scenario.Locator(".plantuml-browser").First;
        var (bTop, _) = await BoxAsync(box);
        var bLeft = await box.EvaluateAsync<double>("e => e.getBoundingClientRect().left");
        await MarkAsync(Page);
        await Page.Mouse.ClickAsync((float)(bLeft + 4), (float)(Math.Max(bTop, barHeight) + 10));
        await WaitPastTheWatchAsync(Page);
        Assert.Equal(y, await ScrollYAsync(Page));

        // out: the scenario's steps, after focus has left it
        await Page.EvaluateAsync("() => { if (document.activeElement) document.activeElement.blur(); }");
        var step = scenario.Locator(".step-text").First;
        var (tTop, tBottom) = await BoxAsync(step);
        Assert.True(tTop > barHeight + 2, $"the step starts at {tTop}, under the bar");
        var tLeft = await step.EvaluateAsync<double>("e => e.getBoundingClientRect().left");
        await MarkAsync(Page);
        await Page.Mouse.ClickAsync((float)(tLeft + 4), (float)((tTop + tBottom) / 2));
        await WaitPastTheWatchAsync(Page);
        Assert.True(await HasFocusAsync(scenario), "the click gave the scenario the focus");
        Assert.Equal(y, await ScrollYAsync(Page));
    }

    [Fact]
    public async Task Tab_between_the_bars_controls_leaves_the_page_where_it_is()
    {
        await OpenAsync(Generate("Focus_Bar.html"));
        await PlaceAsync(Page, Scenario("03-1"), 300);
        await AssertPinnedAsync(Page);
        await FocusQuietlyAsync(Page.Locator("#tab-features"));
        var y = await ScrollYAsync(Page);

        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await Bar.EvaluateAsync<bool>("b => b.contains(document.activeElement) && document.activeElement.id !== 'tab-features'"),
            "Tab went to the bar's next control");
        Assert.Equal(y, await ScrollYAsync(Page));
    }

    [Fact]
    public async Task Tab_into_the_internal_flow_popup_leaves_the_page_where_it_is()
    {
        // a short window, so the popup's close sits where the bar covers what scrolls under it
        await Page.SetViewportSizeAsync(ViewportWidth, 260);
        await OpenAsync(Generate("Focus_Popup.html", flow: true));
        await PlaceAsync(Page, Scenario("03-1"), 200);
        await AssertPinnedAsync(Page);
        await Page.EvaluateAsync("() => window._iflowShowPopup('iflow-d48')");
        var close = Page.Locator(".iflow-overlay .iflow-popup-close");
        await close.WaitForAsync();
        await TwoAnimationFramesAsync(Page);
        var (closeTop, _) = await BoxAsync(close);
        var (_, navBottom) = await BoxAsync(Bar);
        Assert.True(closeTop < navBottom + 12 - 2, $"the popup's close is at {closeTop}, the bar's foot at {navBottom}");
        Assert.True(await close.EvaluateAsync<bool>(FocusBefore), "the control before the popup has the focus");
        var y = await ScrollYAsync(Page);

        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await HasFocusAsync(close), "Tab went to the popup's close");
        Assert.Equal(y, await ScrollYAsync(Page));
    }

    // the controls above the bar at rest are the title line's since the search left the box for it (plans/SEARCH_PLAN.md
    // 4.2): from the search button Tab goes to the theme switch, and the page stays where it is
    [Fact]
    public async Task Tab_to_a_control_above_the_bar_at_rest_leaves_the_page_where_it_is()
    {
        await OpenAsync(Generate("Focus_Rest.html"));
        // scrolled, but not so far that the theme switch comes within the focus script's 12 px of the window's top: then it
        // would rightly bring the switch down (report-focus-script.js, GAP)
        var y = await Page.EvaluateAsync<int>(
            "() => Math.max(1, Math.floor(document.querySelector('.theme-toggle').getBoundingClientRect().top + window.scrollY) - 14)");
        await Page.EvaluateAsync("y => window.scrollTo(0, y)", y);
        await TwoAnimationFramesAsync(Page);
        Assert.Equal(y, await ScrollYAsync(Page));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector('.nav-mark').getBoundingClientRect().top > 0"), "the bar is at rest");
        await FocusQuietlyAsync(Page.Locator("#search-button"));

        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await Page.EvaluateAsync<bool>("() => document.activeElement.classList.contains('theme-toggle')"), "Tab went to the theme switch");
        var top = await Page.EvaluateAsync<double>("() => document.activeElement.getBoundingClientRect().top");
        var (navTop, _) = await BoxAsync(Bar);
        Assert.True(top < navTop, $"the control at {top} is above the bar at {navTop}");
        Assert.Equal(y, await ScrollYAsync(Page));
    }

    [Fact]
    public async Task Shift_Tab_to_the_search_cut_by_the_top_of_the_window_while_the_bar_rests_brings_it_12_px_in()
    {
        await OpenAsync(Generate("Focus_RestTop.html"));
        // the search is the title line's button since plans/SEARCH_PLAN.md 4.2, and the theme switch the control after it
        var search = Page.Locator("#search-button");
        await PlaceAsync(Page, search, -10);
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector('.nav-mark').getBoundingClientRect().top > 0"), "the bar is at rest");
        await FocusQuietlyAsync(Page.Locator(".theme-toggle"));

        await Page.Keyboard.PressAsync("Shift+Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await HasFocusAsync(search), "Shift+Tab went to the search");
        // the bar at rest covers nothing, so the band starts 12 px below the top of the window, as it ends 12 px above its foot
        var (top, _) = await BoxAsync(search);
        Assert.True(top >= 12 - 1, $"the search starts at {top}, less than 12 px into the window");
    }

    [Fact]
    public async Task On_a_phone_Tab_leaves_the_page_where_the_browser_put_it()
    {
        await Page.SetViewportSizeAsync(400, 800);
        await OpenAsync(Generate("Focus_Phone.html"));
        Assert.Equal("static", await Bar.EvaluateAsync<string>("b => getComputedStyle(b).position"));
        var next = Scenario("05-2");
        await PlaceAsync(Page, next, 800 - 12);
        await FocusQuietlyAsync(LinkIcon(Scenario("05-1")));

        await Page.Keyboard.PressAsync("Tab");
        await WaitPastTheWatchAsync(Page);

        Assert.True(await HasFocusAsync(next), "Tab went to the next scenario");
        // the browser brings a control cut by the window's foot up to the foot, and nothing lifts it further
        var (_, bottom) = await BoxAsync(next);
        Assert.True(bottom > 800 - 6 && bottom <= 800 + 1, $"the scenario ends at {bottom}");
    }
}
