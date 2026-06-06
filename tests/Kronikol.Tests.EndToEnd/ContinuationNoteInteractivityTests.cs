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

    // ── Separate container with database/collections participants (real-world split) ──

    private async Task SetupDatabaseContinuationDiagram(string fileName)
    {
        await Page.GotoAsync(GenerateDatabaseContinuationSplitReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllDiagramsAndWait();
        await WaitForNoteElements();
    }

    [Fact]
    public async Task Database_continuation_note_has_hover_rects_and_toggle_icons()
    {
        await SetupDatabaseContinuationDiagram("DbContNoteHoverRects.html");

        // puml-1 has the continuation diagram with database + collections participants
        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var c = document.getElementById('puml-1');
                if (!c) return 'NO_CONTAINER';
                var svg = c.querySelector('svg');
                if (!svg) return 'NO_SVG';
                var src = c._noteOriginalSource || c.getAttribute('data-plantuml') || '';
                var noteBlocks = window._parseNoteBlocks(src).length;
                var noteGroups = window._findNoteGroups(svg).length;
                var hoverRects = c.querySelectorAll('.note-hover-rect').length;
                var toggleIcons = c.querySelectorAll('.note-toggle-icon').length;
                return 'blocks=' + noteBlocks + ',groups=' + noteGroups
                    + ',hovers=' + hoverRects + ',icons=' + toggleIcons;
            })()
        """);

        Assert.DoesNotContain("NO_", result);
        // The continuation diagram has 2 notes — both should have hover rects
        Assert.True(result.Contains("hovers=2") || result.Contains("hovers=3"),
            $"Expected 2+ hover rects in continuation diagram with database participants. Got: {result}");
    }

    [Fact]
    public async Task Database_continuation_note_hover_shows_buttons()
    {
        await SetupDatabaseContinuationDiagram("DbContNoteHoverButtons.html");

        // Find and hover the first note hover rect in puml-1
        var hoverRect = Page.Locator("#puml-1 .note-hover-rect").First;
        await hoverRect.WaitForAsync(new() { Timeout = 10000 });
        await hoverRect.ScrollIntoViewIfNeededAsync();

        await hoverRect.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}))");

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.getElementById('puml-1');
                if (!c) return false;
                var icons = c.querySelectorAll('.note-toggle-icon');
                for (var i = 0; i < icons.length; i++) {
                    if (icons[i].style.opacity !== '0') return true;
                }
                return false;
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Database_continuation_note_has_copy_box_text_in_context_menu()
    {
        await SetupDatabaseContinuationDiagram("DbContNoteCopyBox.html");

        var menuResult = await Page.EvaluateAsync<string>("""
            (() => {
                var c = document.getElementById('puml-1');
                if (!c) return 'NO_CONTAINER';
                var svg = c.querySelector('svg');
                if (!svg) return 'NO_SVG';
                var noteGroups = window._findNoteGroups(svg);
                var targetEl = null;
                for (var i = 0; i < noteGroups.length; i++) {
                    if (noteGroups[i].texts.length > 0) {
                        targetEl = noteGroups[i].texts[0];
                    }
                }
                if (!targetEl) return 'NO_NOTE_TEXT';
                targetEl.scrollIntoView ? targetEl.scrollIntoView() : null;
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
            })()
        """);

        Assert.DoesNotContain("NO_", menuResult);
        Assert.Contains("Copy box text", menuResult);
    }

    // ── Client-side chunked with step delimiters + database/collections (real-world scenario) ──

    [Fact]
    public async Task Chunked_database_note_continuation_fragment_has_hover_rects()
    {
        await Page.GotoAsync(GenerateChunkedDatabaseNoteReport("ChunkedDbContHovers.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Wait for rendering (default is truncated — expand to trigger chunking)
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg');
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Expand to trigger chunkLargeNotes
        await Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']").ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (!c || c._noteRendering || window._plantumlRendering) return false;
                var frags = c.querySelectorAll('.puml-fragment');
                if (frags.length < 2) return false;
                for (var i = 0; i < frags.length; i++) {
                    if (!frags[i].querySelector('svg')) return false;
                }
                return true;
            }
        """, null, new() { Timeout = 120000, PollingInterval = 200 });

        // Check continuation fragments for hover rects
        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                var info = [];
                for (var i = 0; i < frags.length; i++) {
                    var src = frags[i].getAttribute('data-plantuml') || '';
                    var hasCont = src.indexOf('Continued From Previous Diagram') >= 0;
                    var svg = frags[i].querySelector('svg');
                    var noteGroups = svg ? window._findNoteGroups(svg).length : 0;
                    var noteBlocks = window._parseNoteBlocks(src).length;
                    var hoverRects = frags[i].querySelectorAll('.note-hover-rect').length;
                    var toggleIcons = frags[i].querySelectorAll('.note-toggle-icon').length;
                    info.push('frag' + i + ':cont=' + hasCont + ',groups=' + noteGroups
                        + ',blocks=' + noteBlocks + ',hovers=' + hoverRects + ',icons=' + toggleIcons);
                }
                return info.join(' | ');
            })()
        """);

        // Every fragment with note blocks should have hover rects
        Assert.DoesNotContain("blocks=0,hovers=0", result);
        // Specifically check the continuation fragment has hover rects
        Assert.True(
            result.Contains("cont=true") && !result.Contains("cont=true,groups=0"),
            $"Continuation fragment should have note groups. Full: {result}");
        Assert.False(
            result.Contains("cont=true") && result.Split(" | ").Any(f => f.Contains("cont=true") && f.Contains("hovers=0")),
            $"Continuation fragment should have hover rects. Full: {result}");
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
