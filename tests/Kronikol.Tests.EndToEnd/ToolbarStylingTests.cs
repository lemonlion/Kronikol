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
    private const string Hover = "rgb(237, 241, 248)";        // --kron-hover: 5.0.0's hover (plans/OPTION_C_PLAN.md S1)
    private const string Selected = "rgb(220, 232, 251)";     // --kron-selected: a chosen filter, 5.0.0 (S6; 4.x a solid Blue)
    private const string SelectedEdge = "rgb(106, 143, 217)"; // --kron-selected-edge
    private const string SelectedInk = "rgb(29, 79, 176)";    // --kron-selected-ink
    private const string ControlBorder = "rgb(174, 183, 198)"; // --kron-control-border: a filter control's edge (S6)
    private const string SelectBorder = "rgb(122, 132, 150)";  // --kron-select-border: a dropdown's and a gear's edge (S8)
    private const string Accent = "rgb(37, 89, 199)";          // --kron-accent: the picked view tab's ink (S8)
    private const string None = "rgba(0, 0, 0, 0)";

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

        // 5.0.0: the scenario's settings, behind the gear of its diagrams (S8)
        await OpenScenarioSettingsAsync();
        var toolbar = Page.Locator("details.example-diagrams .diagram-toggle").First;
        await Expect(toolbar).ToBeVisibleAsync();
        await Expect(toolbar).ToHaveCSSAsync("display", "flex");
        await Expect(toolbar).ToHaveCSSAsync("flex-wrap", "wrap");
        await Expect(toolbar).ToHaveCSSAsync("padding-left", "16px");
        await Expect(toolbar).ToHaveCSSAsync("padding-right", "16px");
    }

    // ── T7: every toolbar state on the violet page, and the blue report unchanged ──

    [Theory]
    [InlineData(true, Violet, Violet)]
    [InlineData(false, Selected, SelectedEdge)]
    public async Task An_active_filter_toggle_keeps_its_active_colour_while_hovered(bool violet, string active, string edge)
    {
        await Open($"FilterHover{violet}.html", violet);

        await OpenFilterRowsAsync();
        var happyPath = Page.Locator(".filtering-box .happy-seg[data-hp='in']").First;
        await happyPath.ClickAsync();
        await Expect(happyPath).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.True(await happyPath.EvaluateAsync<bool>("b => b.matches(':hover')"), "the clicked toggle is still hovered");
        await Paints(happyPath, active, edge);
    }

    public static TheoryData<bool, string, string?, string> HeaderHovers => new()
    {
        { true, Tint, TintBorder, ".dependency-toggle" },
        { true, Tint, TintBorder, ".dep-mode-toggle" },
        { true, Tint, TintBorder, ".tag-mode-toggle" },
        { true, Tint, TintBorder, ".percentile-btn" },
        { true, Tint, TintBorder, ".fold-btn" },
        { true, Tint, TintBorder, ".export-btn" },
        { true, Tint, TintBorder, ".toolbar-right .details-radio-btn:not(.details-active)" },
        // 5.0.0: a filter row's controls take the hover tint and keep their edge (plans/OPTION_C_PLAN.md S6)
        { false, Hover, ControlBorder, ".dependency-toggle" },
        { false, Hover, ControlBorder, ".dep-mode-toggle" },
        { false, Hover, ControlBorder, ".tag-mode-toggle" },
        { false, Hover, ControlBorder, ".percentile-btn" },
        // 5.0.0: expand and collapse are icon buttons with a transparent edge (plans/OPTION_C_PLAN.md S4)
        { false, Hover, "rgba(0, 0, 0, 0)", ".fold-btn" },
        { false, Hover, "rgba(0, 0, 0, 0)", ".export-btn" }, // 5.0.0: a text button in the filter card (OPTION_C_PLAN S2)
        // 5.0.0: the Scenario Timeline toggle is a tab (plans/OPTION_C_PLAN.md S3); a tab not selected takes the hover
        // tint and has no border. The violet theme's tabs are S13's.
        { false, Hover, null, ".report-tab[aria-selected=\"false\"]" },
        // 5.0.0: a payload button takes the hover tint on its transparent edge (plans/OPTION_C_PLAN.md S8)
        { false, Hover, None, ".toolbar-right .details-radio-btn:not(.details-active)" },
    };

    [Theory]
    [MemberData(nameof(HeaderHovers))]
    public async Task A_hovered_header_control_takes_the_themes_tint(bool violet, string background, string? border, string selector)
    {
        await Open($"HeaderHover{violet}{System.Text.RegularExpressions.Regex.Replace(selector, "[^a-z]", "")}.html", violet);

        await OpenFilterRowsAsync();
        var control = Page.Locator(selector).First;
        await control.HoverAsync();
        await Paints(control, background, border);
    }

    /// <summary>The percentile buttons' active state. The timeline toggle this covered too is a tab since 5.0.0, whose
    /// selected look ReportTabsTests covers (plans/OPTION_C_PLAN.md S3).</summary>
    [Theory]
    [InlineData(true, Violet, Violet, Violet)]
    [InlineData(false, Selected, Selected, SelectedEdge)] // 5.0.0: the selected tint (S6)
    public async Task Active_percentile_buttons_take_the_themes_colours(bool violet, string active, string activeHovered, string edge)
    {
        await Open($"ActiveHeader{violet}.html", violet);

        await OpenFilterRowsAsync();
        var percentile = Page.Locator(".percentile-btn").First;
        await percentile.ClickAsync();
        await Expect(percentile).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("percentile-active"));
        await Paints(percentile, activeHovered, edge);
        await Page.Mouse.MoveAsync(0, 0);
        await Paints(percentile, active, edge);
    }

    [Theory]
    [InlineData(true, Violet, Violet)]
    [InlineData(false, Selected, SelectedEdge)] // 5.0.0: the selected tint and edge (S8)
    public async Task The_active_details_radio_takes_the_themes_colour_at_rest_and_hovered(bool violet, string active, string edge)
    {
        await Open($"DetailsRadio{violet}.html", violet);

        var radio = Page.Locator(".toolbar-right .details-radio-btn.details-active").First;
        await Paints(radio, active, edge);
        await radio.HoverAsync();
        await Paints(radio, active, edge);
    }

    public static TheoryData<bool, bool, string, string, string, string, string?> TabColours => new()
    {
        // violet, flow tracking, idle, idle hovered, active, active hovered, active edge
        // 5.0.0: the violet theme paints over the C base, whose idle tab has no fill (S8; the theme is S13's)
        { true, true, None, Tint, Violet, VioletDark, Violet },
        { true, false, None, Tint, Violet, VioletDark, Violet },
        // 5.0.0: a view tab has no fill; the picked one is marked by the accent's ink and underline (S8)
        { false, true, None, Hover, None, Hover, null },
        { false, false, None, Hover, None, Hover, null },
    };

    [Theory]
    [MemberData(nameof(TabColours))]
    public async Task Diagram_tabs_take_the_themes_colours_with_or_without_internal_flow_tracking(
        bool violet, bool internalFlowTracking, string idle, string idleHovered, string active, string activeHovered, string? activeEdge)
    {
        await Open($"Tabs{violet}{internalFlowTracking}.html", violet, internalFlowTracking);
        await OpenFirstScenarioDiagrams();

        var idleTab = Page.Locator("details.example-diagrams .diagram-toggle-btn:not(.diagram-toggle-active)").First;
        var activeTab = Page.Locator("details.example-diagrams .diagram-toggle-btn.diagram-toggle-active").First;
        await Expect(activeTab).ToBeVisibleAsync();
        await Paints(idleTab, idle);
        await Paints(activeTab, active, activeEdge);
        if (!violet) await Expect(activeTab).ToHaveCSSAsync("color", Accent);
        await idleTab.HoverAsync();
        await Paints(idleTab, idleHovered);
        await activeTab.HoverAsync();
        await Paints(activeTab, activeHovered, activeEdge);
    }

    [Theory]
    [InlineData(true, Tint)]
    [InlineData(false, Hover)]
    public async Task The_scenario_link_and_copy_button_take_the_themes_tint_when_hovered(bool violet, string tint)
    {
        await Open($"ScenarioLinks{violet}.html", violet);
        await ExpandAllFeaturesAsync();

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
    [InlineData(true, Tint, Violet, Violet, VioletDark, Tint, Violet, "rgb(139, 92, 246) 4px 0px 0px 0px inset")]
    // 5.0.0: the popup's view switch is a pair of tabs on the theme's tokens, the picked one underlined in the accent
    // (S11c), and the search match has the accent's 2px edge (S1)
    [InlineData(false, Hover, None, Accent, None, Hover, Accent, "rgb(37, 89, 199) 2px 0px 0px 0px inset")]
    public async Task Popup_controls_and_search_matches_take_the_themes_colours(bool violet, string idleHovered, string active,
        string activeEdge, string activeHovered, string listHovered, string listHoveredBorder, string searchMatchShadow)
    {
        await Open($"PopupControls{violet}.html", violet);
        await AppendPopupControls();

        var idle = Page.Locator("#probe-idle");
        await idle.HoverAsync();
        await Paints(idle, idleHovered);
        var toggle = Page.Locator("#probe-active");
        await Page.Mouse.MoveAsync(0, 0);
        await Paints(toggle, active, activeEdge);
        if (!violet)
            await Expect(toggle).ToHaveCSSAsync("box-shadow", "rgb(37, 89, 199) 0px -2px 0px 0px inset");
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
    [InlineData(false, Hover, SelectBorder)] // 5.0.0: the hover tint on the gear's edge (S8)
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
    [InlineData(true, Violet, "rgb(255, 255, 255)")]
    [InlineData(false, Selected, SelectedInk)]
    public async Task A_tapped_filter_on_a_phone_keeps_its_active_colour(bool violet, string active, string ink)
    {
        await using var phone = await OpenContextAsync(_fixture.Browser, new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            IsMobile = true,
            HasTouch = true
        });
        var page = await OpenPageAsync(phone);
        await page.GotoAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, $"PhoneTap{violet}.html", _ => { },
            stylesheet: violet ? Stylesheets.VioletThemeStyleSheet : null));
        await page.Locator("details.feature").First.WaitForAsync();

        await page.Locator(".mobile-filter-toggle").TapAsync();
        var happyPath = page.Locator(".filtering-box .happy-seg[data-hp='in']").First;
        await happyPath.TapAsync();

        await Assertions.Expect(happyPath).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.True(await happyPath.EvaluateAsync<bool>("b => b.matches(':hover')"),
            "a tap leaves the control hovered on a touch screen; without that this fact proves nothing");
        await Assertions.Expect(happyPath).ToHaveCSSAsync("background-color", active);
        await Assertions.Expect(happyPath).ToHaveCSSAsync("color", ink);
    }
}
