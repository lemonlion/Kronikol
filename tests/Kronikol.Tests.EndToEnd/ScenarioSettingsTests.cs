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
        await Page.GotoAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, fileName, _ => { }));
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
