using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// When the engine answers that a diagram is too large to draw, the page draws it again in parts half as tall, and the
/// parts go to the front of the drawing queue in their order, the top one, usually on the screen, first. Until 5.2.4 a
/// queue insert per part, in a forward loop, put them in reverse (<c>plans/LONG_NOTE_SCROLL_PLAN.md</c> F4). The engine's
/// first answer is stubbed in the page, never on the network; every drawing after it is the engine's own.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class TooLargeRetryOrderTests : PlaywrightTestBase
{
    public TooLargeRetryOrderTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_diagram_drawn_again_in_smaller_parts_draws_them_top_first()
    {
        // The engine's first answer for the diagram is "too large": wrapped into the page's engine shim as the page installs
        // it, before any diagram is drawn.
        await Page.AddInitScriptAsync("""
            (() => {
                var refused = false, current;
                window.__drawnIds = [];
                Object.defineProperty(window, 'plantuml', {
                    configurable: true,
                    get: function() { return current; },
                    set: function(shim) {
                        var real = shim && shim.render;
                        if (real) shim.render = function(lines, id, opts) {
                            window.__drawnIds.push(id);
                            var el = document.getElementById(id);
                            if (!refused && el && el.matches('[data-diagram-type="plantuml"]') && opts && opts.onError) {
                                refused = true;
                                setTimeout(function() { opts.onError('Diagram too large for browser rendering (stubbed)'); }, 0);
                                return;
                            }
                            return real.apply(this, arguments);
                        };
                        current = shim;
                    }
                });
            })();
            """);
        // 30 calls: drawn whole under the 12,000 px estimate, and in parts at half of it
        await Page.GotoAsync(LongPayloadReports.WideQueries(TempDir, OutputDir, "too-large-retry-order.html", calls: 30, NoteWidthMode.Default));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        // drawn again in parts, or given up (a diagram the splitter cannot cut at half the height)
        try
        {
            await Page.WaitForFunctionAsync("""
                () => {
                    var c = document.querySelector('[data-diagram-type="plantuml"]');
                    return !window._plantumlRendering && c && c.dataset.rendered === '1'
                        && (c.querySelectorAll(':scope > .puml-fragment > svg').length >= 2 || /too large/.test(c.textContent));
                }
                """, null, new() { Timeout = 60000, PollingInterval = 200 });
        }
        catch (Exception)
        {
            var state = await Page.EvaluateAsync<string>("""
                () => {
                    var c = document.querySelector('[data-diagram-type="plantuml"]');
                    return JSON.stringify({ drawn: window.__drawnIds, rendering: window._plantumlRendering, rendered: c && c.dataset.rendered,
                        children: c ? Array.from(c.children).map(e => e.tagName + '.' + e.className + '#' + e.id) : null,
                        text: c ? c.textContent.slice(0, 200) : null });
                }
                """);
            Assert.Fail("the diagram was neither drawn in parts nor given up: " + state);
        }
        var split = await Page.EvaluateAsync<int[]>("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var source = c._noteOriginalSource || c.getAttribute('data-plantuml');
                return [window._splitDiagramSource(source, 12000).length, window._splitDiagramSource(source, 6000).length];
            }
            """);
        Assert.True(split[0] == 1 && split[1] > 1, $"premise: whole at 12,000 px and in parts at 6,000 ({split[0]}, {split[1]})");

        var ids = await Page.EvaluateAsync<string[]>("() => window.__drawnIds");
        var parts = ids.Where(id => id.Contains("-frag-")).ToArray();
        Assert.True(parts.Length >= 2, $"premise: the diagram was drawn again in parts ({string.Join(", ", ids)})");
        Assert.Equal(parts.OrderBy(id => int.Parse(id[(id.LastIndexOf('-') + 1)..])).ToArray(), parts);
    }
}
