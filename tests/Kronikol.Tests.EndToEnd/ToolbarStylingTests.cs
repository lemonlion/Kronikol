using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What the toolbars PAINT (plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.md §2.4, §5 T6, T7, T10, T11): the scenario
/// toolbar is styled whether or not internal-flow tracking is on; on the violet page every toolbar state
/// is violet, because the theme is emitted after the component sheets and states its hovers before its
/// active rules; the blue report keeps its blue; InternalFlowPopupCustomStyleSheet is applied; and on a
/// phone, where a tap leaves the control hovered, a tapped filter keeps its active colour.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ToolbarStylingTests : PlaywrightTestBase
{
    private readonly PlaywrightFixture _fixture;

    public ToolbarStylingTests(PlaywrightFixture fixture) : base(fixture) => _fixture = fixture;

    private const string Violet = "rgb(139, 92, 246)";       // #8B5CF6, an active control
    private const string VioletDark = "rgb(124, 58, 237)";   // #7C3AED, an active control hovered
    private const string Tint = "rgb(237, 233, 254)";        // #EDE9FE, an idle control hovered
    private const string TintBorder = "rgb(167, 139, 250)";  // #A78BFA
    private const string Blue = "rgb(66, 133, 244)";
    private const string BlueTint = "rgb(230, 240, 255)";
    private const string BlueTintBorder = "rgb(100, 150, 255)";
    private const string Grey = "rgb(245, 245, 245)";         // an idle diagram tab

    private async Task Open(string fileName, bool violet, bool internalFlowTracking = true, string? stylesheet = null)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, fileName, _ => { },
            stylesheet: stylesheet ?? (violet ? Stylesheets.VioletThemeStyleSheet : null), internalFlowTracking: internalFlowTracking));
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    private static async Task Paints(ILocator control, string background, string? border = null)
    {
        await Expect(control).ToHaveCSSAsync("background-color", background);
        if (border is not null)
            await Expect(control).ToHaveCSSAsync("border-top-color", border);
    }

    private async Task OpenFirstScenarioDiagrams()
    {
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => document.querySelectorAll('details.example-diagrams').forEach(d => d.open = true)");
    }

    // ── T6: the scenario toolbar without internal-flow tracking ──

    [Fact]
    public async Task Scenario_toolbar_is_a_wrapping_flex_row_without_internal_flow_tracking()
    {
        await Page.GotoAsync(GenerateReport("ToolbarNoFlow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenFirstScenarioDiagrams();

        var toolbar = Page.Locator("details.example-diagrams .diagram-toggle").First;
        await Expect(toolbar).ToBeVisibleAsync();
        await Expect(toolbar).ToHaveCSSAsync("display", "flex");
        await Expect(toolbar).ToHaveCSSAsync("flex-wrap", "wrap");
        await Expect(toolbar).ToHaveCSSAsync("padding-left", "16px");
        await Expect(toolbar).ToHaveCSSAsync("padding-right", "16px");
    }

    // ── T7: every toolbar state on the violet page, and the blue report unchanged ──

    [Theory]
    [InlineData(true, Violet)]
    [InlineData(false, Blue)]
    public async Task An_active_filter_toggle_keeps_its_active_colour_while_hovered(bool violet, string active)
    {
        await Open($"FilterHover{violet}.html", violet);

        var happyPath = Page.Locator(".happy-path-toggle").First;
        await happyPath.ClickAsync();
        await Expect(happyPath).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("happy-path-active"));
        Assert.True(await happyPath.EvaluateAsync<bool>("b => b.matches(':hover')"), "the clicked toggle is still hovered");
        await Paints(happyPath, active, active);
    }

    public static TheoryData<bool, string, string?, string> HeaderHovers => new()
    {
        { true, Tint, TintBorder, ".dependency-toggle" },
        { true, Tint, TintBorder, ".dep-mode-toggle" },
        { true, Tint, TintBorder, ".cat-mode-toggle" },
        { true, Tint, TintBorder, ".percentile-btn" },
        { true, Tint, TintBorder, ".collapse-expand-all" },
        { true, Tint, TintBorder, ".export-btn" },
        { true, Tint, TintBorder, ".timeline-toggle" },
        { true, Tint, TintBorder, ".toolbar-right .details-radio-btn:not(.details-active)" },
        { false, BlueTint, BlueTintBorder, ".dependency-toggle" },
        { false, "rgb(220, 230, 245)", BlueTintBorder, ".dep-mode-toggle" },
        { false, "rgb(220, 230, 245)", BlueTintBorder, ".cat-mode-toggle" },
        { false, BlueTint, BlueTintBorder, ".percentile-btn" },
        { false, BlueTint, BlueTintBorder, ".collapse-expand-all" },
        { false, BlueTint, BlueTintBorder, ".export-btn" },
        { false, BlueTint, BlueTintBorder, ".timeline-toggle" },
        { false, BlueTint, BlueTintBorder, ".toolbar-right .details-radio-btn:not(.details-active)" },
    };

    [Theory]
    [MemberData(nameof(HeaderHovers))]
    public async Task A_hovered_header_control_takes_the_themes_tint(bool violet, string background, string? border, string selector)
    {
        await Open($"HeaderHover{violet}{System.Text.RegularExpressions.Regex.Replace(selector, "[^a-z]", "")}.html", violet);

        var control = Page.Locator(selector).First;
        await control.HoverAsync();
        await Paints(control, background, border);
    }

    [Theory]
    [InlineData(true, Violet, Violet, VioletDark)]
    [InlineData(false, Blue, Blue, "rgb(50, 110, 220)")]
    public async Task Active_percentile_and_timeline_buttons_take_the_themes_colours(bool violet, string active, string activeHovered, string timelineActiveHovered)
    {
        await Open($"ActiveHeader{violet}.html", violet);

        var percentile = Page.Locator(".percentile-btn").First;
        await percentile.ClickAsync();
        await Expect(percentile).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("percentile-active"));
        await Paints(percentile, activeHovered, active);
        await Page.Mouse.MoveAsync(0, 0);
        await Paints(percentile, active, active);

        var timeline = Page.Locator(".timeline-toggle", new() { HasTextString = "Scenario Timeline" });
        await timeline.ClickAsync();
        await Expect(timeline).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("timeline-toggle-active"));
        await Paints(timeline, timelineActiveHovered);
        await Page.Mouse.MoveAsync(0, 0);
        await Paints(timeline, active, active);
    }

    [Theory]
    [InlineData(true, Violet)]
    [InlineData(false, Blue)]
    public async Task The_active_details_radio_takes_the_themes_colour_at_rest_and_hovered(bool violet, string active)
    {
        await Open($"DetailsRadio{violet}.html", violet);

        var radio = Page.Locator(".toolbar-right .details-radio-btn.details-active").First;
        await Paints(radio, active, active);
        await radio.HoverAsync();
        await Paints(radio, active, active);
    }

    public static TheoryData<bool, bool, string, string, string, string> TabColours => new()
    {
        // violet, flow tracking, idle, idle hovered, active, active hovered
        { true, true, Grey, Tint, Violet, VioletDark },
        { true, false, Grey, Tint, Violet, VioletDark },
        { false, true, Grey, "rgb(232, 240, 254)", Blue, "rgb(51, 103, 214)" },
        { false, false, Grey, "rgb(232, 240, 254)", Blue, "rgb(51, 103, 214)" },
    };

    [Theory]
    [MemberData(nameof(TabColours))]
    public async Task Diagram_tabs_take_the_themes_colours_with_or_without_internal_flow_tracking(
        bool violet, bool internalFlowTracking, string idle, string idleHovered, string active, string activeHovered)
    {
        await Open($"Tabs{violet}{internalFlowTracking}.html", violet, internalFlowTracking);
        await OpenFirstScenarioDiagrams();

        var idleTab = Page.Locator("details.example-diagrams .diagram-toggle-btn:not(.diagram-toggle-active)").First;
        var activeTab = Page.Locator("details.example-diagrams .diagram-toggle-btn.diagram-toggle-active").First;
        await Expect(activeTab).ToBeVisibleAsync();
        await Paints(idleTab, idle);
        await Paints(activeTab, active, active);
        await idleTab.HoverAsync();
        await Paints(idleTab, idleHovered);
        await activeTab.HoverAsync();
        await Paints(activeTab, activeHovered, active);
    }

    [Theory]
    [InlineData(true, Tint)]
    [InlineData(false, BlueTint)]
    public async Task The_scenario_link_and_copy_button_take_the_themes_tint_when_hovered(bool violet, string tint)
    {
        await Open($"ScenarioLinks{violet}.html", violet);
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();

        foreach (var selector in new[] { ".scenario-link", ".copy-scenario-name" })
        {
            var control = Page.Locator(selector).First;
            await control.HoverAsync();
            await Paints(control, tint);
        }
    }

    /// <summary>The popup's controls and a search-matched parameterized row, which the fixture does not
    /// render on its own: elements carrying the real classes, appended to the page, paint as the
    /// emitted markup paints.</summary>
    [Theory]
    [InlineData(true, Tint, Violet, VioletDark, Tint, Violet, "rgb(139, 92, 246) 4px 0px 0px 0px inset")]
    [InlineData(false, "rgb(232, 240, 254)", Blue, "rgb(51, 103, 214)", "rgb(240, 244, 255)", Blue, "rgb(66, 133, 244) 4px 0px 0px 0px inset")]
    public async Task Popup_controls_and_search_matches_take_the_themes_colours(bool violet, string idleHovered, string active,
        string activeHovered, string listHovered, string listHoveredBorder, string searchMatchShadow)
    {
        await Open($"PopupControls{violet}.html", violet);
        await AppendPopupControls();

        var idle = Page.Locator("#probe-idle");
        await idle.HoverAsync();
        await Paints(idle, idleHovered);
        var toggle = Page.Locator("#probe-active");
        await Page.Mouse.MoveAsync(0, 0);
        await Paints(toggle, active, active);
        await toggle.HoverAsync();
        await Paints(toggle, activeHovered);
        var item = Page.Locator("#probe-rel-item");
        await item.HoverAsync();
        await Paints(item, listHovered, listHoveredBorder);
        var cell = Page.Locator("#probe-rel-cell");
        await cell.HoverAsync();
        await Paints(cell, listHovered);
        await Expect(Page.Locator("#probe-search-match")).ToHaveCSSAsync("box-shadow", searchMatchShadow);
    }

    private Task AppendPopupControls() => Page.EvaluateAsync("""
        () => {
            const host = document.createElement('div');
            host.style.cssText = 'position: fixed; top: 60px; left: 60px; z-index: 100000; background: white; padding: 8px';
            host.innerHTML =
                '<div class="iflow-toggle"><button id="probe-idle" class="iflow-toggle-btn">Flame Chart</button>' +
                '<button id="probe-active" class="iflow-toggle-btn iflow-toggle-active">Activity</button></div>' +
                '<ul class="iflow-rel-list"><li id="probe-rel-item">Related test</li></ul>' +
                '<table class="iflow-rel-summary-table"><tbody><tr><td id="probe-rel-cell">Related</td></tr></tbody></table>' +
                '<table class="param-test-table"><tbody><tr id="probe-search-match" class="row-passed row-search-match"><td>1</td></tr></tbody></table>';
            document.body.appendChild(host);
        }
        """);

    [Theory]
    [InlineData(true, Tint, TintBorder)]
    [InlineData(false, BlueTint, BlueTintBorder)]
    public async Task The_phone_diagram_settings_button_takes_the_themes_tint_when_hovered(bool violet, string tint, string border)
    {
        await Page.SetViewportSizeAsync(700, 900);
        await Open($"SettingsHover{violet}.html", violet);
        await OpenFirstScenarioDiagrams();

        var settings = Page.Locator(".scenario-diagram-controls-toggle").First;
        await settings.HoverAsync();
        await Paints(settings, tint, border);
    }

    // ── T10: InternalFlowPopupCustomStyleSheet ──

    [Theory]
    [InlineData(".iflow-toggle-active { background: rgb(1, 2, 3); }", "rgb(1, 2, 3)")]
    [InlineData(null, Violet)]
    public async Task The_popup_custom_stylesheet_paints_over_the_theme_when_flow_tracking_is_on(string? popupSheet, string expected)
    {
        // The sheets the two report call sites compose from the options (ReportGenerator.UserStylesheets).
        var options = new ReportConfigurationOptions { InternalFlowTracking = true, InternalFlowPopupCustomStyleSheet = popupSheet };
        await Open($"PopupSheet{popupSheet is null}.html", violet: true,
            stylesheet: ReportGenerator.UserStylesheets(Stylesheets.VioletThemeStyleSheet, options));
        await AppendPopupControls();

        await Paints(Page.Locator("#probe-active"), expected);
    }

    // ── T11: a tapped filter on a phone ──

    [Theory]
    [InlineData(true, Violet)]
    [InlineData(false, Blue)]
    public async Task A_tapped_filter_on_a_phone_keeps_its_active_colour(bool violet, string active)
    {
        await using var phone = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            IsMobile = true,
            HasTouch = true
        });
        var page = await phone.NewPageAsync();
        await page.GotoAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, $"PhoneTap{violet}.html", _ => { },
            stylesheet: violet ? Stylesheets.VioletThemeStyleSheet : null));
        await page.Locator("details.feature").First.WaitForAsync();

        await page.Locator(".mobile-filter-toggle").TapAsync();
        var happyPath = page.Locator(".happy-path-toggle").First;
        await happyPath.TapAsync();

        await Assertions.Expect(happyPath).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("happy-path-active"));
        Assert.True(await happyPath.EvaluateAsync<bool>("b => b.matches(':hover')"),
            "a tap leaves the control hovered on a touch screen; without that this fact proves nothing");
        await Assertions.Expect(happyPath).ToHaveCSSAsync("background-color", active);
        await Assertions.Expect(happyPath).ToHaveCSSAsync("color", "rgb(255, 255, 255)");
    }
}
