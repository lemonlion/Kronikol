namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests that continuation notes ("..Continued From Previous Diagram..") have
/// working hover buttons and context menu "Copy box text" option, both for
/// server-side-split diagrams (separate containers) and client-side-chunked
/// diagrams (puml-fragment divs inside one container).
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class ContinuationNoteInteractivityTests : DiagramNotePlaywrightBase
{
    public ContinuationNoteInteractivityTests(PlaywrightFixture fixture) : base(fixture) { }

    // ── Separate container (server-side split) ──

    [Fact]
    public async Task Continuation_note_in_separate_container_has_copy_box_text_in_context_menu()
    {
        await Page.GotoAsync(GenerateThreeDiagramSplitReport("ContNoteCopyBoxSeparate.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllThreeDiagramsAndWait();
        await WaitForNoteElements();

        var menuResult = await Page.EvaluateAsync<string>("""
            (() => {
                var hr = document.querySelector('#puml-2 .note-hover-rect');
                if (!hr) return 'NO_HOVER_RECT';
                var rect = hr.getBoundingClientRect();
                var evt = new MouseEvent('contextmenu', {
                    bubbles: true, cancelable: true,
                    clientX: rect.left + rect.width / 2,
                    clientY: rect.top + rect.height / 2,
                    pageX: rect.left + rect.width / 2 + window.scrollX,
                    pageY: rect.top + rect.height / 2 + window.scrollY
                });
                hr.dispatchEvent(evt);

                var menu = document.querySelector('.diagram-ctx-menu');
                if (!menu) return 'NO_MENU';
                var items = Array.from(menu.children)
                    .map(function(i) { return i.textContent.trim(); })
                    .filter(function(i) { return i; });
                return 'ITEMS:' + items.join('|');
            })()
        """);

        Assert.DoesNotContain("NO_MENU", menuResult);
        Assert.Contains("Copy box text", menuResult);
    }

    [Fact]
    public async Task Continuation_note_in_separate_container_hover_shows_buttons()
    {
        await Page.GotoAsync(GenerateThreeDiagramSplitReport("ContNoteHoverSeparate.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllThreeDiagramsAndWait();
        await WaitForNoteElements();

        var hoverRect = Page.Locator("#puml-2 .note-hover-rect").First;
        await hoverRect.WaitForAsync(new() { Timeout = 10000 });
        await hoverRect.ScrollIntoViewIfNeededAsync();

        await hoverRect.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}))");

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.getElementById('puml-2');
                if (!c) return false;
                var icons = c.querySelectorAll('.note-toggle-icon');
                for (var i = 0; i < icons.length; i++) {
                    if (icons[i].style.opacity !== '0') return true;
                }
                return false;
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    // ── Client-side chunked (puml-fragment inside one container) ──

    private async Task RenderLargeNoteExpandedAndWaitForFragments(string fileName)
    {
        await Page.GotoAsync(GenerateLargeNoteReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // Click "Expanded" in the report toolbar to expand all notes (triggers chunkLargeNotes)
        await Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']").ClickAsync();

        // Wait for fragments to appear (the large note exceeds _maxNoteChars when expanded)
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container) return false;
                if (container._noteRendering || window._plantumlRendering) return false;
                var frags = container.querySelectorAll('.puml-fragment');
                if (frags.length < 2) return false;
                for (var i = 0; i < frags.length; i++) {
                    if (!frags[i].querySelector('svg')) return false;
                }
                return true;
            }
        """, null, new() { Timeout = 120000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Expanded_chunked_continuation_note_has_hover_rects()
    {
        await RenderLargeNoteExpandedAndWaitForFragments("ChunkedContNoteHoverRects.html");

        var contResult = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                var found = [];
                for (var i = 0; i < frags.length; i++) {
                    var src = frags[i].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') >= 0) {
                        var hoverRects = frags[i].querySelectorAll('.note-hover-rect').length;
                        var toggleIcons = frags[i].querySelectorAll('.note-toggle-icon').length;
                        found.push('frag' + i + ':hovers=' + hoverRects + ',icons=' + toggleIcons);
                    }
                }
                return found.length > 0 ? found.join(';') : 'NO_CONTINUATION_FRAGMENT';
            })()
        """);

        Assert.DoesNotContain("NO_CONTINUATION_FRAGMENT", contResult);
        Assert.DoesNotContain("hovers=0", contResult);
    }

    [Fact]
    public async Task Expanded_chunked_continuation_note_hover_shows_buttons()
    {
        await RenderLargeNoteExpandedAndWaitForFragments("ChunkedContNoteHoverButtons.html");

        var hoverRect = await Page.EvaluateHandleAsync("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                for (var i = 0; i < frags.length; i++) {
                    var src = frags[i].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') >= 0) {
                        var hr = frags[i].querySelector('.note-hover-rect');
                        if (hr) return hr;
                    }
                }
                return null;
            })()
        """);

        Assert.NotNull(hoverRect);

        await Page.EvaluateAsync("""
            (hr) => {
                hr.scrollIntoView();
                hr.dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));
            }
        """, hoverRect);

        await Page.WaitForFunctionAsync("""
            () => {
                var frags = document.querySelectorAll('.puml-fragment');
                for (var i = 0; i < frags.length; i++) {
                    var src = frags[i].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') >= 0) {
                        var icons = frags[i].querySelectorAll('.note-toggle-icon');
                        for (var j = 0; j < icons.length; j++) {
                            if (icons[j].style.opacity !== '0') return true;
                        }
                    }
                }
                return false;
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Expanded_chunked_continuation_note_has_copy_box_text_in_context_menu()
    {
        await RenderLargeNoteExpandedAndWaitForFragments("ChunkedContNoteCopyBox.html");

        var menuResult = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                for (var i = 0; i < frags.length; i++) {
                    var src = frags[i].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') >= 0) {
                        var fragSvg = frags[i].querySelector('svg');
                        if (!fragSvg) continue;
                        var noteGroups = window._findNoteGroups(fragSvg);
                        var targetEl = null;
                        if (noteGroups.length > 0) {
                            var lastGrp = noteGroups[noteGroups.length - 1];
                            if (lastGrp.texts.length > 0) targetEl = lastGrp.texts[0];
                            else if (lastGrp.paths.length > 0) targetEl = lastGrp.paths[0];
                        }
                        if (!targetEl) targetEl = frags[i].querySelector('.note-hover-rect');
                        if (!targetEl) continue;

                        if (targetEl.scrollIntoView) targetEl.scrollIntoView();
                        var rect = targetEl.getBoundingClientRect();
                        var evt = new MouseEvent('contextmenu', {
                            bubbles: true, cancelable: true,
                            clientX: rect.left + rect.width / 2,
                            clientY: rect.top + rect.height / 2,
                            pageX: rect.left + rect.width / 2 + window.scrollX,
                            pageY: rect.top + rect.height / 2 + window.scrollY
                        });
                        targetEl.dispatchEvent(evt);

                        var menu = document.querySelector('.diagram-ctx-menu');
                        if (!menu) return 'NO_MENU';
                        var items = Array.from(menu.children)
                            .map(function(it) { return it.textContent.trim(); })
                            .filter(function(it) { return it; });
                        return 'ITEMS:' + items.join('|');
                    }
                }
                return 'NO_CONTINUATION_FRAGMENT';
            })()
        """);

        Assert.DoesNotContain("NO_CONTINUATION_FRAGMENT", menuResult);
        Assert.DoesNotContain("NO_MENU", menuResult);
        Assert.True(menuResult.Contains("Copy box text"),
            $"Expected 'Copy box text' in context menu for continuation note. Got: {menuResult}");
    }
}
