namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Regression tests for the noteIndexOffset overcounting bug: when a note spans
/// 3+ client-side fragments, continuation notes in earlier fragments inflated the
/// offset for later fragments, causing out-of-bounds access in makeNotesCollapsible
/// and halting the entire render queue.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class ThreeFragmentContinuationTests : DiagramNotePlaywrightBase
{
    public ThreeFragmentContinuationTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task ExpandToThreeFragments()
    {
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
                if (frags.length < 3) return false;
                for (var i = 0; i < frags.length; i++) {
                    if (!frags[i].querySelector('svg')) return false;
                }
                return true;
            }
        """, null, new() { Timeout = 120000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Three_fragment_continuation_renders_without_js_errors()
    {
        var jsErrors = new List<string>();
        Page.PageError += (_, msg) => jsErrors.Add(msg);

        await Page.GotoAsync(GenerateThreeFragmentContinuationReport("ThreeFragNoError.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ExpandToThreeFragments();

        Assert.Empty(jsErrors);
    }

    [Fact]
    public async Task Three_fragment_continuation_all_fragments_have_hover_rects()
    {
        await Page.GotoAsync(GenerateThreeFragmentContinuationReport("ThreeFragHoverRects.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ExpandToThreeFragments();

        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                var info = [];
                for (var i = 0; i < frags.length; i++) {
                    var src = frags[i].getAttribute('data-plantuml') || '';
                    var hasCont = src.indexOf('Continued From Previous Diagram') >= 0;
                    var noteBlocks = window._parseNoteBlocks(src).length;
                    var hoverRects = frags[i].querySelectorAll('.note-hover-rect').length;
                    info.push('frag' + i + ':cont=' + hasCont
                        + ',blocks=' + noteBlocks + ',hovers=' + hoverRects);
                }
                return info.join(' | ');
            })()
        """);

        // Every fragment with note blocks must have hover rects
        var fragments = result.Split(" | ");
        foreach (var frag in fragments)
        {
            if (frag.Contains("blocks=0")) continue;
            Assert.False(frag.Contains("hovers=0"),
                $"Fragment with notes has no hover rects. Full: {result}");
        }
    }

    [Fact]
    public async Task Three_fragment_continuation_render_queue_completes()
    {
        await Page.GotoAsync(GenerateThreeFragmentContinuationReport("ThreeFragQueue.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ExpandToThreeFragments();

        // Verify the render queue is not stuck — plantumlRendering should be false
        var queueOk = await Page.EvaluateAsync<bool>(
            "() => !window._plantumlRendering");
        Assert.True(queueOk, "Render queue is stuck (plantumlRendering still true)");
    }

    [Fact]
    public async Task Three_fragment_continuation_note_steps_map_to_correct_global_indices()
    {
        await Page.GotoAsync(GenerateThreeFragmentContinuationReport("ThreeFragSteps.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ExpandToThreeFragments();

        // Check that noteSteps keys don't exceed ownerNoteBlocks count
        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (!c) return 'NO_CONTAINER';
                var src = c._noteOriginalSource || c.getAttribute('data-plantuml') || '';
                var ownerNoteCount = window._parseNoteBlocks(src).length;
                var steps = c._noteSteps || {};
                var keys = Object.keys(steps).map(Number);
                var maxKey = keys.length > 0 ? Math.max.apply(null, keys) : -1;
                var outOfBounds = keys.filter(function(k) { return k >= ownerNoteCount; });
                return 'ownerNotes=' + ownerNoteCount
                    + ' maxStepKey=' + maxKey
                    + ' outOfBounds=' + JSON.stringify(outOfBounds);
            })()
        """);

        Assert.DoesNotContain("NO_CONTAINER", result);
        Assert.Contains("outOfBounds=[]", result);
    }

    /// <summary>
    /// Regression: in a 3-fragment split of note 1 (of 4), the middle fragment
    /// holds nothing but a continuation chunk of that note. Its minus button
    /// must collapse note 1 — with the old hard-coded fragContinuationMap of
    /// [0] it collapsed note 0 (the request body in fragment 0) instead.
    /// </summary>
    [Fact]
    public async Task Middle_fragment_continuation_minus_collapses_the_split_note()
    {
        await Page.GotoAsync(GenerateThreeFragmentContinuationReport("ThreeFragMiddleMinus.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ExpandToThreeFragments();

        // Click minus on the note in a MIDDLE fragment: one whose source is
        // both continued-from and continued-on (only the split note's interior
        // chunk matches).
        var clickResult = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                for (var fi = 0; fi < frags.length; fi++) {
                    var src = frags[fi].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') < 0) continue;
                    if (src.indexOf('Continued On Next Diagram') < 0) continue;
                    var svg = frags[fi].querySelector('svg');
                    if (!svg) continue;
                    var groups = window._findNoteGroups(svg);
                    if (groups.length === 0) continue;
                    var bbox = window._getNoteBBox(groups[0]);
                    groups[0].paths[0].dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));
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
                return 'NO_MIDDLE_FRAG';
            })()
        """);
        Assert.StartsWith("CLICKED", clickResult);

        await Page.WaitForTimeoutAsync(2000);
        await Page.WaitForFunctionAsync("() => !window._plantumlRendering", null, new() { Timeout = 30000, PollingInterval = 200 });
        await Page.WaitForFunctionAsync("() => { var c = document.querySelectorAll('[data-diagram-type=\"plantuml\"]'); for(var i=0;i<c.length;i++) if(c[i]._noteRendering) return false; return true; }", null, new() { Timeout = 30000, PollingInterval = 200 });

        var steps = await Page.EvaluateAsync<string>("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container) return 'NO_CONTAINER';
                var steps = container._noteSteps || {};
                return 'step0=' + (steps[0] === undefined ? 'u' : steps[0])
                    + ' step1=' + (steps[1] === undefined ? 'u' : steps[1])
                    + ' step2=' + (steps[2] === undefined ? 'u' : steps[2])
                    + ' step3=' + (steps[3] === undefined ? 'u' : steps[3]);
            }
        """);

        // The middle chunk continues note 1: only step1 may change.
        Assert.Contains("step1=0", steps);
        Assert.Contains("step0=2", steps);
        Assert.Contains("step2=2", steps);
        Assert.Contains("step3=2", steps);
    }
}
