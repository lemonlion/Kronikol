namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class DiagramNoteBasicTests : DiagramNotePlaywrightBase
{
    public DiagramNoteBasicTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Note_toggle_icons_appear_after_render()
    {
        await Page.GotoAsync(GenerateReport("NoteToggleIcons.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        var count = await Page.Locator(".note-toggle-icon").CountAsync();
        Assert.True(count > 0, "Note toggle icons should exist after diagram renders");
    }

    [Fact]
    public async Task Note_hover_rects_exist_after_render()
    {
        await Page.GotoAsync(GenerateReport("NoteHoverRects.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        Assert.True(await Page.Locator(".note-hover-rect").CountAsync() > 0);
    }

    [Fact]
    public async Task Scenario_truncated_is_active_by_default()
    {
        await Page.GotoAsync(GenerateReport("NoteScenarioTruncDefault.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var truncBtn = Page.Locator(".diagram-toggle .details-radio-btn[data-state='truncated']");
        await Expect(truncBtn.First).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));
    }

    [Fact]
    public async Task Clicking_scenario_expanded_activates_it()
    {
        await Page.GotoAsync(GenerateReport("NoteScenarioExpanded.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var expandBtn = Page.Locator(".diagram-toggle .details-radio-btn[data-state='expanded']").First;
        await expandBtn.ClickAsync();
        await Expect(expandBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));

        var truncBtn = Page.Locator(".diagram-toggle .details-radio-btn[data-state='truncated']").First;
        var cls = await truncBtn.GetAttributeAsync("class");
        Assert.DoesNotContain("details-active", cls!);
    }

    [Fact]
    public async Task Clicking_scenario_collapsed_activates_it()
    {
        await Page.GotoAsync(GenerateReport("NoteScenarioCollapsed.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var collapseBtn = Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First;
        await collapseBtn.ClickAsync();
        await Expect(collapseBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));
    }

    [Fact]
    public async Task Scenario_line_count_dropdown_exists()
    {
        await Page.GotoAsync(GenerateReport("NoteLineDropdown.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var select = Page.Locator(".diagram-toggle .truncate-lines-select").First;
        var value = await select.InputValueAsync();
        Assert.Equal("40", value);
    }

    [Fact]
    public async Task Changing_scenario_line_count_updates_selected_value()
    {
        await Page.GotoAsync(GenerateReport("NoteLineChange.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var select = Page.Locator(".diagram-toggle .truncate-lines-select").First;
        Assert.Equal("40", await select.InputValueAsync());
        await select.SelectOptionAsync("3");
        Assert.Equal("3", await select.InputValueAsync());
    }

    [Fact]
    public async Task Scenario_headers_hidden_is_the_start_state()
    {
        // 4.0.0 (plans/V4_PLAN.md R8); headers started shown through 3.x.
        await Page.GotoAsync(GenerateReport("NoteHeadersDefault.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var headersBtn = Page.Locator(".diagram-toggle .toggle-btn[data-toggle='headers']").First;
        await Expect(headersBtn).ToHaveTextAsync("Headers Hidden");
        await Expect(headersBtn).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));
    }

    [Fact]
    public async Task Clicking_scenario_headers_shows_then_hides_them()
    {
        await Page.GotoAsync(GenerateReport("NoteHeadersHidden.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var headersBtn = Page.Locator(".diagram-toggle .toggle-btn[data-toggle='headers']").First;
        await headersBtn.ClickAsync();
        await Expect(headersBtn).ToHaveTextAsync("Headers Shown");
        await Expect(headersBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));

        await headersBtn.ClickAsync();
        await Expect(headersBtn).ToHaveTextAsync("Headers Hidden");
        var headersBtnClass = await headersBtn.GetAttributeAsync("class");
        Assert.DoesNotContain("details-active", headersBtnClass!);
    }

    [Fact]
    public async Task Double_click_on_note_hover_rect_cycles_state()
    {
        await Page.GotoAsync(GenerateReport("NoteDblClick.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await DoubleClickFirstNoteAndWait();
        // If we got here without timeout, SVG re-rendered (state changed)
    }

    [Fact]
    public async Task Collapsed_note_shows_plus_button_in_top_right()
    {
        await Page.GotoAsync(GenerateReport("NotePlusCollapsed.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var scenario = Page.Locator("details.scenario").First;
        await scenario.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").ClickAsync();

        await scenario.Locator("[data-note-btn='plus']").First.WaitForAsync(new() { Timeout = 10000 });
        Assert.True(await scenario.Locator("[data-note-btn='plus']").CountAsync() > 0);
        Assert.Equal(0, await scenario.Locator("[data-note-btn='minus']").CountAsync());
    }

    [Fact]
    public async Task Truncated_note_shows_minus_button_not_plus()
    {
        await Page.GotoAsync(GenerateReport("NoteMinusTruncated.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await Page.Locator("[data-note-btn='minus']").First.WaitForAsync(new() { Timeout = 10000 });
        Assert.True(await Page.Locator("[data-note-btn='minus']").CountAsync() > 0);
        Assert.Equal(0, await Page.Locator("[data-note-btn='plus']").CountAsync());
    }

    [Fact]
    public async Task Clicking_plus_button_expands_note_and_shows_minus()
    {
        await Page.GotoAsync(GenerateReport("NotePlusClick.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await SetScenarioState("collapsed");
        await Page.Locator("[data-note-btn='plus']").First.WaitForAsync(new() { Timeout = 10000 });

        var minusBefore = await Page.Locator("[data-note-btn='minus']").CountAsync();
        await ClickNoteButton("[data-note-btn='plus']");
        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"minus\"]').length > {minusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Report_level_expanded_activates_for_all_scenarios()
    {
        await Page.GotoAsync(GenerateReport("NoteReportExpanded.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']").ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var btns = document.querySelectorAll('.diagram-toggle .details-radio-btn[data-state="expanded"]');
                return btns.length > 0 && Array.from(btns).every(b => b.classList.contains('details-active'));
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Minus_button_from_expanded_goes_to_collapsed()
    {
        await ExpandAndRenderLongNoteDiagram("MinusExpToColl.html");
        await SetScenarioState("expanded");

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await ClickNoteButton("[data-note-btn='minus']");
        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Minus_button_from_truncated_goes_to_collapsed()
    {
        await ExpandAndRenderLongNoteDiagram("MinusTruncToColl.html");
        await SetScenarioState("truncated");

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await ClickNoteButton("[data-note-btn='minus']");
        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }
}