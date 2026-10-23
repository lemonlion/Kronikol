using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests verifying that "Copy Highlighted Text" from the SVG context menu
/// preserves spaces instead of inserting newlines at word-wrap boundaries.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class CopyHighlightedTextTests : PlaywrightTestBase
{
    public CopyHighlightedTextTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateAndSetup(string fileName)
    {
        var url = ReportTestHelper.GenerateReportWithLongLineNote(TempDir, OutputDir, fileName);
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.note-hover-rect').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Copy_highlighted_text_from_note_preserves_spaces_instead_of_newlines()
    {
        await NavigateAndSetup("CopyHighlight_PreservesSpaces.html");
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        // Select all text in the first note by creating a range over SVG text elements
        await Page.EvaluateAsync("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var noteGroups = window._findNoteGroups(svg);
                if (!noteGroups || noteGroups.length === 0) throw new Error('No note groups found');
                var texts = noteGroups[0].texts;
                var range = document.createRange();
                range.setStartBefore(texts[0]);
                range.setEndAfter(texts[texts.length - 1]);
                var sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(range);
            }
        """);

        // Dispatch context menu on the first note text element
        var noteText = Page.Locator("[data-diagram-type='plantuml'] svg text").First;
        await DispatchContextMenu(noteText);

        // Click "Copy Highlighted Text"
        var menuItem = Page.Locator(".diagram-ctx-menu").GetByText("Copy Highlighted Text");
        await menuItem.WaitForAsync(new() { Timeout = 5000 });
        await menuItem.ClickAsync();

        // Read clipboard
        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        // The note has header lines + body. Copied text from full selection
        // should preserve the structure from original source (lines separated by \n)
        // but word-wrap boundaries within a single source line should be spaces, not \n.
        // The JSON body is one long line in the source — it should not have \n within it.
        Assert.Contains("orderId", clipboard);
        Assert.Contains("customerName", clipboard);
        // If word-wrapping introduced newlines, there would be \n between parts of the JSON
        // Check that the JSON portion is on a single line (no \n between "orderId" and the end)
        var jsonLine = clipboard.Split('\n')
            .FirstOrDefault(line => line.Contains("orderId"));
        Assert.NotNull(jsonLine);
        Assert.Contains("shippingAddress", jsonLine!);
    }

    [Fact]
    public async Task Copy_highlighted_text_with_real_multiline_preserves_line_breaks()
    {
        await NavigateAndSetup("CopyHighlight_PreservesReal.html");
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        // Select all text in the first note
        await Page.EvaluateAsync("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var noteGroups = window._findNoteGroups(svg);
                if (!noteGroups || noteGroups.length === 0) throw new Error('No note groups found');
                var texts = noteGroups[0].texts;
                var range = document.createRange();
                range.setStartBefore(texts[0]);
                range.setEndAfter(texts[texts.length - 1]);
                var sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(range);
            }
        """);

        var noteText = Page.Locator("[data-diagram-type='plantuml'] svg text").First;
        await DispatchContextMenu(noteText);

        var menuItem = Page.Locator(".diagram-ctx-menu").GetByText("Copy Highlighted Text");
        await menuItem.WaitForAsync(new() { Timeout = 5000 });
        await menuItem.ClickAsync();

        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        // The note has real multi-line content (gray headers + body).
        // Real line breaks between different SOURCE lines should be preserved.
        // The gray headers are in the source, so we should see them if text selection included them.
        Assert.Contains("Content-Type", clipboard);
    }

    // ── Under skinparam wrapWidth, which every generated sequence diagram carries ──
    // The engine paints each word and each space as its own <text>, and Chromium's Selection.toString()
    // puts a line break at every <text> boundary: "GET\n \n/api/items" for the label "GET /api/items".

    private async Task OpenMixedNotesDiagram(string fileName)
    {
        var url = ReportTestHelper.GenerateReportWithMixedParticipantNotes(TempDir, OutputDir, fileName);
        await OpenDiagram(url);
    }

    private async Task OpenDiagram(string url)
    {
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.note-hover-rect').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
    }

    /// <summary>
    /// Selects the painted text from the first <c>&lt;text&gt;</c> reading <paramref name="from"/> to the first one
    /// after it reading <paramref name="to"/>. The offsets put each end inside its text node, where a mouse leaves
    /// it; null selects the whole element.
    /// </summary>
    private Task SelectPaintedText(string from, string to, int? fromOffset = null, int? toOffset = null) =>
        Page.EvaluateAsync(
            """
            ([from, to, fromOffset, toOffset]) => {
                const texts = Array.from(document.querySelectorAll('[data-diagram-type="plantuml"] svg text'));
                const a = texts.findIndex(t => t.textContent === from);
                const b = texts.findIndex((t, i) => i >= a && t.textContent === to);
                if (a < 0 || b < 0) throw new Error('No painted text reads ' + JSON.stringify(a < 0 ? from : to));
                const range = document.createRange();
                if (fromOffset === null) range.setStartBefore(texts[a]); else range.setStart(texts[a].firstChild, fromOffset);
                if (toOffset === null) range.setEndAfter(texts[b]); else range.setEnd(texts[b].firstChild, toOffset);
                const sel = getSelection();
                sel.removeAllRanges();
                sel.addRange(range);
            }
            """, new object?[] { from, to, fromOffset, toOffset });

    private async Task<string> CopyHighlightedTextFrom(string paintedText)
    {
        var target = Page.Locator("[data-diagram-type='plantuml'] svg text",
            new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(paintedText) + "$") });
        await DispatchContextMenu(target.First);
        var menuItem = Page.Locator(".diagram-ctx-menu").GetByText("Copy Highlighted Text");
        await menuItem.WaitForAsync(new() { Timeout = 5000 });
        await menuItem.ClickAsync();
        return await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
    }

    [Fact]
    public async Task Copy_highlighted_text_from_a_message_label_keeps_its_spaces()
    {
        await OpenMixedNotesDiagram("CopyHighlight_MessageLabel.html");
        await SelectPaintedText("GET", "/api/items");

        var clipboard = await CopyHighlightedTextFrom("GET");

        Assert.Equal("GET /api/items", clipboard);
    }

    [Fact]
    public async Task Copy_highlighted_text_keeps_the_selection_ends_inside_words()
    {
        await OpenMixedNotesDiagram("CopyHighlight_PartialWords.html");
        await SelectPaintedText("GET", "/api/items", fromOffset: 1, toOffset: 4);

        var clipboard = await CopyHighlightedTextFrom("GET");

        Assert.Equal("ET /api", clipboard);
    }

    [Fact]
    public async Task Copy_highlighted_text_from_a_note_right_clicked_off_the_note_keeps_its_lines()
    {
        await OpenMixedNotesDiagram("CopyHighlight_OffNote.html");
        await SelectPaintedText("{", "}");

        var clipboard = await CopyHighlightedTextFrom("Caller");

        Assert.Equal("{\n  \"data\": \"value1\"\n}", clipboard.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Copy_highlighted_text_across_a_message_label_and_its_note_keeps_each_row()
    {
        await OpenMixedNotesDiagram("CopyHighlight_LabelAndNote.html");
        await SelectPaintedText("OK", "}");

        var clipboard = await CopyHighlightedTextFrom("[Content-Type=application/json]");

        Assert.Equal("OK\n[Content-Type=application/json]\n\n{\n  \"data\": \"value1\"\n}", clipboard.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Copy_highlighted_text_of_page_text_keeps_the_browsers_reading()
    {
        await OpenMixedNotesDiagram("CopyHighlight_PageText.html");
        var expected = await Page.EvaluateAsync<string>(
            """
            () => {
                const range = document.createRange();
                range.selectNodeContents(document.querySelector('details.feature > summary'));
                const sel = getSelection();
                sel.removeAllRanges();
                sel.addRange(range);
                return sel.toString().trim();
            }
            """);
        Assert.False(string.IsNullOrWhiteSpace(expected));

        var clipboard = await CopyHighlightedTextFrom("GET");

        Assert.Equal(expected.Replace("\r\n", "\n"), clipboard.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Copy_highlighted_text_across_a_width_budget_break_rejoins_the_line()
    {
        await OpenDiagram(ReportTestHelper.GenerateReportWithWrappedLineNote(TempDir, OutputDir, "CopyHighlight_WrappedLine.html"));
        await SelectPaintedText("the", "it");

        var clipboard = await CopyHighlightedTextFrom("the");

        Assert.Equal("the first half of a line the width budget broke and the second half of it", clipboard);
    }
}
