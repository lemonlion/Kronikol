using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests verifying that note hover button click behavior (plus/minus/dblclick
/// 3-state cycle) works correctly AFTER toggling headers to "hidden".
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteButtonsAfterHeaderHideTests : DiagramNotePlaywrightBase
{
    public NoteButtonsAfterHeaderHideTests(PlaywrightFixture fixture) : base(fixture) { }

    private new string GenerateReport(string fileName) =>
        GenerateLongNoteWithHeadersReport(fileName);

    private async Task ToggleHeadersHidden()
    {
        var scenario = Page.Locator("details.scenario");

        // Capture render queue counter so we can detect when the re-render queue completes
        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");

        await OpenScenarioSettingsAsync(scenario);
        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();

        // Wait for the render queue to finish processing (counter increments on completion,
        // even if the individual render was abandoned due to timeout)
        await Page.WaitForFunctionAsync(
            "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
            renderCount,
            new() { Timeout = 60000, PollingInterval = 200 });

        // Verify post-render state: note interaction elements must exist
        await Page.WaitForFunctionAsync(
            @"() => document.querySelectorAll('.note-hover-rect').length > 0 &&
                    document.querySelectorAll('.note-toggle-icon').length > 0",
            null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    private async Task ToggleHeadersShown()
    {
        var scenario = Page.Locator("details.scenario");

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");

        await OpenScenarioSettingsAsync(scenario);
        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='false']").ClickAsync();

        await Page.WaitForFunctionAsync(
            "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
            renderCount,
            new() { Timeout = 60000, PollingInterval = 200 });

        await Page.WaitForFunctionAsync(
            @"() => document.querySelectorAll('.note-hover-rect').length > 0 &&
                    document.querySelectorAll('.note-toggle-icon').length > 0",
            null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    private async Task NavigateAndSetup(string fileName)
    {
        await Page.GotoAsync(GenerateReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.note-hover-rect').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    // ═══════════════════════════════════════════════════════════
    // Baseline: buttons still exist after hiding headers
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task After_hiding_headers_note_hover_rects_still_exist()
    {
        await NavigateAndSetup("HdrHide_HoverRectsExist.html");
        await ToggleHeadersHidden();

        var count = await Page.Locator(".note-hover-rect").CountAsync();
        Assert.True(count > 0, "Note hover rects should exist after hiding headers");
    }

    [Fact]
    public async Task After_hiding_headers_note_toggle_icons_still_exist()
    {
        await NavigateAndSetup("HdrHide_ToggleIconsExist.html");
        await ToggleHeadersHidden();

        var count = await Page.Locator(".note-toggle-icon").CountAsync();
        Assert.True(count > 0, "Note toggle icons should exist after hiding headers");
    }

    // ═══════════════════════════════════════════════════════════
    // 3-state dblclick cycle with headers hidden
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Headers_hidden_dblclick_from_expanded_goes_to_truncated()
    {
        await NavigateAndSetup("HdrHide_DblClickExpToTrunc.html");
        await SetScenarioState("expanded");
        await ToggleHeadersHidden();

        await DoubleClickFirstNoteAndWait();

        var minusCount = await Page.Locator("[data-note-btn='minus']").CountAsync();
        Assert.True(minusCount > 0, "Minus button should be present in truncated state");
    }

    [Fact]
    public async Task Headers_hidden_dblclick_from_truncated_goes_to_collapsed()
    {
        await NavigateAndSetup("HdrHide_DblClickTruncToColl.html");
        await SetScenarioState("truncated");
        await ToggleHeadersHidden();

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await DoubleClickFirstNoteAndWait();

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Headers_hidden_dblclick_from_collapsed_goes_to_truncated()
    {
        await NavigateAndSetup("HdrHide_DblClickCollToTrunc.html");
        await SetScenarioState("collapsed");
        await ToggleHeadersHidden();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-note-btn=\"plus\"]').length >= 2",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        Assert.True(plusBefore >= 2);

        await DoubleClickFirstNoteAndWait();

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length < {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Headers_hidden_full_3_state_cycle()
    {
        await NavigateAndSetup("HdrHide_Full3StateCycle.html");
        await SetScenarioState("expanded");
        await ToggleHeadersHidden();

        // expanded → truncated
        await DoubleClickFirstNoteAndWait();

        // truncated → collapsed
        await DoubleClickFirstNoteAndWait();
        await WaitForNoteElements();
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > 0",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        // collapsed → truncated
        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await DoubleClickFirstNoteAndWait();
        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length < {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    // ═══════════════════════════════════════════════════════════
    // Button clicks with headers hidden
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Headers_hidden_plus_button_from_collapsed_goes_to_truncated()
    {
        await NavigateAndSetup("HdrHide_PlusCollToTrunc.html");
        await SetScenarioState("collapsed");
        await ToggleHeadersHidden();

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await ClickNoteButton("[data-note-btn='plus']");

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length < {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Headers_hidden_minus_button_from_expanded_goes_to_collapsed()
    {
        await NavigateAndSetup("HdrHide_MinusExpToColl.html");
        await SetScenarioState("expanded");
        await ToggleHeadersHidden();

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await ClickNoteButton("[data-note-btn='minus']");

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Headers_hidden_minus_button_from_truncated_goes_to_collapsed()
    {
        await NavigateAndSetup("HdrHide_MinusTruncToColl.html");
        await SetScenarioState("truncated");
        await ToggleHeadersHidden();

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await ClickNoteButton("[data-note-btn='minus']");

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    // ═══════════════════════════════════════════════════════════
    // Up arrow with headers hidden
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Headers_hidden_up_arrow_visible_when_expanded()
    {
        await NavigateAndSetup("HdrHide_UpArrowVisible.html");
        await SetScenarioState("expanded");
        await ToggleHeadersHidden();

        await HoverNoteRect(0);

        await Page.WaitForFunctionAsync("""
            () => {
                var icons = document.querySelectorAll('.note-toggle-icon');
                return Array.from(icons).some(i =>
                    /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || '')
                    && i.style.opacity !== '0');
            }
        """, null, new() { Timeout = 30000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Headers_hidden_up_arrow_click_goes_to_truncated()
    {
        await NavigateAndSetup("HdrHide_UpArrowToTrunc.html");
        await SetScenarioState("expanded");
        await ToggleHeadersHidden();

        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);

        await Page.WaitForFunctionAsync("""
            () => {
                var icons = document.querySelectorAll('.note-toggle-icon');
                return Array.from(icons).some(i =>
                    /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || '')
                    && i.style.opacity !== '0');
            }
        """, null, new() { Timeout = 10000, PollingInterval = 200 });

        await Page.EvaluateAsync("""
            () => {
                var icons = Array.from(document.querySelectorAll('.note-toggle-icon'));
                var upArrow = icons.find(i =>
                    /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || ''));
                if (upArrow) {
                    var rect = upArrow.querySelector('rect');
                    if (rect) rect.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}));
                }
            }
        """);
        await WaitForSvgReRender(htmlBefore);

        // ▲ should disappear after clicking
        var hasUpArrow = await Page.EvaluateAsync<bool>("""
            () => {
                var icons = Array.from(document.querySelectorAll('.note-toggle-icon'));
                return icons.some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || ''));
            }
        """);
        Assert.False(hasUpArrow);
    }

    // ═══════════════════════════════════════════════════════════
    // Headers toggle mid-interaction
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Hiding_headers_after_manual_note_state_change_preserves_state()
    {
        await NavigateAndSetup("HdrHide_PreservesManualState.html");
        await SetScenarioState("expanded");

        await DoubleClickFirstNoteAndWait(); // expanded → truncated

        await ToggleHeadersHidden();

        var minusAfter = await Page.Locator("[data-note-btn='minus']").CountAsync();
        Assert.True(minusAfter > 0,
            "Minus button should still be present after hiding headers (note stayed truncated)");
    }

    [Fact]
    public async Task Showing_headers_after_hidden_preserves_note_button_behavior()
    {
        await NavigateAndSetup("HdrHide_ShowAfterHideWorks.html");
        await SetScenarioState("expanded");
        await ToggleHeadersHidden();

        await DoubleClickFirstNoteAndWait(); // expanded → truncated

        await ToggleHeadersShown();

        // truncated → collapsed
        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await DoubleClickFirstNoteAndWait();

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }
}