using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests for the scenario-level and report-level Assertions Show/Hide radio buttons.
/// Verifies that toggling assertions on one scenario does NOT affect other scenarios (bug fix).
/// Also tests combinations with other radio buttons (Details, Headers) and hover buttons.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class AssertionToggleTests : DiagramNotePlaywrightBase
{
    public AssertionToggleTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateAssertionReport(string fileName) =>
        ReportTestHelper.GenerateReportWithAssertionNotes(TempDir, OutputDir, fileName);

    private async Task ExpandBothScenariosAndRender(string fileName)
    {
        await Page.GotoAsync(GenerateAssertionReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        // Render all diagrams (both scenarios)
        await RenderAllDiagramsAndWait();
    }

    private ILocator ScenarioLocator(int index) =>
        Page.Locator("details.scenario").Nth(index);

    private ILocator AssertionShowBtn(ILocator scenario) =>
        scenario.Locator(".toggle-btn[data-toggle='assertions'][data-shown='false']");

    private ILocator AssertionHideBtn(ILocator scenario) =>
        scenario.Locator(".toggle-btn[data-toggle='assertions'][data-shown='true']");

    private ILocator ReportAssertionShowBtn =>
        Page.Locator(".toolbar-right .toggle-btn[data-toggle='assertions'][data-shown='false']");

    private ILocator ReportAssertionHideBtn =>
        Page.Locator(".toolbar-right .toggle-btn[data-toggle='assertions'][data-shown='true']");

    /// <summary>
    /// Checks if the rendered SVG for a scenario contains assertion note text.
    /// This checks the actual visual output, not data-plantuml (which retains the full source).
    /// Scenario 1's assertions contain "Scenario1 status code" and scenario 2's contain "Scenario2 status code".
    /// </summary>
    private async Task<bool> SvgContainsAssertionText(ILocator scenario, string scenarioLabel)
    {
        var svg = scenario.Locator("[data-diagram-type='plantuml'] svg");
        var html = await svg.First.EvaluateAsync<string>("el => el.outerHTML");
        return html.Contains($"{scenarioLabel} status code");
    }

    /// <summary>
    /// Click a control that re-renders a diagram, and return once that render has COMPLETED.
    /// </summary>
    /// <remarks>
    /// Not "until the SVG differs from before". The render queue swaps an SVG in only when it is done, but
    /// the post-render hooks of the PREVIOUS render (the zoom button, tooltips, the note-width correction
    /// pass) mutate the old SVG on their own schedule, so under CI load a snapshot taken right after
    /// <c>RenderAllDiagramsAndWait</c> could differ before the click's render had begun - and the
    /// assertion read the old diagram (<c>Assertions_survive_details_state_change</c>, 2026-09-14). A
    /// bare wait on <c>!window._plantumlRendering</c> has the mirror defect: the click QUEUES the render,
    /// so the flag is still false for a moment afterwards. The queue's completion counter is the signal
    /// every other toggle test in this suite waits on, and now the only one this class does.
    /// </remarks>
    private async Task ClickAndWaitForRender(ILocator control)
    {
        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await control.ClickAsync();
        await WaitForRenderComplete(renderCount);
    }

    private async Task WaitForRenderComplete(int previousCount, int timeoutMs = 90000)
    {
        await Page.WaitForFunctionAsync(
            "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
            previousCount,
            new() { Timeout = timeoutMs, PollingInterval = 200 });
    }

    // === BUG FIX: Scenario-level Show should NOT affect other scenarios ===

    [Fact]
    public async Task Scenario_show_assertions_does_not_affect_other_scenario()
    {
        await ExpandBothScenariosAndRender("AssertIsolation1.html");

        var scenario1 = ScenarioLocator(0);
        var scenario2 = ScenarioLocator(1);

        // Both scenarios start with assertions hidden
        Assert.False(await SvgContainsAssertionText(scenario1, "Scenario1"));
        Assert.False(await SvgContainsAssertionText(scenario2, "Scenario2"));

        // Click Show for scenario 1 only
        await ClickAndWaitForRender(AssertionShowBtn(scenario1));

        // Scenario 1 should now show assertion text
        Assert.True(await SvgContainsAssertionText(scenario1, "Scenario1"));

        // Scenario 2 should still NOT show assertion text
        Assert.False(await SvgContainsAssertionText(scenario2, "Scenario2"));
    }

    [Fact]
    public async Task Scenario_hide_assertions_does_not_affect_other_scenario()
    {
        await ExpandBothScenariosAndRender("AssertIsolation2.html");

        var scenario1 = ScenarioLocator(0);
        var scenario2 = ScenarioLocator(1);

        // First show assertions for both via report-level
        await ReportAssertionShowBtn.ClickAsync();
        await Page.WaitForFunctionAsync(
            @"() => {
                var svgs = document.querySelectorAll('details.scenario [data-diagram-type=""plantuml""] svg');
                return svgs.length >= 2 &&
                    Array.from(svgs).every(s => s.outerHTML.includes('status code'));
            }",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        // Now hide assertions for scenario 1 only
        await ClickAndWaitForRender(AssertionHideBtn(scenario1));

        // Scenario 1 should NOT show assertion text
        Assert.False(await SvgContainsAssertionText(scenario1, "Scenario1"));

        // Scenario 2 should STILL show assertion text
        Assert.True(await SvgContainsAssertionText(scenario2, "Scenario2"));
    }

    // === Report-level Show/Hide affects all scenarios ===

    [Fact]
    public async Task Report_show_assertions_shows_for_all_scenarios()
    {
        await ExpandBothScenariosAndRender("AssertReportShow.html");

        await ReportAssertionShowBtn.ClickAsync();

        await Page.WaitForFunctionAsync(
            @"() => {
                var svgs = document.querySelectorAll('details.scenario [data-diagram-type=""plantuml""] svg');
                return svgs.length >= 2 &&
                    Array.from(svgs).every(s => s.outerHTML.includes('status code'));
            }",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        Assert.True(await SvgContainsAssertionText(ScenarioLocator(0), "Scenario1"));
        Assert.True(await SvgContainsAssertionText(ScenarioLocator(1), "Scenario2"));
    }

    [Fact]
    public async Task Report_hide_assertions_hides_for_all_scenarios()
    {
        await ExpandBothScenariosAndRender("AssertReportHide.html");

        // First show them
        await ReportAssertionShowBtn.ClickAsync();
        await Page.WaitForFunctionAsync(
            @"() => {
                var svgs = document.querySelectorAll('details.scenario [data-diagram-type=""plantuml""] svg');
                return svgs.length >= 2 &&
                    Array.from(svgs).every(s => s.outerHTML.includes('status code'));
            }",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        // Now hide them
        await ReportAssertionHideBtn.ClickAsync();
        await Page.WaitForFunctionAsync(
            @"() => {
                var svgs = document.querySelectorAll('details.scenario [data-diagram-type=""plantuml""] svg');
                return svgs.length >= 2 &&
                    Array.from(svgs).every(s => !s.outerHTML.includes('status code'));
            }",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        Assert.False(await SvgContainsAssertionText(ScenarioLocator(0), "Scenario1"));
        Assert.False(await SvgContainsAssertionText(ScenarioLocator(1), "Scenario2"));
    }

    // === Assertion toggle combined with Details radio buttons ===

    [Fact]
    public async Task Assertions_survive_details_state_change()
    {
        await ExpandBothScenariosAndRender("AssertWithDetails.html");

        var scenario1 = ScenarioLocator(0);

        // Show assertions for scenario 1
        await ClickAndWaitForRender(AssertionShowBtn(scenario1));
        Assert.True(await SvgContainsAssertionText(scenario1, "Scenario1"));

        // Now change details to Expand and wait for THAT render to complete.
        await ClickAndWaitForRender(scenario1.Locator(".details-radio-btn[data-state='expanded']"));

        // Assertions should still be visible
        Assert.True(await SvgContainsAssertionText(scenario1, "Scenario1"));
    }

    [Fact]
    public async Task Assertions_survive_details_collapse()
    {
        await ExpandBothScenariosAndRender("AssertWithCollapse.html");

        var scenario1 = ScenarioLocator(0);

        // Show assertions
        await ClickAndWaitForRender(AssertionShowBtn(scenario1));

        // Change to collapsed
        await ClickAndWaitForRender(scenario1.Locator(".details-radio-btn[data-state='collapsed']"));

        // Assertions should still be present
        Assert.True(await SvgContainsAssertionText(scenario1, "Scenario1"));
    }

    // === Assertion toggle combined with Headers radio buttons ===

    [Fact]
    public async Task Assertions_survive_headers_hide()
    {
        await ExpandBothScenariosAndRender("AssertWithHeadersHide.html");

        var scenario1 = ScenarioLocator(0);

        // Show assertions
        await ClickAndWaitForRender(AssertionShowBtn(scenario1));

        // Hide headers — headers toggle triggers a full PlantUML re-render
        await ClickAndWaitForRender(scenario1.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']"));

        // Assertions should still be present
        Assert.True(await SvgContainsAssertionText(scenario1, "Scenario1"));
    }

    [Fact]
    public async Task Headers_hide_does_not_restore_hidden_assertions()
    {
        await ExpandBothScenariosAndRender("AssertHeadersNoRestore.html");

        var scenario1 = ScenarioLocator(0);

        // Assertions start hidden (default). Toggle headers hide and wait for that render to complete -
        // Assert.False on the OLD SVG would pass without having looked at the re-rendered one.
        await ClickAndWaitForRender(scenario1.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']"));

        // Assertions should still be hidden
        Assert.False(await SvgContainsAssertionText(scenario1, "Scenario1"));
    }

    // === Radio button active-state CSS class correctness ===

    [Fact]
    public async Task Scenario_show_button_gets_active_class()
    {
        await ExpandBothScenariosAndRender("AssertActiveClass.html");

        var scenario1 = ScenarioLocator(0);

        // Initially assertions are hidden — button should NOT have details-active
        await Expect(AssertionShowBtn(scenario1)).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("details-active"));

        // Click to show assertions
        await AssertionShowBtn(scenario1).ClickAsync();

        // Button should now have details-active (and data-shown=true)
        await Expect(AssertionHideBtn(scenario1)).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("details-active"));
    }

    [Fact]
    public async Task Report_show_syncs_all_scenario_buttons()
    {
        await ExpandBothScenariosAndRender("AssertReportSync.html");

        await ReportAssertionShowBtn.ClickAsync();

        // Both scenario-level buttons should now be in "shown" state with active class
        await Expect(AssertionHideBtn(ScenarioLocator(0))).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("details-active"));
        await Expect(AssertionHideBtn(ScenarioLocator(1))).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("details-active"));
    }

    // === Scenario isolation after report-level then individual override ===

    [Fact]
    public async Task Scenario_override_after_report_show_is_isolated()
    {
        await ExpandBothScenariosAndRender("AssertOverrideIsolated.html");

        var scenario1 = ScenarioLocator(0);
        var scenario2 = ScenarioLocator(1);

        // Report-level Show all
        await ReportAssertionShowBtn.ClickAsync();
        await Page.WaitForFunctionAsync(
            @"() => {
                var svgs = document.querySelectorAll('details.scenario [data-diagram-type=""plantuml""] svg');
                return svgs.length >= 2 &&
                    Array.from(svgs).every(s => s.outerHTML.includes('status code'));
            }",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        // Now hide for scenario 1 only
        await ClickAndWaitForRender(AssertionHideBtn(scenario1));

        // Scenario 1 hidden, scenario 2 still shown
        Assert.False(await SvgContainsAssertionText(scenario1, "Scenario1"));
        Assert.True(await SvgContainsAssertionText(scenario2, "Scenario2"));
    }

    [Fact]
    public async Task Scenario_show_after_report_hide_is_isolated()
    {
        await ExpandBothScenariosAndRender("AssertShowAfterHide.html");

        var scenario1 = ScenarioLocator(0);
        var scenario2 = ScenarioLocator(1);

        // Assertions are already hidden by default; show for scenario 1 only
        await ClickAndWaitForRender(AssertionShowBtn(scenario1));

        // Scenario 1 shown, scenario 2 still hidden
        Assert.True(await SvgContainsAssertionText(scenario1, "Scenario1"));
        Assert.False(await SvgContainsAssertionText(scenario2, "Scenario2"));
    }
}
