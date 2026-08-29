using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Regression tests for the context-menu copy paths on notes: "Copy box text"
/// on a YAML-toggled note must yield the displayed YAML (the user-reported bug
/// copied the original JSON), no copy path may leak creole <c>~</c> escapes,
/// and "Copy Highlighted Text" must normalise against the displayed YAML.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class YamlNoteCopyTextTests : DiagramNotePlaywrightBase
{
    public YamlNoteCopyTextTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateEscapingFixture(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithEscapingYamlNote(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
    }

    /// <summary>Truncates the (now long) YAML note via its ▲ contract button.</summary>
    private async Task TruncateNoteViaUpArrow()
    {
        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);
        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => Array.from(i.querySelectorAll('text')).some(t => t.textContent.includes('▲')) && i.style.opacity === '1');
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
        await Page.EvaluateAsync("""
            () => {
                var up = Array.from(document.querySelectorAll('.note-toggle-icon'))
                    .find(i => Array.from(i.querySelectorAll('text')).some(t => t.textContent.includes('▲')));
                up.querySelector('rect').dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}));
            }
        """);
        await WaitForSvgReRender(htmlBefore);
    }

    private async Task OpenNoteContextMenu()
    {
        await DispatchContextMenu(Page.Locator(".note-hover-rect").First);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
    }

    private async Task<string> ClickBoxTextSubmenuItem(string itemText)
    {
        var parent = Page.Locator(".diagram-ctx-menu .submenu-parent", new() { HasTextString = "Copy box text" });
        await parent.HoverAsync();
        var item = parent.Locator(".submenu").GetByText(itemText);
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();
        return await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
    }

    // ═══════════════════════════════════════════════════════════
    // Expanded YAML note — the user-reported bug
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Copy_box_text_on_expanded_yaml_note_yields_yaml_not_json()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithJsonYamlNotes(TempDir, OutputDir, "YamlCopy_ExpandedBoxText.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        await ClickNoteFormatButton();

        await OpenNoteContextMenu();
        var menuItem = Page.Locator(".diagram-ctx-menu").GetByText("Copy box text");
        await menuItem.WaitForAsync(new() { Timeout = 5000 });
        await menuItem.ClickAsync();
        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        Assert.Contains("query: |-", clipboard);
        Assert.Contains("FROM orders o", clipboard);
        Assert.Contains("id: 9007199254740993", clipboard);
        // Not the original JSON payload
        Assert.DoesNotContain("\"query\":", clipboard);
        Assert.DoesNotContain("\\n", clipboard);
    }

    // ═══════════════════════════════════════════════════════════
    // Truncated YAML note — full/current box text
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Copy_full_box_text_on_truncated_yaml_note_yields_yaml_not_json()
    {
        await NavigateEscapingFixture("YamlCopy_TruncatedFull.html");
        await ClickNoteFormatButton();
        await TruncateNoteViaUpArrow();

        await OpenNoteContextMenu();
        var clipboard = await ClickBoxTextSubmenuItem("Copy full box text");

        Assert.Contains("url: \"https://example.com/orders\"", clipboard);
        Assert.Contains("query: |-", clipboard);
        Assert.Contains("FROM orders o", clipboard);
        Assert.DoesNotContain("~", clipboard);
        Assert.DoesNotContain("\"query\":", clipboard);
        Assert.DoesNotContain("\\n", clipboard);
    }

    [Fact]
    public async Task Copy_current_box_text_on_truncated_yaml_note_has_no_creole_escapes()
    {
        await NavigateEscapingFixture("YamlCopy_TruncatedCurrent.html");
        await ClickNoteFormatButton();
        await TruncateNoteViaUpArrow();

        await OpenNoteContextMenu();
        var clipboard = await ClickBoxTextSubmenuItem("Copy current box text");

        // The url line sits inside the first 40 truncated lines; the splice
        // escapes (~/~/) must have been removed on the way to the clipboard.
        Assert.Contains("url: \"https://example.com/orders\"", clipboard);
        Assert.Contains("query: |-", clipboard);
        Assert.DoesNotContain("~", clipboard);
        // Truncated view, not the full one
        Assert.DoesNotContain("FROM orders o", clipboard);
        Assert.Contains("...", clipboard);
    }

    // ═══════════════════════════════════════════════════════════
    // Copy Highlighted Text on a YAML note
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Copy_highlighted_text_on_yaml_note_yields_displayed_block_scalar_lines()
    {
        await NavigateEscapingFixture("YamlCopy_Highlighted.html");
        await ClickNoteFormatButton();

        // Select all text of the (expanded) note
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

        await OpenNoteContextMenu();
        var menuItem = Page.Locator(".diagram-ctx-menu").GetByText("Copy Highlighted Text");
        await menuItem.WaitForAsync(new() { Timeout = 5000 });
        await menuItem.ClickAsync();
        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        Assert.Contains("url: \"https://example.com/orders\"", clipboard);
        Assert.Contains("FROM orders o", clipboard);
        Assert.DoesNotContain("~", clipboard);
    }

    // ═══════════════════════════════════════════════════════════
    // JSON view — source creole escapes must not leak either
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Copy_box_text_in_json_view_unescapes_creole_markers()
    {
        await NavigateEscapingFixture("YamlCopy_JsonViewUnescape.html");

        // No format toggle — the JSON view's source lines carry the
        // generation-side ~/~/ escapes, which the display never shows.
        await OpenNoteContextMenu();
        var menuItem = Page.Locator(".diagram-ctx-menu").GetByText("Copy box text");
        await menuItem.WaitForAsync(new() { Timeout = 5000 });
        await menuItem.ClickAsync();
        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        Assert.Contains("\"url\": \"https://example.com/orders\"", clipboard);
        Assert.DoesNotContain("~/~/", clipboard);
    }
}
