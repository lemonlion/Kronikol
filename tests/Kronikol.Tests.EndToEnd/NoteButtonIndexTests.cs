using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests that note hover buttons affect the correct note when some notes become empty
/// (e.g., header-only notes with Headers off). Verifies that the index mapping
/// between SVG note groups and PlantUML source note blocks remains aligned.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteButtonIndexTests : PlaywrightTestBase
{
    public NoteButtonIndexTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task ExpandAndRender(string url)
    {
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private async Task HideHeaders()
    {
        await Page.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-plantuml]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var icons = document.querySelectorAll('[data-diagram-type="plantuml"] svg .note-toggle-icon');
                return icons.length > 0;
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    private async Task<int> GetNoteGroupCount() =>
        await Page.EvaluateAsync<int>("""
            () => {
                var container = document.querySelector('[data-plantuml]');
                var svg = container.querySelector('svg');
                return window._findNoteGroups(svg).length;
            }
        """);

    private async Task<int> GetNoteBlockCount() =>
        await Page.EvaluateAsync<int>("""
            () => {
                var container = document.querySelector('[data-plantuml]');
                return window._parseNoteBlocks(container._noteOriginalSource).length;
            }
        """);

    private async Task ClickMinusButtonOnNote(int noteHoverRectIndex)
    {
        await Page.EvaluateAsync("""
            (idx) => {
                var rects = document.querySelectorAll('.note-hover-rect');
                if (rects.length > idx) {
                    rects[idx].dispatchEvent(new MouseEvent('mouseenter', {bubbles: true}));
                }
            }
        """, noteHoverRectIndex);

        await Page.WaitForFunctionAsync("""
            () => {
                var icons = document.querySelectorAll('.note-toggle-icon[data-note-btn="minus"]');
                return Array.from(icons).some(i => i.style.opacity === '1');
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        await Page.EvaluateAsync("""
            () => {
                var icons = document.querySelectorAll('.note-toggle-icon[data-note-btn="minus"]');
                var visible = Array.from(icons).filter(i => i.style.opacity === '1');
                if (visible.length > 0) {
                    visible[0].querySelector('rect').dispatchEvent(new MouseEvent('click', {bubbles: true}));
                }
            }
        """);
    }

    private async Task ClickPlusButtonOnNote(int noteHoverRectIndex)
    {
        await Page.EvaluateAsync("""
            (idx) => {
                var rects = document.querySelectorAll('.note-hover-rect');
                if (rects.length > idx) {
                    rects[idx].dispatchEvent(new MouseEvent('mouseenter', {bubbles: true}));
                }
            }
        """, noteHoverRectIndex);

        await Page.WaitForFunctionAsync("""
            () => {
                var icons = document.querySelectorAll('.note-toggle-icon[data-note-btn="plus"]');
                return Array.from(icons).some(i => i.style.opacity === '1');
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        await Page.EvaluateAsync("""
            () => {
                var icons = document.querySelectorAll('.note-toggle-icon[data-note-btn="plus"]');
                var visible = Array.from(icons).filter(i => i.style.opacity === '1');
                if (visible.length > 0) {
                    visible[0].querySelector('rect').dispatchEvent(new MouseEvent('click', {bubbles: true}));
                }
            }
        """);
    }

    private async Task<int> GetNoteStepForSourceIndex(int sourceIndex) =>
        await Page.EvaluateAsync<int>(
            "() => { var container = document.querySelector('[data-plantuml]');" +
            $" return (container._noteSteps && container._noteSteps[{sourceIndex}]) || 0; }}");

    private async Task WaitForReRender()
    {
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-plantuml]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var svg = container.querySelector('svg');
                return svg && svg.querySelectorAll('.note-toggle-icon').length > 0;
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    // ═══════════════════════════════════════════════════════════
    // Index alignment: header-only note becomes empty when headers hidden
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Note_groups_and_blocks_match_with_headers_visible()
    {
        var url = ReportTestHelper.GenerateReportWithHeaderOnlyNotes(TempDir, OutputDir, "NoteIdxVisible.html");
        await ExpandAndRender(url);

        var groups = await GetNoteGroupCount();
        var blocks = await GetNoteBlockCount();
        Assert.Equal(blocks, groups);
    }

    [Fact]
    public async Task Note_groups_and_blocks_match_after_hiding_headers()
    {
        var url = ReportTestHelper.GenerateReportWithHeaderOnlyNotes(TempDir, OutputDir, "NoteIdxHidden.html");
        await ExpandAndRender(url);
        await HideHeaders();

        var groups = await GetNoteGroupCount();
        var blocks = await GetNoteBlockCount();
        Assert.Equal(blocks, groups);
    }

    [Fact]
    public async Task Collapse_button_affects_correct_note_after_hiding_headers()
    {
        var url = ReportTestHelper.GenerateReportWithHeaderOnlyNotes(TempDir, OutputDir, "NoteIdxCollapse.html");
        await ExpandAndRender(url);

        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='expanded']").First.ClickAsync();
        await WaitForReRender();
        await HideHeaders();

        var hoverRects = await Page.Locator(".note-hover-rect").CountAsync();
        Assert.True(hoverRects >= 3, $"Expected at least 3 hover rects, got {hoverRects}");

        await ClickMinusButtonOnNote(2);
        await WaitForReRender();

        var note2Step = await GetNoteStepForSourceIndex(1);
        var note3Step = await GetNoteStepForSourceIndex(2);
        Assert.NotEqual(0, note2Step);
        Assert.Equal(0, note3Step);
    }

    [Fact]
    public async Task Multiple_header_only_notes_maintain_correct_index_mapping()
    {
        var url = ReportTestHelper.GenerateReportWithMultipleHeaderOnlyNotes(TempDir, OutputDir, "NoteIdxMultiEmpty.html");
        await ExpandAndRender(url);

        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='expanded']").First.ClickAsync();
        await WaitForReRender();
        await HideHeaders();

        var groups = await GetNoteGroupCount();
        var blocks = await GetNoteBlockCount();
        Assert.Equal(blocks, groups);
    }

    [Fact]
    public async Task Collapse_last_content_note_with_multiple_empty_notes_before_it()
    {
        var url = ReportTestHelper.GenerateReportWithMultipleHeaderOnlyNotes(TempDir, OutputDir, "NoteIdxMultiCollapse.html");
        await ExpandAndRender(url);

        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='expanded']").First.ClickAsync();
        await WaitForReRender();
        await HideHeaders();

        var hoverRects = await Page.Locator(".note-hover-rect").CountAsync();
        var lastIdx = hoverRects - 1;
        Assert.True(lastIdx >= 0);

        await ClickMinusButtonOnNote(lastIdx);
        await WaitForReRender();

        var lastNoteStep = await GetNoteStepForSourceIndex(3);
        Assert.Equal(0, lastNoteStep);

        var note2Step = await GetNoteStepForSourceIndex(1);
        Assert.NotEqual(0, note2Step);
    }

    [Fact]
    public async Task Collapsed_header_only_note_still_maintains_index_alignment()
    {
        var url = ReportTestHelper.GenerateReportWithHeaderOnlyNotes(TempDir, OutputDir, "NoteIdxCollapsedGray.html");
        await ExpandAndRender(url);

        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First.ClickAsync();
        await WaitForReRender();

        var groups = await GetNoteGroupCount();
        var blocks = await GetNoteBlockCount();
        Assert.Equal(blocks, groups);
    }

    [Fact]
    public async Task Expand_specific_note_after_collapse_with_header_only_notes()
    {
        var url = ReportTestHelper.GenerateReportWithHeaderOnlyNotes(TempDir, OutputDir, "NoteIdxExpandAfter.html");
        await ExpandAndRender(url);

        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First.ClickAsync();
        await WaitForReRender();

        var hoverRects = await Page.Locator(".note-hover-rect").CountAsync();
        Assert.True(hoverRects >= 2, $"Expected at least 2 hover rects, got {hoverRects}");

        await ClickPlusButtonOnNote(0);
        await WaitForReRender();

        var note0Step = await GetNoteStepForSourceIndex(0);
        var note1Step = await GetNoteStepForSourceIndex(1);
        Assert.Equal(2, note0Step);
        Assert.Equal(0, note1Step);
    }

    [Fact]
    public async Task Double_click_note_cycles_correct_note_after_headers_hidden()
    {
        var url = ReportTestHelper.GenerateReportWithHeaderOnlyNotes(TempDir, OutputDir, "NoteIdxDblClick.html");
        await ExpandAndRender(url);

        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='expanded']").First.ClickAsync();
        await WaitForReRender();
        await HideHeaders();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.note-hover-rect').length >= 3",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        await Page.EvaluateAsync("""
            () => {
                var rects = document.querySelectorAll('.note-hover-rect');
                if (rects.length >= 3) rects[2].dispatchEvent(new MouseEvent('dblclick', {bubbles: true}));
            }
        """);
        await WaitForReRender();

        var note1Step = await GetNoteStepForSourceIndex(1);
        var note2Step = await GetNoteStepForSourceIndex(2);
        Assert.Equal(2, note1Step);
        Assert.Equal(0, note2Step);
    }
}