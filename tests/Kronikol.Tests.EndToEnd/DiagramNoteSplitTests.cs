namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class DiagramNoteSplitTests : DiagramNotePlaywrightBase
{
    public DiagramNoteSplitTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task ExpandAndRenderSplitDiagram(string fileName)
    {
        await Page.GotoReportAsync(GenerateSplitDiagramReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllDiagramsAndWait();
        await WaitForNoteElements();
    }

    [Fact]
    public async Task Split_diagram_all_parts_have_hover_rects_on_initial_render()
    {
        await ExpandAndRenderSplitDiagram("SplitHoverRectsInit.html");

        var count = await Page.Locator(".note-hover-rect").CountAsync();
        Assert.True(count >= 2, $"Expected hover rects in all diagram parts, found {count}");
    }

    [Fact]
    public async Task Split_diagram_all_parts_have_toggle_icons_on_initial_render()
    {
        await ExpandAndRenderSplitDiagram("SplitToggleIconsInit.html");

        var count = await Page.Locator(".note-toggle-icon").CountAsync();
        Assert.True(count >= 2, $"Expected toggle icons in all diagram parts, found {count}");
    }

    [Fact]
    public async Task Split_diagram_second_part_hover_shows_buttons_on_initial_render()
    {
        await ExpandAndRenderSplitDiagram("SplitSecondPartHover.html");

        var rects = Page.Locator(".note-hover-rect");
        var count = await rects.CountAsync();
        Assert.True(count >= 2);

        // JS dispatch avoids SVG <text> elements intercepting pointer events
        await rects.Nth(count - 1).EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}))");

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => i.style.opacity !== '0')
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Split_diagram_first_part_hover_shows_buttons_on_initial_render()
    {
        await ExpandAndRenderSplitDiagram("SplitFirstPartHover.html");

        await HoverNoteRect(0);

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => i.style.opacity !== '0')
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Split_diagram_dblclick_on_second_diagram_note_cycles_state()
    {
        await ExpandAndRenderSplitDiagram("SplitDblClickSecond.html");

        var rects = Page.Locator(".note-hover-rect");
        var count = await rects.CountAsync();
        var htmlBefore = await Page.Locator("[data-diagram-type='plantuml'] svg").Last.EvaluateAsync<string>("el => el.outerHTML");

        // JS dispatch avoids SVG <text> elements intercepting pointer events
        await rects.Nth(count - 1).EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");

        await Page.WaitForFunctionAsync(
            $"() => {{ var svgs = document.querySelectorAll('[data-diagram-type=\"plantuml\"] svg'); " +
            $"return svgs[svgs.length-1].outerHTML !== {System.Text.Json.JsonSerializer.Serialize(htmlBefore)}; }}",
            null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Split_diagram_scenario_state_change_preserves_second_diagram_buttons()
    {
        await ExpandAndRenderSplitDiagram("SplitStateChangePreserves.html");

        await SetScenarioState("collapsed");

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        // Check second diagram also has buttons
        var svgs = Page.Locator("[data-diagram-type='plantuml'] svg");
        var svgCount = await svgs.CountAsync();
        Assert.True(svgCount >= 2);
    }

    [Fact]
    public async Task After_report_truncation_change_all_diagrams_have_hover_buttons()
    {
        await ExpandAndRenderSplitDiagram("SplitReportTruncChange.html");

        await Page.Locator(".toolbar-row .details-radio-btn[data-state='collapsed']").ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task After_scenario_truncation_change_all_diagrams_have_hover_buttons()
    {
        await ExpandAndRenderSplitDiagram("SplitScenarioTruncChange.html");

        await SetScenarioState("expanded");

        Assert.True(await Page.Locator(".note-hover-rect").CountAsync() >= 2);
    }

    [Fact]
    public async Task Split_diagram_all_parts_have_hover_buttons_after_truncation_change()
    {
        await ExpandAndRenderSplitDiagram("SplitAllPartsButtons.html");

        await SetScenarioState("collapsed");
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        await SetScenarioState("expanded");
        Assert.True(await Page.Locator(".note-hover-rect").CountAsync() >= 2);
    }

    [Fact]
    public async Task Split_diagram_hover_buttons_visible_on_second_diagram_after_truncation()
    {
        await ExpandAndRenderSplitDiagram("SplitSecondDiagramButtons.html");

        await SetScenarioState("collapsed");
        await SetScenarioState("truncated");

        var rects = Page.Locator(".note-hover-rect");
        var count = await rects.CountAsync();
        // JS dispatch avoids SVG <text> elements intercepting pointer events
        await rects.Nth(count - 1).EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}))");

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => i.style.opacity !== '0')
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Three_diagram_split_continuation_note_has_hover_rects()
    {
        await Page.GotoReportAsync(GenerateThreeDiagramSplitReport("ThreeSplitHoverRects.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllThreeDiagramsAndWait();
        await WaitForNoteElements();

        Assert.True(await Page.Locator(".note-hover-rect").CountAsync() >= 3);
    }

    [Fact]
    public async Task Three_diagram_split_continuation_note_has_toggle_icons()
    {
        await Page.GotoReportAsync(GenerateThreeDiagramSplitReport("ThreeSplitToggleIcons.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllThreeDiagramsAndWait();
        await WaitForNoteElements();

        Assert.True(await Page.Locator(".note-toggle-icon").CountAsync() >= 3);
    }

    [Fact]
    public async Task Three_diagram_split_findNoteGroups_matches_noteBlocks_on_all_diagrams()
    {
        await Page.GotoReportAsync(GenerateThreeDiagramSplitReport("ThreeSplitGroupBlockMatch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllThreeDiagramsAndWait();
        await WaitForNoteElements();

        var hoverCount = await Page.Locator(".note-hover-rect").CountAsync();
        var iconCount = await Page.Locator(".note-toggle-icon").CountAsync();
        Assert.True(hoverCount > 0, $"Expected hover rects, found {hoverCount}");
        Assert.True(iconCount > 0, $"Expected toggle icons, found {iconCount}");
        Assert.True(iconCount >= hoverCount,
            $"Expected at least as many toggle icons ({iconCount}) as hover rects ({hoverCount})");
    }

    [Fact]
    public async Task Three_diagram_split_all_diagrams_with_notes_have_hover_rects()
    {
        await Page.GotoReportAsync(GenerateThreeDiagramSplitReport("ThreeSplitAllHoverRects.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllThreeDiagramsAndWait();
        await WaitForNoteElements();

        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var containers = document.querySelectorAll('[data-diagram-type="plantuml"]');
                var results = [];
                containers.forEach(function(c) {
                    var src = c._noteOriginalSource || c.getAttribute('data-plantuml');
                    var noteBlocks = window._parseNoteBlocks ? window._parseNoteBlocks(src).length : 0;
                    var hoverRects = c.querySelectorAll('.note-hover-rect').length;
                    results.push({ id: c.id, noteBlocks: noteBlocks, hoverRects: hoverRects });
                });
                return JSON.stringify(results);
            })()
        """);

        var diagrams = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(result)!;
        foreach (var diag in diagrams)
        {
            var id = diag.GetProperty("id").GetString()!;
            var blocks = diag.GetProperty("noteBlocks").GetInt32();
            var rects = diag.GetProperty("hoverRects").GetInt32();
            if (blocks > 0)
            {
                Assert.True(rects > 0,
                    $"{id}: has {blocks} noteBlocks but {rects} hoverRects. Full: {result}");
            }
        }
    }

    [Fact]
    public async Task Three_diagram_split_dblclick_on_continuation_note_cycles_state()
    {
        await Page.GotoReportAsync(GenerateThreeDiagramSplitReport("ThreeSplitDblClickCont.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllThreeDiagramsAndWait();
        await WaitForNoteElements();

        var svgBefore = await Page.EvaluateAsync<string>("""
            (() => {
                var c = document.getElementById('puml-2');
                var svg = c ? c.querySelector('svg') : null;
                return svg ? svg.outerHTML : '';
            })()
        """);
        Assert.False(string.IsNullOrEmpty(svgBefore));

        await Page.EvaluateAsync("""
            (() => {
                var c = document.getElementById('puml-2');
                var hr = c ? c.querySelector('.note-hover-rect') : null;
                if (hr) hr.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}));
            })()
        """);

        await Page.WaitForFunctionAsync("""
            (prev) => {
                var c = document.getElementById('puml-2');
                var svg = c ? c.querySelector('svg') : null;
                return svg && svg.outerHTML !== prev;
            }
        """, svgBefore, new() { Timeout = 15000 });

        var hasButtons = await Page.EvaluateAsync<bool>("""
            (() => {
                var c = document.getElementById('puml-2');
                return c && c.querySelectorAll('.note-hover-rect').length > 0
                    && c.querySelectorAll('.note-toggle-icon').length > 0;
            })()
        """);
        Assert.True(hasButtons, "puml-2 should have hover rects and toggle icons after dblclick state cycle");
    }

    [Fact]
    public async Task Three_diagram_split_hover_on_continuation_note_shows_buttons()
    {
        await Page.GotoReportAsync(GenerateThreeDiagramSplitReport("ThreeSplitHoverContNote.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await RenderAllThreeDiagramsAndWait();
        await WaitForNoteElements();

        var hoverRect = Page.Locator("#puml-2 .note-hover-rect").First;
        await hoverRect.ScrollIntoViewIfNeededAsync();
        // JS dispatch avoids SVG <text> elements intercepting pointer events
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

    [Fact]
    public async Task Split_diagram_note_click_does_not_cause_layout_shift()
    {
        await Page.GotoReportAsync(GenerateFragmentedDiagramReport("SplitNoLayoutShift.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Wait for fragments to render (height-based splitting triggers client-side fragmentation)
        await Page.WaitForFunctionAsync("""
            () => {
                var frags = document.querySelectorAll('.puml-fragment svg');
                if (frags.length < 2) return false;
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                return container && !container._noteRendering && !window._plantumlRendering
                    && document.querySelectorAll('.note-hover-rect').length > 0;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Install height monitor using MutationObserver — fires synchronously on DOM mutation
        await Page.EvaluateAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                window._heightBefore = container.offsetHeight;
                window._minObservedHeight = container.offsetHeight;
                var observer = new MutationObserver(() => {
                    var h = container.offsetHeight;
                    if (h < window._minObservedHeight) window._minObservedHeight = h;
                });
                observer.observe(container, { childList: true, subtree: true });
                window._heightObserver = observer;
            }
        """);

        // Double-click a note on the second fragment to trigger re-render of all fragments
        await Page.Locator(".note-hover-rect").Last.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");

        // Wait for re-render to complete
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                return container && !container._noteRendering && !window._plantumlRendering;
            }
        """, null, new() { Timeout = 30000, PollingInterval = 200 });

        // Stop observer and check results
        await Page.EvaluateAsync("() => window._heightObserver.disconnect()");

        var minHeight = await Page.EvaluateAsync<double>("() => window._minObservedHeight");
        var heightBefore = await Page.EvaluateAsync<double>("() => window._heightBefore");

        // The container height should never drop significantly during re-render
        Assert.True(minHeight >= heightBefore * 0.5,
            $"Layout shift detected: container height dropped to {minHeight}px during render (was {heightBefore}px before click)");
    }

    [Fact]
    public async Task Split_diagram_note_click_keeps_svg_visible_during_rerender()
    {
        await Page.GotoReportAsync(GenerateFragmentedDiagramReport("SplitSvgVisibleDuringRerender.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Wait for fragments to render
        await Page.WaitForFunctionAsync("""
            () => {
                var frags = document.querySelectorAll('.puml-fragment svg');
                if (frags.length < 2) return false;
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                return container && !container._noteRendering && !window._plantumlRendering
                    && document.querySelectorAll('.note-hover-rect').length > 0;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Install SVG visibility monitor — checks that visible SVGs never drop to zero
        await Page.EvaluateAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                window._svgEverDisappeared = false;
                window._svgCheckCount = 0;
                var observer = new MutationObserver(() => {
                    window._svgCheckCount++;
                    // Count visible SVGs (not inside display:none elements)
                    var visibleSvgs = Array.from(container.querySelectorAll('svg')).filter(svg => {
                        var el = svg.closest('.puml-fragment-new, [style*="display: none"], [style*="display:none"]');
                        return !el;
                    });
                    if (visibleSvgs.length === 0) {
                        window._svgEverDisappeared = true;
                    }
                });
                observer.observe(container, { childList: true, subtree: true });
                window._svgVisibilityObserver = observer;
            }
        """);

        // Double-click a note to trigger re-render
        await Page.Locator(".note-hover-rect").Last.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");

        // Wait for re-render to complete
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                return container && !container._noteRendering && !window._plantumlRendering;
            }
        """, null, new() { Timeout = 30000, PollingInterval = 200 });

        // Stop observer and check results
        await Page.EvaluateAsync("() => window._svgVisibilityObserver.disconnect()");

        var disappeared = await Page.EvaluateAsync<bool>("() => window._svgEverDisappeared");
        var checkCount = await Page.EvaluateAsync<int>("() => window._svgCheckCount");

        Assert.True(checkCount > 0, "MutationObserver should have fired at least once during re-render");
        Assert.False(disappeared,
            "SVG content disappeared during re-render — old diagram should remain visible until new one is ready");
    }

    [Fact]
    public async Task Split_diagram_hover_buttons_present_after_note_toggle_rerender()
    {
        await Page.GotoReportAsync(GenerateFragmentedDiagramReport("SplitHoverAfterRerender.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Wait for fragments to render with hover rects
        await Page.WaitForFunctionAsync("""
            () => {
                var frags = document.querySelectorAll('.puml-fragment svg');
                if (frags.length < 2) return false;
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                return container && !container._noteRendering && !window._plantumlRendering
                    && document.querySelectorAll('.note-hover-rect').length > 0;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Double-click a note to trigger re-render (cycles note state)
        await Page.Locator(".note-hover-rect").First.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");

        // Wait for re-render to complete
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                return container && !container._noteRendering && !window._plantumlRendering;
            }
        """, null, new() { Timeout = 30000, PollingInterval = 200 });

        // Verify hover rects exist in the new fragments (getBBox must have worked on visible elements)
        var hoverRectCount = await Page.Locator(".note-hover-rect").CountAsync();
        Assert.True(hoverRectCount >= 2,
            $"Expected hover rects in re-rendered fragments, found {hoverRectCount}. " +
            "This indicates makeNotesCollapsible ran while fragments were display:none");

        // Verify hover rects have non-zero dimensions (proves getBBox worked)
        var allNonZero = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.note-hover-rect')).every(r => {
                var w = parseFloat(r.getAttribute('width') || '0');
                var h = parseFloat(r.getAttribute('height') || '0');
                return w > 0 && h > 0;
            })
        """);
        Assert.True(allNonZero,
            "Some hover rects have zero dimensions — makeNotesCollapsible likely ran on hidden elements");
    }
}