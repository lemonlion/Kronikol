using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests verifying that the ▼ expand arrow does NOT appear on notes that are
/// fully visible when headers are hidden (i.e., non-gray lines fit within limit).
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteExpandArrowHeaderHiddenTests : DiagramNotePlaywrightBase
{
    public NoteExpandArrowHeaderHiddenTests(PlaywrightFixture fixture) : base(fixture) { }

    private new string GenerateReport(string fileName) =>
        GenerateBarelyOverLimitWithHeadersReport(fileName);

    private async Task ToggleHeadersHidden()
    {
        var scenario = Page.Locator("details.scenario");
        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");

        await OpenScenarioSettingsAsync(scenario);
        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();

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
    // When headers are hidden and effective body lines ≤ limit,
    // the note should be in expanded state (step 2) with NO ▼
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Headers_hidden_no_expand_arrow_when_body_fits_within_limit()
    {
        await NavigateAndSetup("HdrHide_NoExpandArrow.html");

        // With headers visible, total lines > 40, so note IS truncated (step 1) — ▼ is correct
        await HoverNoteRect(0);
        var hasDownArrowBefore = await Page.EvaluateAsync<bool>("""
            () => {
                var icons = Array.from(document.querySelectorAll('.note-toggle-icon'));
                return icons.some(i => i.getAttribute('data-note-btn') === 'expand-bottom');
            }
        """);
        Assert.True(hasDownArrowBefore, "Down arrow should be present with headers visible (note is truncated)");

        // Now hide headers — effective body lines (38) ≤ 40 limit, so note should auto-expand
        await ToggleHeadersHidden();

        await HoverNoteRect(0);

        // After headers hidden: note should be in expanded state, no ▼ arrow
        var hasDownArrowAfter = await Page.EvaluateAsync<bool>("""
            () => {
                var icons = Array.from(document.querySelectorAll('.note-toggle-icon'));
                return icons.some(i =>
                    i.getAttribute('data-note-btn') === 'expand-bottom'
                    && i.style.opacity !== '0');
            }
        """);
        Assert.False(hasDownArrowAfter, "Down arrow should NOT be present after hiding headers (body fits within limit)");
    }

    [Fact]
    public async Task Headers_visible_note_remains_truncated_when_total_lines_exceed_limit()
    {
        await NavigateAndSetup("HdrVisible_StillTruncated.html");

        // With headers visible, total content lines = 44 > 40, note IS truncated
        await HoverNoteRect(0);
        var hasDownArrow = await Page.EvaluateAsync<bool>("""
            () => {
                var icons = Array.from(document.querySelectorAll('.note-toggle-icon'));
                return icons.some(i =>
                    i.getAttribute('data-note-btn') === 'expand-bottom'
                    && i.style.opacity !== '0');
            }
        """);
        Assert.True(hasDownArrow, "Down arrow should be present with headers visible (total lines > limit)");
    }

    [Fact]
    public async Task Headers_hidden_note_has_minus_button_for_collapse()
    {
        await NavigateAndSetup("HdrHide_HasMinusBtn.html");
        await ToggleHeadersHidden();

        // Note should be expanded (step 2) — minus button allows collapsing
        await HoverNoteRect(0);
        var hasMinusButton = await Page.EvaluateAsync<bool>("""
            () => {
                var icons = Array.from(document.querySelectorAll('.note-toggle-icon[data-note-btn="minus"]'));
                return icons.some(i => i.style.opacity !== '0');
            }
        """);
        Assert.True(hasMinusButton, "Minus button should be present after hiding headers (note is expanded)");
    }

    [Fact]
    public async Task Headers_hidden_no_ellipsis_in_note_content()
    {
        await NavigateAndSetup("HdrHide_NoEllipsis.html");
        await ToggleHeadersHidden();

        // No "..." should appear in the SVG note content since all body fits
        var hasEllipsis = await Page.EvaluateAsync<bool>("""
            () => {
                var texts = Array.from(document.querySelectorAll('[data-diagram-type="plantuml"] svg text'));
                return texts.some(t => t.textContent.trim() === '...');
            }
        """);
        Assert.False(hasEllipsis, "No ellipsis should appear when all body content fits within limit");
    }
}
