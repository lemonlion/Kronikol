namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests that verify note hover buttons work correctly in diagrams with
/// entity, queue, and database participant types — which generate SVG
/// path+text groups that were previously misidentified as notes, causing
/// click-on-note-N to collapse note-M instead.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class DiagramNoteMixedParticipantTests : DiagramNotePlaywrightBase
{
    public DiagramNoteMixedParticipantTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task FindNoteGroups_excludes_entity_queue_database_shapes()
    {
        await Page.GotoReportAsync(GenerateMixedParticipantNotesReport("MixedParticExclude.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // The diagram has 5 note blocks in source (2 gray-only, 1 with JSON,
        // 1 eventNote, 1 with JSON). findNoteGroups should return exactly 5
        // groups — NOT more from entity/queue/database participant shapes.
        var noteGroupCount = await Page.EvaluateAsync<int>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                return window._findNoteGroups(svg).length;
            }
        """);

        // parseNoteBlocks on the displayed source gives the expected count
        var noteBlockCount = await Page.EvaluateAsync<int>("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                var source = container._noteOriginalSource || container.getAttribute('data-plantuml');
                return window._parseNoteBlocks(source).length;
            }
        """);

        Assert.Equal(noteBlockCount, noteGroupCount);
    }

    [Fact]
    public async Task Hover_rects_match_note_count_not_participant_count()
    {
        await Page.GotoReportAsync(GenerateMixedParticipantNotesReport("MixedParticHoverRects.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        var result = await Page.EvaluateAsync<System.Text.Json.JsonElement>("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                var source = container._noteOriginalSource || container.getAttribute('data-plantuml');
                var noteBlocks = window._parseNoteBlocks(source).length;
                var hoverRects = document.querySelectorAll('.note-hover-rect').length;
                return { noteBlocks: noteBlocks, hoverRects: hoverRects };
            }
        """);

        var noteBlocks = result.GetProperty("noteBlocks").GetInt32();
        var hoverRects = result.GetProperty("hoverRects").GetInt32();

        Assert.Equal(noteBlocks, hoverRects);
    }

    [Fact]
    public async Task Minus_button_collapses_correct_note_content()
    {
        await Page.GotoReportAsync(GenerateMixedParticipantNotesReport("MixedParticMinusCorrect.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // Set to expanded state so all notes show content
        await SetScenarioState("expanded");

        // Get the first note's text content before clicking minus
        var firstNoteTextBefore = await Page.EvaluateAsync<string>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var groups = window._findNoteGroups(svg);
                if (groups.length === 0) return '';
                return groups[0].texts.map(t => t.textContent).join('|');
            }
        """);

        // Click minus on the first note
        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);
        await Page.Locator("[data-note-btn='minus']").First.WaitForAsync(
            new() { State = Microsoft.Playwright.WaitForSelectorState.Visible });
        await Page.Locator("[data-note-btn='minus'] rect").First.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}))");
        await WaitForSvgReRender(htmlBefore);

        // After collapse, the first note's _noteSteps[0] should be 0
        var firstNoteStep = await Page.EvaluateAsync<int>("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                return container._noteSteps ? (container._noteSteps[0] || 0) : -1;
            }
        """);
        Assert.Equal(0, firstNoteStep);

        // The first note group should now have different (shorter) text
        var firstNoteTextAfter = await Page.EvaluateAsync<string>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var groups = window._findNoteGroups(svg);
                if (groups.length === 0) return '';
                return groups[0].texts.map(t => t.textContent).join('|');
            }
        """);

        Assert.NotEqual(firstNoteTextBefore, firstNoteTextAfter);
    }

    [Fact]
    public async Task Second_note_unaffected_when_first_note_collapsed()
    {
        await Page.GotoReportAsync(GenerateMixedParticipantNotesReport("MixedParticIsolation.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await SetScenarioState("expanded");

        // Get second note's text before collapse
        var secondNoteTextBefore = await Page.EvaluateAsync<string>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var groups = window._findNoteGroups(svg);
                return groups.length > 1 ? groups[1].texts.map(t => t.textContent).join('|') : '';
            }
        """);

        // Collapse first note
        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);
        await Page.Locator("[data-note-btn='minus']").First.WaitForAsync(
            new() { State = Microsoft.Playwright.WaitForSelectorState.Visible });
        await Page.Locator("[data-note-btn='minus'] rect").First.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}))");
        await WaitForSvgReRender(htmlBefore);

        // Second note should have the same text
        var secondNoteTextAfter = await Page.EvaluateAsync<string>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var groups = window._findNoteGroups(svg);
                return groups.length > 1 ? groups[1].texts.map(t => t.textContent).join('|') : '';
            }
        """);

        Assert.Equal(secondNoteTextBefore, secondNoteTextAfter);
    }

    [Fact]
    public async Task Event_note_with_different_fill_is_detected()
    {
        await Page.GotoReportAsync(GenerateMixedParticipantNotesReport("MixedParticEventNote.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // The eventNote (with different background #cfecf7) should also be
        // detected as a note group since it still has a fold triangle
        var eventNoteDetected = await Page.EvaluateAsync<bool>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var groups = window._findNoteGroups(svg);
                // Check if any group has the event note fill
                return groups.some(g =>
                    g.paths.some(p => {
                        var fill = (p.getAttribute('fill') || '').toLowerCase();
                        return fill === '#cfecf7';
                    })
                );
            }
        """);

        Assert.True(eventNoteDetected, "Event note with custom fill should be detected by findNoteGroups");
    }
}
