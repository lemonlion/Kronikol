using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

public class BugReproTests : IAsyncLifetime
{
    private IPlaywright _pw = null!;
    private IBrowser _br = null!;
    private IBrowserContext _ctx = null!;
    private IPage _pg = null!;
    private readonly string _tmp;

    public BugReproTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "kronikol-bug-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tmp);
    }

    public async ValueTask InitializeAsync()
    {
        _pw = await Playwright.CreateAsync();
        _br = await _pw.Chromium.LaunchAsync(new() { Headless = false, SlowMo = 100 });
        _ctx = await _br.NewContextAsync(new() { ViewportSize = new() { Width = 1920, Height = 1080 } });
        _pg = await _ctx.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync(); await _br.DisposeAsync(); _pw.Dispose();
        try { Directory.Delete(_tmp, true); } catch { }
    }

    private const string SrcPath = @"C:\Users\cex\Downloads\bug-aftermath\TestRunReport.html";

    private async Task NavigateExpandAndRender(string htmlPath)
    {
        await _pg.GotoAsync(new Uri(htmlPath).AbsoluteUri);
        await _pg.Locator("details.feature").First.WaitForAsync();
        await _pg.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await _pg.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
        await _pg.WaitForTimeoutAsync(3000);
        await _pg.Locator("details.scenario").First.ScrollIntoViewIfNeededAsync();
        await _pg.WaitForTimeoutAsync(3000);
        await _pg.EvaluateAsync("() => { var s = document.querySelector('details.scenario'); if (s && window._renderDiagramsInContainer) window._renderDiagramsInContainer(s); }");
        await _pg.WaitForFunctionAsync("() => { var s = document.querySelector('details.scenario'); return s && s.querySelectorAll('[data-diagram-type=\"plantuml\"] svg').length >= 2 && !window._plantumlRendering; }", null, new() { Timeout = 120000, PollingInterval = 500 });
        await _pg.WaitForFunctionAsync("() => { var c = document.querySelectorAll('[data-diagram-type=\"plantuml\"]'); for(var i=0;i<c.length;i++) if(c[i]._noteRendering) return false; return true; }", null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    /// <summary>
    /// Reproduces: The continuation note (229x179, "WoW") shows a minus button
    /// but NO expand (▼) button. This means isLongNote returns false for the
    /// continuation note — a regression from the fragContinuationMap fix which
    /// maps to original index 0 whose origContentLines may not match this chunk.
    /// </summary>
    [Fact]
    public async Task BugTest_continuation_note_missing_expand_button()
    {
        var dest = Path.Combine(_tmp, "BugTest.html");
        File.Copy(SrcPath, dest, true);
        await NavigateExpandAndRender(dest);

        var result = await _pg.EvaluateAsync<string>("""
            (() => {
                var s = document.querySelector('details.scenario');
                var svgs = s.querySelectorAll('[data-diagram-type="plantuml"] svg');
                for (var si = 0; si < svgs.length; si++) {
                    var texts = svgs[si].querySelectorAll('text');
                    var wowEl = null;
                    for (var ti = 0; ti < texts.length; ti++) {
                        if (texts[ti].textContent.indexOf('"WoW"') >= 0) { wowEl = texts[ti]; break; }
                    }
                    if (!wowEl) continue;

                    var groups = window._findNoteGroups(svgs[si]);
                    var wowGroup = null;
                    var wowBB = wowEl.getBBox();
                    for (var gi = 0; gi < groups.length; gi++) {
                        var bb = window._getNoteBBox(groups[gi]);
                        if (wowBB.x >= bb.x-2 && wowBB.x+wowBB.width <= bb.x+bb.width+2
                            && wowBB.y >= bb.y-2 && wowBB.y+wowBB.height <= bb.y+bb.height+2) {
                            wowGroup = groups[gi]; break;
                        }
                    }
                    if (!wowGroup) return 'WOW_NOT_IN_GROUP';

                    // Hover to show buttons
                    wowGroup.paths[0].dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));

                    var frag = svgs[si].closest('.puml-fragment') || svgs[si].closest('[data-diagram-type]');
                    var bbox = window._getNoteBBox(wowGroup);
                    var icons = frag.querySelectorAll('.note-toggle-icon');
                    var hasMinus = false, hasExpand = false;
                    for (var i = 0; i < icons.length; i++) {
                        if (icons[i].style.opacity === '0') continue;
                        var rect = icons[i].querySelector('rect');
                        if (!rect) continue;
                        var ix = parseFloat(rect.getAttribute('x'));
                        var iy = parseFloat(rect.getAttribute('y'));
                        if (!(ix >= bbox.x-5 && ix <= bbox.x+bbox.width+5
                            && iy >= bbox.y-5 && iy <= bbox.y+bbox.height+5)) continue;
                        var btn = icons[i].getAttribute('data-note-btn');
                        if (btn === 'minus') hasMinus = true;
                        var txt = icons[i].querySelector('text');
                        if (txt && txt.textContent.indexOf('▼') >= 0) hasExpand = true;
                    }

                    return 'hasMinus=' + hasMinus + ' hasExpand=' + hasExpand
                        + ' noteSize=' + bbox.width.toFixed(0) + 'x' + bbox.height.toFixed(0);
                }
                return 'NO_WOW';
            })()
        """);

        // Diagnose: what step is the note in? what does isLongNote return?
        var diag = await _pg.EvaluateAsync<string>("""
            (() => {
                var s = document.querySelector('details.scenario');
                var svgs = s.querySelectorAll('[data-diagram-type="plantuml"] svg');
                for (var si = 0; si < svgs.length; si++) {
                    var texts = svgs[si].querySelectorAll('text');
                    var wowEl = null;
                    for (var ti = 0; ti < texts.length; ti++) {
                        if (texts[ti].textContent.indexOf('"WoW"') >= 0) { wowEl = texts[ti]; break; }
                    }
                    if (!wowEl) continue;
                    var frag = svgs[si].closest('.puml-fragment');
                    var container = svgs[si].closest('[data-diagram-type]');
                    var steps = container._noteSteps || {};
                    var stepsStr = JSON.stringify(steps);
                    var src = frag ? frag.getAttribute('data-plantuml') || '' : '';
                    var blocks = window._parseNoteBlocks(src);
                    var ownerBlocks = container._noteOriginalSource ?
                        window._parseNoteBlocks(container._noteOriginalSource) : [];
                    return 'steps=' + stepsStr
                        + ' fragBlocks=' + blocks.length
                        + ' ownerBlocks=' + ownerBlocks.length
                        + ' truncateLines=' + (container._truncateLines || 'null')
                        + ' block0Lines=' + (blocks[0] ? blocks[0].contentLines.length : 0)
                        + ' ownerBlock0Lines=' + (ownerBlocks[0] ? ownerBlocks[0].contentLines.length : 0);
                }
                return 'NO_WOW';
            })()
        """);

        // The bug: minus shows but expand (▼) doesn't
        Assert.Contains("hasMinus=true", result);
        Assert.Contains("hasExpand=true", result);
    }
}
