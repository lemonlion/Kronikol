using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A scenario's diagram bars (5.0.0, plans/OPTION_C_PLAN.md S8, D29 to D34, R17.23, R17.25 and R17.26): the views' tabs
/// on a bar of their own, which the arrow keys move along; the scenario's settings behind the gear at its diagram's
/// corner, which opens and closes them, keeps them under the report's bar while the diagrams are in view and puts them
/// away while another view shows; on a phone, the gear on the section's title; controls that keep their width when
/// switched; the line count offered only while Truncated is picked; and Enter pressing a button while the arrow keys
/// have picked a scenario.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ScenarioSettingsTests : PlaywrightTestBase
{
    public ScenarioSettingsTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenAsync(string fileName, int width = 1400, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoReportAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, fileName, _ => { }));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await TwoAnimationFramesAsync(Page);
    }

    private ILocator Section => Page.Locator("details.example-diagrams:has(> .scenario-settings)").First;
    private ILocator Settings => Section.Locator(":scope > .scenario-settings");
    private ILocator Gear => Settings.Locator(":scope > .scenario-settings-gear");
    private ILocator Bar => Settings.Locator(":scope > .diagram-options-bar");
    private ILocator Tab(string dtype) => Section.Locator($":scope > .diagram-toggle-tabs > .diagram-toggle-btn[data-dtype='{dtype}']");

    private static async Task<double> Width(ILocator l) => (await l.BoundingBoxAsync())!.Width;

    [Fact]
    public async Task The_gear_opens_and_closes_the_scenarios_settings_and_stays_where_it_was()
    {
        await OpenAsync("ScenarioSettings_Gear.html");
        Assert.False(await Bar.IsVisibleAsync());
        Assert.Equal("false", await Gear.GetAttributeAsync("aria-expanded"));
        var closed = (await Gear.BoundingBoxAsync())!;

        await Gear.ClickAsync();

        Assert.True(await Bar.IsVisibleAsync());
        Assert.Equal("true", await Gear.GetAttributeAsync("aria-expanded"));
        Assert.Equal("Hide this scenario's payload and diagram settings", await Gear.GetAttributeAsync("title"));
        Assert.Equal("This scenario:", await Bar.Locator(".scenario-settings-label").TextContentAsync());
        var open = (await Gear.BoundingBoxAsync())!;
        Assert.Equal(closed.X, open.X, 0.5);
        Assert.Equal(closed.Y, open.Y, 0.5);

        await Gear.ClickAsync();

        Assert.False(await Bar.IsVisibleAsync());
        Assert.Equal("false", await Gear.GetAttributeAsync("aria-expanded"));
        Assert.Equal("This scenario's payload and diagram settings, to set it apart from the report's", await Gear.GetAttributeAsync("title"));
    }

    [Fact]
    public async Task The_open_settings_stay_under_the_report_bar_while_the_diagrams_are_in_view()
    {
        await OpenAsync("ScenarioSettings_Sticky.html", height: 420);
        await Gear.ClickAsync();
        // the page scrolled so the settings' own place is under the report's bar, the diagrams still in view
        await Page.EvaluateAsync("""
            () => {
                const s = document.querySelector('details.example-diagrams > .scenario-settings');
                const nav = document.querySelector('.report-nav');
                window.scrollTo(0, s.parentElement.getBoundingClientRect().top + window.scrollY - nav.offsetHeight + 60);
            }
            """);
        await TwoAnimationFramesAsync(Page);

        var read = await Page.EvaluateAsync<double[]>("""
            () => {
                const s = document.querySelector('details.example-diagrams > .scenario-settings');
                return [document.querySelector('.report-nav').getBoundingClientRect().bottom, s.getBoundingClientRect().top,
                        s.parentElement.getBoundingClientRect().bottom];
            }
            """);
        var (navBottom, settingsTop, sectionBottom) = (read[0], read[1], read[2]);
        Assert.True(sectionBottom > settingsTop + 40, $"the diagrams left the window ({sectionBottom} against {settingsTop})");
        Assert.Equal(navBottom, settingsTop, 1.0);
    }

    [Fact]
    public async Task The_settings_step_aside_on_another_view_and_come_back_with_the_sequence_diagrams()
    {
        await OpenAsync("ScenarioSettings_Views.html");
        await Gear.ClickAsync();

        await Tab("activity").ClickAsync();

        Assert.False(await Settings.IsVisibleAsync());
        Assert.Contains("diagrams-not-seq", await Section.GetAttributeAsync("class") ?? "");
        Assert.Equal("true", await Tab("activity").GetAttributeAsync("aria-selected"));
        Assert.Equal("false", await Tab("seq").GetAttributeAsync("aria-selected"));
        Assert.Equal("0", await Tab("activity").GetAttributeAsync("tabindex"));
        Assert.Equal("-1", await Tab("seq").GetAttributeAsync("tabindex"));

        await Tab("seq").ClickAsync();

        Assert.True(await Bar.IsVisibleAsync());
        Assert.DoesNotContain("diagrams-not-seq", await Section.GetAttributeAsync("class") ?? "");
    }

    [Fact]
    public async Task The_arrow_keys_move_between_the_views()
    {
        await OpenAsync("ScenarioSettings_Keys.html");
        await Tab("seq").FocusAsync();

        async Task<string?> Focused() => await Page.EvaluateAsync<string?>("() => document.activeElement.getAttribute('data-dtype')");

        await Page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("activity", await Focused());
        Assert.Equal("true", await Tab("activity").GetAttributeAsync("aria-selected"));
        await Page.Keyboard.PressAsync("End");
        Assert.Equal("flame", await Focused());
        Assert.Equal("true", await Tab("flame").GetAttributeAsync("aria-selected"));
        await Page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("seq", await Focused());
        await Page.Keyboard.PressAsync("ArrowLeft");
        Assert.Equal("flame", await Focused());
        await Page.Keyboard.PressAsync("Home");
        Assert.Equal("seq", await Focused());
        Assert.Equal("true", await Tab("seq").GetAttributeAsync("aria-selected"));
    }

    [Fact]
    public async Task On_a_phone_the_gear_on_the_sections_title_opens_the_settings_without_folding_the_section()
    {
        await OpenAsync("ScenarioSettings_Phone.html", width: 400);
        var titleGear = Section.Locator(":scope > summary > .gear-in-summary");
        Assert.True(await titleGear.IsVisibleAsync());
        Assert.False(await Gear.IsVisibleAsync());
        Assert.False(await Bar.IsVisibleAsync());

        await titleGear.ClickAsync();

        Assert.True(await Bar.IsVisibleAsync());
        Assert.True(await Section.EvaluateAsync<bool>("d => d.open"));
        Assert.Equal("true", await titleGear.GetAttributeAsync("aria-expanded"));

        await titleGear.ClickAsync();

        Assert.False(await Bar.IsVisibleAsync());
        Assert.True(await Section.EvaluateAsync<bool>("d => d.open"));
    }

    // The gear on a closed section's title opens the section with its settings showing, whether they were open or not.
    // Until 5.2.7 it only turned them over, inside the closed section: nothing showed, and with them open it closed them.
    [Fact]
    public async Task On_a_phone_the_gear_on_a_closed_sections_title_opens_the_section_with_its_settings()
    {
        await OpenAsync("ScenarioSettings_PhoneClosed.html", width: 400);
        var titleGear = Section.Locator(":scope > summary > .gear-in-summary");
        await Section.EvaluateAsync("d => { d.open = false; }");

        await titleGear.ClickAsync();

        Assert.True(await Section.EvaluateAsync<bool>("d => d.open"));
        Assert.True(await Bar.IsVisibleAsync());
        Assert.Equal("true", await titleGear.GetAttributeAsync("aria-expanded"));

        // closed again with its settings open
        await Section.EvaluateAsync("d => { d.open = false; }");
        await titleGear.ClickAsync();

        Assert.True(await Section.EvaluateAsync<bool>("d => d.open"));
        Assert.True(await Bar.IsVisibleAsync());
        Assert.Equal("true", await titleGear.GetAttributeAsync("aria-expanded"));
    }

    [Fact]
    public async Task A_toggle_keeps_its_word_and_its_width_when_switched()
    {
        await OpenAsync("ScenarioSettings_Toggle.html");
        await Gear.ClickAsync();
        var headers = Bar.Locator(".toggle-btn[data-toggle='headers']");
        Assert.Equal("false", await headers.GetAttributeAsync("aria-pressed"));
        var before = await Width(headers);

        await headers.ClickAsync();

        await Expect(headers).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal("Headers", await headers.Locator(".btn-label").TextContentAsync());
        Assert.Equal(before, await Width(headers), 0.01);
        await Page.WaitForFunctionAsync("() => !document.querySelector('.details-pending')", null, new() { PollingInterval = 200 });
        Assert.Equal(before, await Width(headers), 0.01);
    }

    [Fact]
    public async Task A_payload_button_keeps_its_width_when_it_is_picked()
    {
        await OpenAsync("ScenarioSettings_Radio.html");
        await Gear.ClickAsync();
        var expanded = Bar.Locator(".details-radio-btn[data-state='expanded']");
        var before = await Width(expanded);

        await expanded.ClickAsync();

        await Expect(expanded).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdetails-active\b"));
        Assert.Equal(before, await Width(expanded), 0.01);
    }

    [Fact]
    public async Task The_line_count_is_offered_while_Truncated_is_picked()
    {
        await OpenAsync("ScenarioSettings_Lines.html");
        await Gear.ClickAsync();
        var lines = Bar.Locator(".truncate-lines-select");
        Assert.True(await lines.IsVisibleAsync());

        await Bar.Locator(".details-radio-btn[data-state='expanded']").ClickAsync();
        Assert.False(await lines.IsVisibleAsync());

        await Bar.Locator(".details-radio-btn[data-state='truncated']").ClickAsync();
        Assert.True(await lines.IsVisibleAsync());
    }

    /// <summary>
    /// SEARCH_PLAN B12: the gear's Payloads and Headers redrew the scenario's diagrams drawn already, and a diagram drawn
    /// after them (another example row, a view not shown yet, one far down the page) took the report's choices on its
    /// first draw, where its five filters took the scenario's. An outline of two rows, each with a diagram of its own: the
    /// second row's is drawn only when that row is chosen. A scenario with no diagram comes first, by name, as the page
    /// draws the first scenario's diagrams, every row's, as it opens.
    /// </summary>
    [Fact]
    public async Task A_scenario_s_Payloads_and_Headers_reach_its_diagrams_drawn_later()
    {
        DiagramApiReports.Scenario Row(string tier, int at) => new(tier, "2 Discount applies",
            [.. DiagramApiReports.Call(tier, at, "GET", "/api/discounts/" + tier, DiagramApiReports.Body(tier, 14))], Outline: "2 discount", Row: tier);
        await Page.GotoReportAsync(DiagramApiReports.Write(TempDir, OutputDir, "ScenarioSettings_LaterRow.html",
            [new("first", "1 No diagram", []), Row("gold", 0), Row("silver", 5)]));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var group = Page.Locator("details.scenario-parameterized").First;
        const string drawn = """
            id => {
                const box = document.getElementById(id), c = box && box.querySelector('.plantuml-browser');
                return !!c && c.dataset.rendered === '1' && !!c.querySelector(':scope > svg .note-hover-rect') && !c._noteRendering && !window._plantumlRendering;
            }
            """;
        var prefix = await group.EvaluateAsync<string>("g => g.querySelector('[id$=\"-diagram-0\"]').id.replace(/-diagram-0$/, '')");
        await Page.WaitForFunctionAsync(drawn, prefix + "-diagram-0", new() { Timeout = 60000, PollingInterval = 200 });
        Assert.Equal(0, await Page.Locator($"#{prefix}-diagram-1 .plantuml-browser svg").CountAsync());

        await OpenScenarioSettingsAsync(group);
        await Bar.Locator(".details-radio-btn[data-state='expanded']").ClickAsync();
        await Page.WaitForFunctionAsync("() => !document.querySelector('.details-pending')", null, new() { PollingInterval = 200 });
        await Bar.Locator(".toggle-btn[data-toggle='headers']").ClickAsync();
        await Page.WaitForFunctionAsync("() => !document.querySelector('.details-pending')", null, new() { PollingInterval = 200 });

        await group.Locator("tr[data-row-idx='1']:visible").First.ClickAsync();
        await Page.WaitForFunctionAsync(drawn, prefix + "-diagram-1", new() { Timeout = 60000, PollingInterval = 200 });

        // the painted texts: a folded note's title, its tooltip, would hold its headers whatever is drawn
        var text = await Page.EvaluateAsync<string>("id => Array.from(document.querySelectorAll('#' + id + ' .plantuml-browser svg text')).map(t => t.textContent).join('').replace(/[\\s\\u200b]+/g, '')", prefix + "-diagram-1");
        Assert.Contains("Content-Type=application/json", text);
        Assert.Contains("silver_14", text);
    }

    /// <summary>
    /// B12's counterpart: the report's Payloads and Headers hold everywhere, as its filters do, so each takes over the
    /// scenarios' own choices, and a diagram a scenario draws later takes the report's again. The gear's Collapsed and
    /// Headers on, then the report's Expanded, and its Headers shown and hidden again: the second row's diagram, drawn
    /// after, shows its note whole, the 14 lines of its body, and without its headers. Not the other way round: a folded
    /// note paints no headers whichever choice holds, so a scenario's Headers kept would go unseen.
    /// </summary>
    [Fact]
    public async Task The_report_s_Payloads_and_Headers_take_over_a_scenario_s_own_for_its_diagrams_drawn_later()
    {
        DiagramApiReports.Scenario Row(string tier, int at) => new(tier, "2 Discount applies",
            [.. DiagramApiReports.Call(tier, at, "GET", "/api/discounts/" + tier, DiagramApiReports.Body(tier, 14))], Outline: "2 discount", Row: tier);
        await Page.GotoReportAsync(DiagramApiReports.Write(TempDir, OutputDir, "ScenarioSettings_ReportTakesOver.html",
            [new("first", "1 No diagram", []), Row("gold", 0), Row("silver", 5)]));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var group = Page.Locator("details.scenario-parameterized").First;
        const string drawn = """
            id => {
                const box = document.getElementById(id), c = box && box.querySelector('.plantuml-browser');
                return !!c && c.dataset.rendered === '1' && !!c.querySelector(':scope > svg .note-hover-rect') && !c._noteRendering && !window._plantumlRendering;
            }
            """;
        var prefix = await group.EvaluateAsync<string>("g => g.querySelector('[id$=\"-diagram-0\"]').id.replace(/-diagram-0$/, '')");
        await Page.WaitForFunctionAsync(drawn, prefix + "-diagram-0", new() { Timeout = 60000, PollingInterval = 200 });
        Assert.Equal(0, await Page.Locator($"#{prefix}-diagram-1 .plantuml-browser svg").CountAsync());

        await OpenScenarioSettingsAsync(group);
        await Bar.Locator(".details-radio-btn[data-state='collapsed']").ClickAsync();
        await Page.WaitForFunctionAsync("() => !document.querySelector('.details-pending')", null, new() { PollingInterval = 200 });
        await Bar.Locator(".toggle-btn[data-toggle='headers']").ClickAsync();
        await Page.WaitForFunctionAsync("() => !document.querySelector('.details-pending')", null, new() { PollingInterval = 200 });
        await Page.EvaluateAsync("""
            () => {
                window._setReportDetails('expanded');
                var headers = document.querySelector('.toolbar-right .toggle-btn[data-toggle="headers"]');
                window._toggleHeaders(headers);
                window._toggleHeaders(headers);
            }
            """);
        await Page.WaitForFunctionAsync("() => !document.querySelector('.details-pending')", null, new() { PollingInterval = 200 });

        await group.Locator("tr[data-row-idx='1']:visible").First.ClickAsync();
        await Page.WaitForFunctionAsync(drawn, prefix + "-diagram-1", new() { Timeout = 60000, PollingInterval = 200 });

        var read = await Page.EvaluateAsync<string>("""
            id => {
                const c = document.querySelector('#' + id + ' .plantuml-browser'), svg = c.querySelector(':scope > svg');
                const source = c._noteOriginalSource || c.getAttribute('data-plantuml');
                const idx = window._parseNoteBlocks(source).findIndex(b => b.contentLines.some(l => l.indexOf('silver_01') >= 0));
                const note = window._findNoteGroups(svg).find(g => { const ix = window._noteIndicesOf(g); return ix && ix.original === idx; });
                // the painted texts: a folded note's title, its tooltip, holds its headers whatever is drawn
                const painted = Array.from(svg.querySelectorAll('text')).map(t => t.textContent).join('');
                return (note ? window._paintedNoteRowCount(note) + ' rows' : 'no note') + ', '
                    + (painted.replace(/[\s​]+/g, '').indexOf('Content-Type') >= 0 ? 'headers' : 'no headers');
            }
            """, prefix + "-diagram-1");
        Assert.Equal("14 rows, no headers", read);
    }

    // until 5.0.0, Enter folded the scenario the arrow keys had picked, wherever the keyboard's focus was, and the
    // button that had it did nothing
    [Fact]
    public async Task Enter_presses_a_button_while_the_arrow_keys_have_picked_a_scenario()
    {
        await OpenAsync("ScenarioSettings_Enter.html");
        await Page.Locator("body").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        var picked = Page.Locator("details.scenario.scenario-focused");
        await picked.WaitForAsync();
        var pickedOpen = await picked.EvaluateAsync<bool>("d => d.open");

        await Gear.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        Assert.Equal("true", await Gear.GetAttributeAsync("aria-expanded"));
        Assert.Equal(pickedOpen, await picked.EvaluateAsync<bool>("d => d.open"));
    }
}
