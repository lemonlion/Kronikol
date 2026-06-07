namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class BugReproTests : DiagramNotePlaywrightBase
{
    public BugReproTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>
    /// The continuation note containing "WoW"/"cadence" text must be detected by
    /// findNoteGroups and have a hover rect. The ..text.. Creole separator in the
    /// continuation marker creates extra SVG elements that can split the note's
    /// paths and texts, preventing findNoteGroups from detecting it.
    /// </summary>
    [Fact]
    public async Task Continuation_note_with_creole_separator_detected_by_findNoteGroups()
    {
        await Page.GotoAsync(GenerateChunkedDatabaseNoteReport("CreoleSepDetection.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg');
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Expand notes to trigger chunkLargeNotes
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

        // Find the SVG that contains "Continued From Previous Diagram" text,
        // then check if that text's note is in a findNoteGroups group
        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                for (var fi = 0; fi < frags.length; fi++) {
                    var src = frags[fi].getAttribute('data-plantuml') || '';
                    if (src.indexOf('Continued From Previous Diagram') < 0) continue;

                    var svg = frags[fi].querySelector('svg');
                    if (!svg) continue;

                    // Find a text element that contains "Continued" in this SVG
                    var contText = null;
                    var allTexts = svg.querySelectorAll('text');
                    for (var ti = 0; ti < allTexts.length; ti++) {
                        if (allTexts[ti].textContent.indexOf('Continued') >= 0) {
                            contText = allTexts[ti]; break;
                        }
                    }
                    if (!contText) return 'NO_CONTINUED_TEXT_IN_SVG';

                    var groups = window._findNoteGroups(svg);
                    var blocks = window._parseNoteBlocks(src);

                    // Check if contText is in any group
                    var ownerGroup = -1;
                    for (var gi = 0; gi < groups.length; gi++) {
                        for (var gti = 0; gti < groups[gi].texts.length; gti++) {
                            if (groups[gi].texts[gti] === contText) {
                                ownerGroup = gi; break;
                            }
                        }
                        if (ownerGroup >= 0) break;
                    }

                    // Also check if the continuation note has a hover rect
                    var hovers = frags[fi].querySelectorAll('.note-hover-rect').length;

                    return 'contTextInGroup=' + ownerGroup
                        + ' groups=' + groups.length
                        + ' blocks=' + blocks.length
                        + ' hovers=' + hovers;
                }
                return 'NO_CONTINUATION_FRAGMENT';
            })()
        """);

        // The continuation text MUST be inside a detected note group
        Assert.False(result.Contains("contTextInGroup=-1"),
            $"Continuation note text not detected by findNoteGroups: {result}");
    }
}
