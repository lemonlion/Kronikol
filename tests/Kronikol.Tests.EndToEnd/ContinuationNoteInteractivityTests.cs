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

    /// <summary>
    /// Regression: the continuation note in a chunked fragment must show an
    /// expand (▼) button when it's a long note. The fragContinuationMap maps
    /// the chunk back to the note it continues, whose owner-source lines may
    /// be truncated from the initial render — continuation chunks are forced
    /// long instead of trusting isLongNote on those lines.
    /// </summary>
    [Fact]
    public async Task Chunked_continuation_note_has_expand_button()
    {
        await Page.GotoAsync(GenerateChunkedDatabaseNoteReport("ChunkedExpandBtn.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg');
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

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

        // Switch to truncated to get ▼ button, but since truncated may not chunk,
        // just check in expanded mode: the continuation note should have a minus
        // button AND an expand arrow (▼) if it's long
        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                for (var fi = 0; fi < frags.length; fi++) {
                    var src = frags[fi].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') < 0) continue;
                    var svg = frags[fi].querySelector('svg');
                    if (!svg) continue;
                    var groups = window._findNoteGroups(svg);
                    if (groups.length === 0) continue;
                    var bbox = window._getNoteBBox(groups[0]);
                    groups[0].paths[0].dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));
                    var icons = frags[fi].querySelectorAll('.note-toggle-icon');
                    var hasMinus = false, hasExpand = false;
                    for (var i = 0; i < icons.length; i++) {
                        if (icons[i].style.opacity === '0') continue;
                        var rect = icons[i].querySelector('rect');
                        if (!rect) continue;
                        var ix = parseFloat(rect.getAttribute('x'));
                        var iy = parseFloat(rect.getAttribute('y'));
                        if (!(ix >= bbox.x-5 && ix <= bbox.x+bbox.width+5
                            && iy >= bbox.y-5 && iy <= bbox.y+bbox.height+5)) continue;
                        if (icons[i].getAttribute('data-note-btn') === 'minus') hasMinus = true;
                        var txt = icons[i].querySelector('text');
                        if (txt && (txt.textContent.indexOf('▼') >= 0 || txt.textContent.indexOf('▲') >= 0)) hasExpand = true;
                    }
                    return 'hasMinus=' + hasMinus + ' hasExpand=' + hasExpand;
                }
                return 'NO_CONTINUATION_FRAG';
            })()
        """);

        Assert.Contains("hasMinus=true", result);
        Assert.Contains("hasExpand=true", result);
    }

    private async Task SetupChunkedFragments(string fileName)
    {
        await Page.GotoAsync(GenerateChunkedDatabaseNoteReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg');
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Expand to trigger chunkLargeNotes (creates fragments)
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
    }

    private async Task WaitForFragmentsIdle()
    {
        await Page.WaitForTimeoutAsync(2000);
        await Page.WaitForFunctionAsync("() => !window._plantumlRendering", null, new() { Timeout = 30000, PollingInterval = 200 });
        await Page.WaitForFunctionAsync("() => { var c = document.querySelectorAll('[data-diagram-type=\"plantuml\"]'); for(var i=0;i<c.length;i++) if(c[i]._noteRendering) return false; return true; }", null, new() { Timeout = 30000, PollingInterval = 200 });
    }

    /// <summary>
    /// Shared in-page helper: finds the note group at <c>groupIdx</c> in the
    /// requested fragment (continuation fragment when <c>wantContinuation</c>),
    /// hovers it and clicks the minus button inside its bbox.
    /// </summary>
    private const string ClickMinusOnFragmentNoteJs = """
        (args) => {
            var wantContinuation = args[0], groupIdx = args[1];
            var frags = document.querySelectorAll('.puml-fragment');
            for (var fi = 0; fi < frags.length; fi++) {
                var src = frags[fi].getAttribute('data-plantuml') || '';
                var isCont = src.indexOf('Continued From Previous Diagram') >= 0;
                if (isCont !== wantContinuation) continue;
                var svg = frags[fi].querySelector('svg');
                if (!svg) continue;
                var groups = window._findNoteGroups(svg);
                if (groups.length <= groupIdx) continue;
                var bbox = window._getNoteBBox(groups[groupIdx]);
                groups[groupIdx].paths[0].dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));
                var icons = frags[fi].querySelectorAll('.note-toggle-icon');
                for (var i = 0; i < icons.length; i++) {
                    if (icons[i].style.opacity === '0') continue;
                    if (icons[i].getAttribute('data-note-btn') !== 'minus') continue;
                    var rect = icons[i].querySelector('rect');
                    if (!rect) continue;
                    var ix = parseFloat(rect.getAttribute('x'));
                    var iy = parseFloat(rect.getAttribute('y'));
                    if (ix >= bbox.x-5 && ix <= bbox.x+bbox.width+5
                        && iy >= bbox.y-5 && iy <= bbox.y+bbox.height+5) {
                        rect.dispatchEvent(new MouseEvent('click', {bubbles:true}));
                        return 'CLICKED frag=' + fi;
                    }
                }
                return 'NO_MINUS_BTN frag=' + fi;
            }
            return 'NO_MATCHING_FRAG';
        }
        """;

    private const string ReadNoteStepsJs = """
        () => {
            var container = document.querySelector('[data-diagram-type="plantuml"]');
            if (!container) return 'NO_CONTAINER';
            var steps = container._noteSteps || {};
            return 'step0=' + (steps[0] === undefined ? 'u' : steps[0])
                + ' step1=' + (steps[1] === undefined ? 'u' : steps[1])
                + ' step2=' + (steps[2] === undefined ? 'u' : steps[2])
                + ' step3=' + (steps[3] === undefined ? 'u' : steps[3]);
        }
        """;

    /// <summary>
    /// Regression: clicking minus on a continuation chunk must collapse the note
    /// it is a chunk OF — the split note, original index 1 in this fixture (note
    /// 0 is the POST request body) — not original note 0. fragContinuationMap
    /// hard-coded the continuation chunk to index 0, so buttons on chunks in
    /// later fragments drove the wrong note; and once note 0 was already
    /// collapsed, the click was silently swallowed by setNoteState's same-step
    /// early return. Phase 1 collapses note 0 first so the buggy path has
    /// nowhere to hide: only the correct mapping can change step1.
    /// </summary>
    [Fact]
    public async Task Chunked_continuation_note_minus_collapses_the_split_note()
    {
        await SetupChunkedFragments("ChunkedExpandCorrect.html");

        // Phase 1: collapse note 0 via its own minus button (first note group
        // of the non-continuation fragment).
        var phase1 = await Page.EvaluateAsync<string>(ClickMinusOnFragmentNoteJs, new object[] { false, 0 });
        Assert.StartsWith("CLICKED", phase1);
        await WaitForFragmentsIdle();
        Assert.Contains("step0=0", await Page.EvaluateAsync<string>(ReadNoteStepsJs));

        // Phase 2: click minus on the continuation chunk (first note group of
        // the continuation fragment). This must collapse the SPLIT note (1).
        var phase2 = await Page.EvaluateAsync<string>(ClickMinusOnFragmentNoteJs, new object[] { true, 0 });
        Assert.StartsWith("CLICKED", phase2);
        await WaitForFragmentsIdle();

        var steps = await Page.EvaluateAsync<string>(ReadNoteStepsJs);
        // The continuation chunk belongs to note 1: step1 must now be 0. With
        // the old map ([0]) the click targeted note 0 (already 0) and was
        // swallowed, leaving step1 at 2.
        Assert.Contains("step1=0", steps);
        Assert.Contains("step0=0", steps);
        // The notes after the split one must be untouched.
        Assert.Contains("step2=2", steps);
        Assert.Contains("step3=2", steps);
    }

    /// <summary>
    /// Regression: the context menu computed its global note index by summing
    /// RAW note-block counts of preceding fragments (no continuation-chunk
    /// remap), so for continuation fragments it read a different _noteSteps
    /// entry than the hover buttons write. Right-clicking the expanded
    /// continuation chunk must offer the single "Copy box text" item, not the
    /// truncated-note submenu keyed off an unrelated note's state.
    /// </summary>
    [Fact]
    public async Task Chunked_continuation_context_menu_agrees_with_note_buttons()
    {
        await SetupChunkedFragments("ChunkedCtxMenuIndex.html");

        var menuResult = await Page.EvaluateAsync<string>("""
            (() => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container) return 'NO_CONTAINER';
                // Poison the index the buggy raw sum lands on (note 2, the
                // response note): with the continuation chunk expanded, the
                // menu must NOT consult this entry.
                container._noteSteps[2] = 0;
                var frags = container.querySelectorAll('.puml-fragment');
                for (var fi = 0; fi < frags.length; fi++) {
                    var src = frags[fi].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') < 0) continue;
                    var hr = frags[fi].querySelector('.note-hover-rect');
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
                    var items = Array.from(menu.querySelectorAll('*'))
                        .map(function(i) { return i.textContent.trim(); })
                        .filter(function(i) { return i; });
                    return 'ITEMS:' + items.join('|');
                }
                return 'NO_CONTINUATION_FRAG';
            })()
        """);

        Assert.StartsWith("ITEMS:", menuResult);
        Assert.Contains("Copy box text", menuResult);
        // The buggy raw-sum index (2) is collapsed, which would render the
        // "Copy full box text" submenu; the continuation chunk's real note (1)
        // is expanded, so only the flat item may appear.
        Assert.DoesNotContain("Copy full box text", menuResult);
    }

    /// <summary>
    /// Regression: the J/Y format toggle on a continuation chunk cached
    /// eligibility and wrote _noteFormats under the wrong index (0), flipping
    /// an unrelated note to YAML. Whatever the eligibility verdict for the
    /// split note itself, note 0 must never gain a format entry from a click
    /// on the continuation chunk.
    /// </summary>
    [Fact]
    public async Task Chunked_continuation_format_button_never_targets_another_note()
    {
        await SetupChunkedFragments("ChunkedFormatIndex.html");

        var clickResult = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                for (var fi = 0; fi < frags.length; fi++) {
                    var src = frags[fi].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') < 0) continue;
                    var svg = frags[fi].querySelector('svg');
                    if (!svg) continue;
                    var groups = window._findNoteGroups(svg);
                    if (groups.length === 0) continue;
                    var bbox = window._getNoteBBox(groups[0]);
                    groups[0].paths[0].dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));
                    var icons = frags[fi].querySelectorAll('.note-toggle-icon');
                    for (var i = 0; i < icons.length; i++) {
                        if (icons[i].getAttribute('data-note-btn') !== 'format') continue;
                        if (icons[i].style.display === 'none') continue;
                        var rect = icons[i].querySelector('rect');
                        if (!rect) continue;
                        var ix = parseFloat(rect.getAttribute('x'));
                        var iy = parseFloat(rect.getAttribute('y'));
                        if (ix >= bbox.x-5 && ix <= bbox.x+bbox.width+5
                            && iy >= bbox.y-5 && iy <= bbox.y+bbox.height+5) {
                            rect.dispatchEvent(new MouseEvent('click', {bubbles:true}));
                            return 'CLICKED';
                        }
                    }
                    return 'NO_VISIBLE_FORMAT_BTN';
                }
                return 'NO_CONTINUATION_FRAG';
            })()
        """);

        // Both outcomes are legitimate: the split note may or may not be
        // YAML-eligible. What must never happen is the button showing (or
        // acting) based on note 0's payload.
        Assert.True(clickResult is "CLICKED" or "NO_VISIBLE_FORMAT_BTN",
            $"Unexpected result: {clickResult}");
        await WaitForFragmentsIdle();

        var formats = await Page.EvaluateAsync<string>("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container) return 'NO_CONTAINER';
                var f = container._noteFormats || {};
                return 'f0=' + (f[0] || 'unset') + ' f1=' + (f[1] || 'unset');
            }
        """);
        Assert.Contains("f0=unset", formats);
    }

    /// <summary>
    /// Regression test: findNoteGroups must not merge consecutive paths with
    /// different fill colors into one group. When a transparent path (#00000000)
    /// sits between two notes with different fills (#e2e2f0 and #feffdd), they
    /// must be detected as separate groups with correct bounding boxes.
    /// </summary>
    [Fact]
    public async Task Chunked_continuation_note_not_merged_with_adjacent_paths()
    {
        await Page.GotoAsync(GenerateChunkedDatabaseNoteReport("ChunkedNoMerge.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg');
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

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

        // No note group should have width > 800px (a merged group spanning
        // multiple notes would be much wider than any single note)
        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                var failures = [];
                for (var fi = 0; fi < frags.length; fi++) {
                    var svg = frags[fi].querySelector('svg');
                    if (!svg) continue;
                    var groups = window._findNoteGroups(svg);
                    for (var gi = 0; gi < groups.length; gi++) {
                        var bb = window._getNoteBBox(groups[gi]);
                        if (bb.width > 800 && bb.height > 800) {
                            failures.push('frag[' + fi + '] g[' + gi + ']: merged group ' + bb.width.toFixed(0) + 'x' + bb.height.toFixed(0));
                        }
                    }
                }
                return failures.length > 0 ? failures.join('; ') : 'OK';
            })()
        """);

        Assert.True(result == "OK", $"Note groups merged across different notes: {result}");
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
