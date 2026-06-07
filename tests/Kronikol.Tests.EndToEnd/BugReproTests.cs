namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class BugReproTests : DiagramNotePlaywrightBase
{
    public BugReproTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task SetupReport(string destFileName)
    {
        var srcPath = @"C:\Users\cex\Downloads\bug (2)\TestRunReport.html";
        var destPath = Path.Combine(TempDir, destFileName);
        File.Copy(srcPath, destPath, true);

        await Page.GotoAsync(new Uri(destPath).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.Locator("details.feature summary").First.ClickAsync();
        await Page.WaitForTimeoutAsync(300);
        await Page.Locator("details.scenario summary").First.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        await Page.EvaluateAsync("""
            () => {
                var scenario = document.querySelector('details.scenario[open]');
                if (scenario && window._renderDiagramsInContainer)
                    window._renderDiagramsInContainer(scenario);
            }
        """);

        await Page.WaitForFunctionAsync("""
            () => {
                var scenario = document.querySelector('details.scenario[open]');
                if (!scenario) return false;
                var containers = scenario.querySelectorAll('[data-diagram-type="plantuml"]');
                if (containers.length === 0) return false;
                for (var i = 0; i < containers.length; i++) {
                    if (!containers[i].querySelector('svg')) return false;
                    if (containers[i]._noteRendering || window._plantumlRendering) return false;
                }
                return true;
            }
        """, null, new() { Timeout = 120000, PollingInterval = 200 });
    }

    /// <summary>
    /// Checks: is the "Continued From Previous Diagram" text inside a findNoteGroups group?
    /// </summary>
    private async Task<string> CheckContinuedFromText()
    {
        return await Page.EvaluateAsync<string>("""
            (() => {
                var scenario = document.querySelector('details.scenario[open]');
                var svgs = scenario.querySelectorAll('[data-diagram-type="plantuml"] svg');
                for (var si = 0; si < svgs.length; si++) {
                    var allTexts = svgs[si].querySelectorAll('text');
                    var contEl = null;
                    for (var ti = 0; ti < allTexts.length; ti++) {
                        if (allTexts[ti].textContent.indexOf('Continued From') >= 0) {
                            contEl = allTexts[ti]; break;
                        }
                    }
                    if (!contEl) continue;

                    var groups = window._findNoteGroups(svgs[si]);
                    var ownerGroup = -1;
                    for (var gi = 0; gi < groups.length; gi++) {
                        if (groups[gi].texts.indexOf(contEl) >= 0) { ownerGroup = gi; break; }
                    }
                    return 'contTextInGroup=' + ownerGroup + ' groups=' + groups.length;
                }
                return 'NO_CONTINUED_FROM_TEXT';
            })()
        """);
    }

    /// <summary>
    /// Step 1: v3.0.31 HTML has the bug — "Continued From" text not in any note group.
    /// </summary>
    [Fact]
    public async Task Original_html_continuation_text_not_in_group()
    {
        await SetupReport("Original.html");
        var result = await CheckContinuedFromText();
        Assert.Contains("contTextInGroup=-1", result);
    }

    /// <summary>
    /// Step 2: After monkey-patching findNoteGroups with the orphaned-text sweep,
    /// the "Continued From" text IS in a note group.
    /// </summary>
    [Fact]
    public async Task Patched_html_continuation_text_in_group()
    {
        await SetupReport("Patched.html");

        // Monkey-patch: add orphaned text sweep to findNoteGroups
        await Page.EvaluateAsync("""
            () => {
                var origFind = window._findNoteGroups;
                window._findNoteGroups = function(svg) {
                    var groups = origFind(svg);
                    // Sweep for orphaned texts inside note bboxes
                    var mainG = svg.querySelector('g');
                    if (!mainG) return groups;
                    var children = Array.from(mainG.children);
                    groups.forEach(function(grp) {
                        var nb = null;
                        for (var i = 0; i < grp.paths.length; i++) {
                            try {
                                var pbb = grp.paths[i].getBBox();
                                if (pbb.width <= 0 && pbb.height <= 0) continue;
                                if (!nb) { nb = { x: pbb.x, y: pbb.y, r: pbb.x + pbb.width, b: pbb.y + pbb.height }; }
                                else {
                                    if (pbb.x < nb.x) nb.x = pbb.x;
                                    if (pbb.y < nb.y) nb.y = pbb.y;
                                    if (pbb.x + pbb.width > nb.r) nb.r = pbb.x + pbb.width;
                                    if (pbb.y + pbb.height > nb.b) nb.b = pbb.y + pbb.height;
                                }
                            } catch(e) {}
                        }
                        if (!nb) return;
                        var collected = new Set(grp.texts);
                        for (var ci = 0; ci < children.length; ci++) {
                            if (children[ci].tagName !== 'text') continue;
                            if (collected.has(children[ci])) continue;
                            try {
                                var tb = children[ci].getBBox();
                                if (tb.x >= nb.x - 2 && tb.x + tb.width <= nb.r + 2
                                    && tb.y >= nb.y - 2 && tb.y + tb.height <= nb.b + 2) {
                                    grp.texts.push(children[ci]);
                                }
                            } catch(e) {}
                        }
                    });
                    return groups;
                };
            }
        """);

        var result = await CheckContinuedFromText();
        Assert.False(result.Contains("contTextInGroup=-1"),
            $"After patch, continuation text should be in a group: {result}");
    }
}
