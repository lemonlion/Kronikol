namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests that verify note collapse/expand still works correctly after hiding
/// databases and/or steps. Reproduces the bug where hiding databases causes
/// note-index mismatch: buttons reference filtered-source indices but
/// setNoteState operates on original-source indices.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteCollapseAfterFilterTests : DiagramNotePlaywrightBase
{
    public NoteCollapseAfterFilterTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>
    /// Core bug reproduction: hide databases + steps, then collapse the response note.
    /// The response note content ("noteId", "recipeName", etc.) should disappear from the
    /// rendered SVG text, proving the note actually collapsed.
    /// </summary>
    [Fact]
    public async Task Response_note_collapses_after_hiding_databases_and_steps()
    {
        await NavigateAndRender("NoteCollapseDbSteps.html");

        // Verify response note content is visible before any toggles
        await AssertSvgContainsText("noteId");

        // Hide databases
        await ToggleAndWaitForFilter("databases", "mongoDB");
        // Hide steps
        await ToggleAndWaitForFilter("steps", "stepDelimiter");

        // Verify the response note is still visible (only DB note removed)
        await AssertSvgContainsText("noteId");

        // Now collapse the response note by double-clicking it
        // The response note is the second note (index 1 after DB note removed)
        var htmlBefore = await GetSvgHtml();
        await DoubleClickNoteByIndex(1);
        await WaitForSvgReRender(htmlBefore);

        // After collapsing, the full response JSON should NOT be in the SVG
        var svgText = await GetAllSvgText();
        Assert.DoesNotContain("createdAt", svgText);
    }

    /// <summary>
    /// After hiding databases and collapsing the response note, expanding it again
    /// should restore the full response JSON content.
    /// </summary>
    [Fact]
    public async Task Response_note_expands_after_collapsing_with_databases_hidden()
    {
        await NavigateAndRender("NoteExpandDbHidden.html");

        // Hide databases
        await ToggleAndWaitForFilter("databases", "mongoDB");

        // Collapse the response note (index 1 after filter)
        var html1 = await GetSvgHtml();
        await DoubleClickNoteByIndex(1);
        await WaitForSvgReRender(html1);

        // Verify it collapsed
        var svgAfterCollapse = await GetAllSvgText();
        Assert.DoesNotContain("createdAt", svgAfterCollapse);

        // Expand it again
        var html2 = await GetSvgHtml();
        await DoubleClickNoteByIndex(1);
        await WaitForSvgReRender(html2);

        // Verify it expanded — full content should be back
        var svgAfterExpand = await GetAllSvgText();
        Assert.Contains("createdAt", svgAfterExpand);
    }

    /// <summary>
    /// After hiding databases, collapsing the request note (index 0) should still
    /// work correctly — it's not shifted by the database filter removal.
    /// </summary>
    [Fact]
    public async Task Request_note_collapses_after_hiding_databases()
    {
        await NavigateAndRender("NoteCollapseReqDbHidden.html");

        // Verify request note content is visible (requestPriority is unique to the request note)
        await AssertSvgContainsText("requestPriority");

        // Hide databases
        await ToggleAndWaitForFilter("databases", "mongoDB");

        // Collapse the request note (index 0, unaffected by filter)
        var htmlBefore = await GetSvgHtml();
        await DoubleClickNoteByIndex(0);
        await WaitForSvgReRender(htmlBefore);

        // After collapsing, the request-only field should be gone from SVG
        var svgText = await GetAllSvgText();
        Assert.DoesNotContain("requestPriority", svgText);
    }

    /// <summary>
    /// After hiding only steps (not databases), note collapse should still work
    /// because step delimiters don't affect note block parsing.
    /// </summary>
    [Fact]
    public async Task Response_note_collapses_after_hiding_only_steps()
    {
        await NavigateAndRender("NoteCollapseStepsOnly.html");

        // Hide steps
        await ToggleAndWaitForFilter("steps", "stepDelimiter");

        // The DB note is still present — response is at index 2
        // Collapse the response note (last note)
        var noteCount = await GetNoteHoverRectCount();
        var htmlBefore = await GetSvgHtml();
        await DoubleClickNoteByIndex(noteCount - 1);
        await WaitForSvgReRender(htmlBefore);

        var svgText = await GetAllSvgText();
        Assert.DoesNotContain("createdAt", svgText);
    }

    /// <summary>
    /// Verifies the noteSteps mapping uses original-source indices.
    /// After hiding databases and collapsing the response note, check that
    /// _noteSteps has the collapse state at the correct original index (2),
    /// not the filtered index (1).
    /// </summary>
    [Fact]
    public async Task NoteSteps_uses_original_index_after_database_filter()
    {
        await NavigateAndRender("NoteStepsIndex.html");

        // Hide databases
        await ToggleAndWaitForFilter("databases", "mongoDB");

        // Collapse the response note (filtered index 1)
        var htmlBefore = await GetSvgHtml();
        await DoubleClickNoteByIndex(1);
        await WaitForSvgReRender(htmlBefore);

        // Check that _noteSteps[2] is 0 (collapsed) — the ORIGINAL index for the response note
        var step2 = await Page.EvaluateAsync<int?>("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && c._noteSteps ? (c._noteSteps[2] ?? null) : null;
            }
        """);
        Assert.Equal(0, step2);
    }

    [Fact]
    public async Task DataPlantuml_source_reflects_collapsed_note_after_database_filter()
    {
        await NavigateAndRender("DataPlantumlCollapse.html");

        // Hide databases
        await ToggleAndWaitForFilter("databases", "mongoDB");

        // Collapse the response note (filtered index 1)
        var htmlBefore = await GetSvgHtml();
        await DoubleClickNoteByIndex(1);
        await WaitForSvgReRender(htmlBefore);

        // The data-plantuml source should not contain createdAt
        var dataPlantuml = await GetDataPlantuml();
        Assert.DoesNotContain("createdAt", dataPlantuml);

        // Visible <text> elements should not contain createdAt
        var visibleText = await GetVisibleSvgText();
        Assert.DoesNotContain("createdAt", visibleText);
    }

    /// <summary>
    /// After hiding databases, collapsing a note, then showing databases again,
    /// the correct note should remain collapsed.
    /// </summary>
    [Fact]
    public async Task Note_state_survives_database_toggle_cycle()
    {
        await NavigateAndRender("NoteStateToggleCycle.html");

        // Hide databases
        await ToggleAndWaitForFilter("databases", "mongoDB");

        // Collapse the response note (filtered index 1)
        var html1 = await GetSvgHtml();
        await DoubleClickNoteByIndex(1);
        await WaitForSvgReRender(html1);

        // Verify it collapsed
        Assert.DoesNotContain("createdAt", await GetAllSvgText());

        // Show databases again
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (!c || c._noteRendering || window._plantumlRendering) return false;
                var src = c.getAttribute('data-plantuml');
                return src && src.includes('mongoDB');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });
        await WaitForNoteElements();

        // The response note (original index 2) should still be collapsed
        var svgAfterRestore = await GetAllSvgText();
        Assert.DoesNotContain("createdAt", svgAfterRestore);
        // But the DB response note (n=1) should be visible again (it was never collapsed)
        Assert.Contains("n=1", svgAfterRestore);
    }

    /// <summary>
    /// Uses the existing WideDatabaseParticipant source which has a similar pattern
    /// (database + step delimiters + eventNote). Verifies the response note collapses
    /// after hiding databases.
    /// </summary>
    [Fact]
    public async Task Wide_database_response_note_collapses_after_hiding_databases()
    {
        await Page.GotoReportAsync(GenerateWideDatabaseParticipantReport("WideDbNoteCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // Verify response content is visible
        await AssertSvgContainsText("updatedAt");

        // Hide databases
        await ToggleAndWaitForFilter("databases", "spanner");

        // Find and collapse the response note (should be the last note after DB removal)
        var noteCount = await GetNoteHoverRectCount();
        var htmlBefore = await GetSvgHtml();
        await DoubleClickNoteByIndex(noteCount - 1);
        await WaitForSvgReRender(htmlBefore);

        // The response JSON should be collapsed
        var svgText = await GetAllSvgText();
        Assert.DoesNotContain("updatedAt", svgText);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task NavigateAndRender(string fileName)
    {
        await Page.GotoReportAsync(GenerateDatabaseStepNoteCollapseReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private async Task ToggleAndWaitForFilter(string toggleName, string absentToken)
    {
        await Page.Locator($"button[data-toggle='{toggleName}']").First.ClickAsync();
        await Page.WaitForFunctionAsync($$"""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (!c || c._noteRendering || window._plantumlRendering) return false;
                var src = c.getAttribute('data-plantuml');
                return src && !src.includes('{{absentToken}}');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });
        await WaitForNoteElements();
    }

    private async Task DoubleClickNoteByIndex(int index)
    {
        await Page.Locator(".note-hover-rect").Nth(index).EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");
    }

    private async Task<string> GetAllSvgText()
    {
        return await Page.EvaluateAsync<string>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                if (!svg) return '';
                return Array.from(svg.querySelectorAll('text')).map(t => t.textContent).join(' ');
            }
        """);
    }

    private async Task<string> GetVisibleSvgText()
    {
        return await Page.EvaluateAsync<string>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                if (!svg) return '';
                return Array.from(svg.querySelectorAll('text')).map(t => t.textContent).join(' ');
            }
        """);
    }

    private async Task AssertSvgContainsText(string text)
    {
        var svgText = await GetAllSvgText();
        Assert.Contains(text, svgText);
    }

    private async Task<int> GetNoteHoverRectCount()
    {
        return await Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.note-hover-rect').length");
    }

    private async Task<string> GetDataPlantuml()
    {
        return await Page.EvaluateAsync<string>("""
            () => document.querySelector('[data-diagram-type="plantuml"]').getAttribute('data-plantuml') || ''
        """);
    }
}
