namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class BugReproTests : DiagramNotePlaywrightBase
{
    public BugReproTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Chunked_continuation_fragment_visible_notes_have_hover_rects()
    {
        await Page.GotoAsync(GenerateChunkedDatabaseNoteReport("ChunkedDbContFragHover.html"));
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

        // Every continuation fragment must have at least 1 hover rect
        // (all-gray notes with headers hidden correctly have no SVG group)
        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var failures = [];
                var frags = document.querySelectorAll('.puml-fragment');
                for (var i = 0; i < frags.length; i++) {
                    var src = frags[i].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') < 0) continue;
                    var hovers = frags[i].querySelectorAll('.note-hover-rect').length;
                    if (hovers === 0) {
                        failures.push('frag[' + i + ']: continuation fragment has 0 hover rects');
                    }
                }
                return failures.length > 0 ? failures.join('; ') : 'OK';
            })()
        """);

        Assert.True(result == "OK", result);
    }
}
