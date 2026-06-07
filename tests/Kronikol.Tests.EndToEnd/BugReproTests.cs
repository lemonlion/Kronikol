namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class BugReproTests : DiagramNotePlaywrightBase
{
    public BugReproTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Chunked_continuation_fragment_all_notes_have_hover_rects()
    {
        await Page.GotoAsync(GenerateChunkedDatabaseNoteReport("ChunkedDbContAllHovers.html"));
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

        // Every continuation fragment must have hover rects for ALL its note blocks
        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var failures = [];
                var frags = document.querySelectorAll('.puml-fragment');
                for (var i = 0; i < frags.length; i++) {
                    var src = frags[i].getAttribute('data-plantuml') || '';
                    var blocks = window._parseNoteBlocks(src);
                    var svg = frags[i].querySelector('svg');
                    var groups = svg ? window._findNoteGroups(svg) : [];
                    var hovers = frags[i].querySelectorAll('.note-hover-rect').length;
                    if (blocks.length > 0 && hovers < blocks.length) {
                        var blockInfo = blocks.map(function(b, bi) {
                            return 'block[' + bi + ']: lines=' + b.contentLines.length
                                + ' "' + b.contentLines[0].substring(0, 50) + '"';
                        });
                        var groupInfo = groups.map(function(g, gi) {
                            return 'group[' + gi + ']: paths=' + g.paths.length + ' texts=' + g.texts.length;
                        });
                        failures.push('frag[' + i + ']: blocks=' + blocks.length
                            + ' groups=' + groups.length + ' hovers=' + hovers
                            + '\n  ' + blockInfo.join('\n  ')
                            + '\n  ' + groupInfo.join('\n  '));
                    }
                }
                return failures.length > 0 ? failures.join('\n') : 'OK';
            })()
        """);

        Assert.True(result == "OK",
            $"Some fragments have fewer hover rects than note blocks:\n{result}");
    }

    [Fact]
    public async Task Chunked_continuation_note_hover_on_path_shows_buttons()
    {
        await Page.GotoAsync(GenerateChunkedDatabaseNoteReport("ChunkedDbContPathHover.html"));
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

        // Hover on the continuation note's path and check buttons appear
        var hoverResult = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                for (var fi = 0; fi < frags.length; fi++) {
                    var src = frags[fi].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') < 0) continue;
                    var svg = frags[fi].querySelector('svg');
                    if (!svg) return 'NO_SVG';
                    var groups = window._findNoteGroups(svg);
                    if (groups.length === 0) return 'NO_GROUPS(blocks=' + window._parseNoteBlocks(src).length + ')';

                    // Dispatch mouseenter on the first group's largest path
                    var bestPath = groups[0].paths[0];
                    var bestArea = 0;
                    for (var pi = 0; pi < groups[0].paths.length; pi++) {
                        try {
                            var bb = groups[0].paths[pi].getBBox();
                            var area = bb.width * bb.height;
                            if (area > bestArea) { bestArea = area; bestPath = groups[0].paths[pi]; }
                        } catch(e) {}
                    }
                    bestPath.scrollIntoView();
                    bestPath.dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));

                    var icons = frags[fi].querySelectorAll('.note-toggle-icon');
                    var visible = 0;
                    for (var i = 0; i < icons.length; i++) {
                        if (icons[i].style.opacity !== '0') visible++;
                    }
                    return 'groups=' + groups.length + ' icons=' + icons.length + ' visible=' + visible;
                }
                return 'NO_CONTINUATION_FRAGMENT';
            })()
        """);

        Assert.False(hoverResult.Contains("visible=0"),
            $"Hover on continuation note path shows no buttons: {hoverResult}");
        Assert.False(hoverResult.StartsWith("NO_"),
            $"Setup issue: {hoverResult}");
    }
}
