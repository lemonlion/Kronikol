using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The right-click menu on a note reads the note that was clicked, whatever the filters hide. With Databases hidden
/// the database's response note is not drawn, so the API's response is the second note drawn but the third in the
/// diagram's source: the menu read the third drawn position's neighbour in the source (the database's "n=1"), and the
/// state, format and YAML of that note, where the note's own buttons read the right one (4.26.8).
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteContextMenuIndexTests : DiagramNotePlaywrightBase
{
    public NoteContextMenuIndexTests(PlaywrightFixture fixture) : base(fixture) { }

    // The fixture's notes: 0 the request, 1 the database's "n=1", 2 the API's response (noteId abc-123).
    // With Databases hidden the drawn notes are 0 the request and 1 the API's response.
    private const int ApiResponseDrawnIndex = 1;

    private async Task NavigateAndRender(string fileName)
    {
        await Page.GotoAsync(GenerateDatabaseStepNoteCollapseReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
    }

    private async Task HideDatabases(string absentToken = "mongoDB")
    {
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            (token) => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (!c || c._noteRendering || window._plantumlRendering) return false;
                var src = c.getAttribute('data-plantuml');
                return src && !src.includes(token);
            }
        """, absentToken, new() { Timeout = 15000, PollingInterval = 200 });
        await WaitForNoteElements();
    }

    private async Task<string[]> OpenMenuOnNote(int drawnIndex)
    {
        await DispatchContextMenu(Page.Locator(".note-hover-rect").Nth(drawnIndex));
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        return (await Page.Locator(".diagram-ctx-menu > div").AllTextContentsAsync())
            .Select(t => t.Trim()).ToArray();
    }

    private async Task<string> ClickAndReadClipboard(ILocator item)
    {
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();
        return await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
    }

    private Task<string> CopyBoxText() =>
        ClickAndReadClipboard(Page.Locator(".diagram-ctx-menu").GetByText("Copy box text", new() { Exact = true }));

    private async Task<string> CopyFromBoxTextSubmenu(string itemText)
    {
        var parent = Page.Locator(".diagram-ctx-menu .submenu-parent", new() { HasTextString = "Copy box text" });
        await parent.HoverAsync();
        return await ClickAndReadClipboard(parent.Locator(".submenu").GetByText(itemText));
    }

    [Fact]
    public async Task Copy_box_text_with_databases_hidden_copies_the_note_right_clicked()
    {
        await NavigateAndRender("NoteMenuIdx_DbHiddenCopy.html");
        await HideDatabases();

        await OpenMenuOnNote(ApiResponseDrawnIndex);
        var clipboard = await CopyBoxText();

        Assert.Contains("abc-123", clipboard);
        Assert.DoesNotContain("n=1", clipboard);
    }

    [Fact]
    public async Task Menu_with_databases_hidden_reads_the_state_of_the_note_right_clicked()
    {
        await NavigateAndRender("NoteMenuIdx_DbHiddenState.html");
        await HideDatabases();

        // Collapse the API's response with its own gesture: the buttons write its state under its source index.
        var htmlBefore = await GetSvgHtml();
        await Page.Locator(".note-hover-rect").Nth(ApiResponseDrawnIndex).EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");
        await WaitForSvgReRender(htmlBefore);

        var items = await OpenMenuOnNote(ApiResponseDrawnIndex);
        Assert.Contains(items, i => i.StartsWith("Copy box text") && i.Contains("Copy full box text"));

        var full = await CopyFromBoxTextSubmenu("Copy full box text");
        Assert.Contains("2026-05-17T16:00:50Z", full);
        Assert.DoesNotContain("n=1", full);
    }

    /// <summary>The note's format is kept under its source index too: after its J/Y button flips it, the copy
    /// is in the format the note is drawn in, whichever the report started with.</summary>
    [Fact]
    public async Task Copy_box_text_with_databases_hidden_copies_the_note_in_its_drawn_format()
    {
        await NavigateAndRender("NoteMenuIdx_DbHiddenFormat.html");
        await HideDatabases();
        await ClickNoteFormatButton(ApiResponseDrawnIndex);
        var painted = await Page.EvaluateAsync<string>($$"""
            () => window._findNoteGroups(document.querySelector('[data-diagram-type="plantuml"] svg'))[{{ApiResponseDrawnIndex}}]
                .texts.map(t => t.textContent).join(' ')
        """);
        var drawnAsJson = painted.Contains("\"noteId\"");

        await OpenMenuOnNote(ApiResponseDrawnIndex);
        var clipboard = await CopyBoxText();

        Assert.Contains(drawnAsJson ? "\"noteId\": \"abc-123\"" : "noteId: abc-123", clipboard);
        Assert.DoesNotContain(drawnAsJson ? "noteId: abc-123" : "\"noteId\"", clipboard);
    }

    // ── A note in a later fragment ──

    private async Task NavigateToFragments(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithDatabaseNotesAcrossFragments(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await HideDatabases("Select item");
    }

    /// <summary>Waits for the diagram to be drawn in fragments, one of them holding a note with the text, then marks
    /// that note's hover rect and returns the fragment's position (0 is the first).</summary>
    /// <summary>Marks the diagram's drawn fragments as old, so <see cref="MarkFragmentNote"/> waits for the ones the
    /// next render draws (a render replaces every fragment's SVG).</summary>
    private Task MarkDrawnFragmentsOld() => Page.EvaluateAsync(
        "() => document.querySelectorAll('[data-diagram-type=\"plantuml\"] .puml-fragment svg').forEach(s => s._old = true)");

    private async Task<int> MarkFragmentNote(string token)
    {
        var handle = await Page.WaitForFunctionAsync("""
            (token) => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (!c || c._noteRendering || window._plantumlRendering) return false;
                var frags = Array.from(c.querySelectorAll('.puml-fragment'));
                if (frags.length < 2 || frags.some(f => !f.querySelector('svg') || f.querySelector('svg')._old)) return false;
                document.querySelectorAll('[data-test-target]').forEach(e => e.removeAttribute('data-test-target'));
                for (var fi = 0; fi < frags.length; fi++) {
                    var svg = frags[fi].querySelector('svg');
                    var grp = window._findNoteGroups(svg).find(g => g.texts.map(t => t.textContent).join(' ').replace(/\s+/g, ' ').includes(token));
                    if (!grp) continue;
                    var gb = grp.paths[0].getBoundingClientRect();
                    var cx = gb.left + gb.width / 2, cy = gb.top + gb.height / 2;
                    var hr = Array.from(svg.querySelectorAll('.note-hover-rect')).find(r => {
                        var b = r.getBoundingClientRect();
                        return cx >= b.left && cx <= b.right && cy >= b.top && cy <= b.bottom;
                    });
                    if (!hr) return false;
                    hr.setAttribute('data-test-target', '1');
                    return String(fi);
                }
                return false;
            }
        """, token, new() { Timeout = 30000, PollingInterval = 200 });
        return int.Parse(await handle.JsonValueAsync<string>());
    }

    [Fact]
    public async Task Copy_full_box_text_in_a_later_fragment_copies_the_whole_note()
    {
        await NavigateToFragments("NoteMenuIdx_FragmentFull.html");
        await MarkDrawnFragmentsOld();
        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First.ClickAsync();
        Assert.True(await MarkFragmentNote("Item 95") >= 1, "the note is in a later fragment");

        await DispatchContextMenu(Page.Locator("[data-test-target]"));
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        var full = await CopyFromBoxTextSubmenu("Copy full box text");

        // The collapsed note draws its first 60 characters; the full text is the whole response.
        Assert.Contains("Item 95", full);
        Assert.Contains("End of item 95", full);
    }

    [Fact]
    public async Task Menu_in_a_later_fragment_reads_the_state_of_the_note_right_clicked()
    {
        await NavigateToFragments("NoteMenuIdx_FragmentState.html");
        await MarkDrawnFragmentsOld();
        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First.ClickAsync();
        await MarkFragmentNote("Item 95");

        // Open that one note: every other note stays collapsed.
        await MarkDrawnFragmentsOld();
        await Page.Locator("[data-test-target]").EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");
        Assert.True(await MarkFragmentNote("End of item 95") >= 1, "the note is in a later fragment");

        var items = await OpenMenuOnMarkedNote();
        Assert.Contains("Copy box text", items);
        Assert.DoesNotContain(items, i => i.Contains("Copy full box text"));
    }

    private async Task<string[]> OpenMenuOnMarkedNote()
    {
        await DispatchContextMenu(Page.Locator("[data-test-target]"));
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        return (await Page.Locator(".diagram-ctx-menu > div").AllTextContentsAsync()).Select(t => t.Trim()).ToArray();
    }
}
