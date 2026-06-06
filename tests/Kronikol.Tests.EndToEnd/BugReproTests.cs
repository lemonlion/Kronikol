namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class BugReproTests : DiagramNotePlaywrightBase
{
    public BugReproTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Chunked_continuation_fragment_all_notes_have_hover_rects()
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

        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var failures = [];
                var frags = document.querySelectorAll('.puml-fragment');
                for (var i = 0; i < frags.length; i++) {
                    var src = frags[i].getAttribute('data-plantuml') || '';
                    var blocks = window._parseNoteBlocks(src).length;
                    var svg = frags[i].querySelector('svg');
                    var groups = svg ? window._findNoteGroups(svg).length : 0;
                    var hovers = frags[i].querySelectorAll('.note-hover-rect').length;
                    if (blocks > 0 && hovers < blocks) {
                        var bs = window._parseNoteBlocks(src);
                        var blockInfo = bs.map(function(b, bi) {
                            return 'block[' + bi + ']: lines=' + b.contentLines.length
                                + ' first="' + b.contentLines[0].substring(0, 60) + '"';
                        });
                        var gs = svg ? window._findNoteGroups(svg) : [];
                        var groupInfo = gs.map(function(g, gi) {
                            var txt = g.texts.slice(0, 2).map(function(t) { return t.textContent; }).join(' ').substring(0, 60);
                            var fill = g.paths[0] ? g.paths[0].getAttribute('fill') : 'none';
                            return 'group[' + gi + ']: fill=' + fill + ' paths=' + g.paths.length + ' texts=' + g.texts.length + ' "' + txt + '"';
                        });
                        // Also count raw candidates before fold filtering
                        var mainG = svg.querySelector('g');
                        var children = mainG ? Array.from(mainG.children) : [];
                        var rawCandidates = [];
                        var ci2 = 0;
                        while (ci2 < children.length) {
                            if (children[ci2].tagName === 'g') { ci2++; continue; }
                            if (children[ci2].tagName === 'path') {
                                var fill2 = (children[ci2].getAttribute('fill') || '').toLowerCase().trim();
                                var hasFill = fill2 && fill2 !== 'none' && fill2 !== 'transparent'
                                    && fill2 !== '#000000' && fill2 !== '#000' && fill2 !== 'black';
                                if (hasFill) {
                                    var candPaths = [];
                                    while (ci2 < children.length && children[ci2].tagName === 'path') {
                                        candPaths.push(children[ci2]); ci2++;
                                    }
                                    var candTexts = 0;
                                    while (ci2 < children.length && (children[ci2].tagName === 'text' || children[ci2].tagName === 'line' || children[ci2].tagName === 'rect' || children[ci2].tagName === 'circle')) {
                                        if (children[ci2].tagName === 'text') candTexts++;
                                        ci2++;
                                    }
                                    if (candPaths.length > 0 && candTexts > 0) {
                                        rawCandidates.push('cand:fill=' + fill2 + ',paths=' + candPaths.length + ',texts=' + candTexts);
                                    }
                                } else { ci2++; }
                            } else { ci2++; }
                        }

                        failures.push('frag[' + i + ']: blocks=' + blocks + ' groups=' + groups + ' hovers=' + hovers
                            + ' rawCandidates=' + rawCandidates.length
                            + '\n  ' + blockInfo.join('\n  ')
                            + '\n  ' + groupInfo.join('\n  ')
                            + '\n  ' + rawCandidates.join('\n  '));
                    }
                }
                return failures.length > 0 ? failures.join('; ') : 'OK';
            })()
        """);

        Assert.True(result == "OK",
            $"Some fragments have fewer hover rects than note blocks: {result}");
    }
}
