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
}
