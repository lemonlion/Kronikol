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

    private const string SrcPath = @"C:\Users\cex\Downloads\buglatest\TestRunReport.html";

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
    /// Reproduces: hover buttons appear on the WoW note, but clicking the
    /// expand (down arrow) button expands the WRONG note (the sister response
    /// note to the right) instead of the WoW note itself.
    /// </summary>
    [Fact]
    public async Task BugTest_expand_button_expands_wrong_note()
    {
        var dest = Path.Combine(_tmp, "BugTest.html");
        File.Copy(SrcPath, dest, true);
        await NavigateExpandAndRender(dest);

        // Find the WoW note, get its height BEFORE clicking expand
        var before = await _pg.EvaluateAsync<string>("""
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
                    // Find the note path containing WoW (smallest enclosing path)
                    var mainG = svgs[si].querySelector('g');
                    var children = Array.from(mainG.children);
                    var wowBB = wowEl.getBBox();
                    var notePath = null;
                    for (var ci = 0; ci < children.length; ci++) {
                        if (children[ci].tagName !== 'path') continue;
                        var fill = (children[ci].getAttribute('fill') || '').toLowerCase();
                        if (!fill || fill === 'none' || fill === '#000' || fill === '#000000') continue;
                        try {
                            var pb = children[ci].getBBox();
                            if (wowBB.x >= pb.x-2 && wowBB.x+wowBB.width <= pb.x+pb.width+2
                                && wowBB.y >= pb.y-2 && wowBB.y+wowBB.height <= pb.y+pb.height+2) {
                                if (!notePath || (pb.width*pb.height < notePath.area))
                                    notePath = {el:children[ci], area:pb.width*pb.height, x:pb.x, y:pb.y, w:pb.width, h:pb.height};
                            }
                        } catch(e) {}
                    }
                    if (!notePath) return 'NO_NOTE_PATH';
                    return 'BEFORE:noteSize=' + notePath.w.toFixed(0) + 'x' + notePath.h.toFixed(0)
                        + ' notePos=' + notePath.x.toFixed(0) + ',' + notePath.y.toFixed(0);
                }
                return 'NO_WOW';
            })()
        """);

        Assert.StartsWith("BEFORE:", before);

        // Hover the note to show buttons, then click the expand (down arrow) button
        var expandResult = await _pg.EvaluateAsync<string>("""
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
                    // Trigger hover to show buttons
                    wowGroup.paths[0].dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));
                    // Find the expand (down arrow ▼) button on this note
                    var frag = svgs[si].closest('.puml-fragment') || svgs[si].closest('[data-diagram-type]');
                    var icons = frag.querySelectorAll('.note-toggle-icon');
                    var bbox = window._getNoteBBox(wowGroup);
                    // Find the down-arrow (▼) button — it's a note-toggle-icon without
                    // data-note-btn attr (not minus/plus), containing ▼ text
                    var expandBtn = null;
                    for (var i = 0; i < icons.length; i++) {
                        if (icons[i].style.opacity === '0') continue;
                        var btn = icons[i].getAttribute('data-note-btn');
                        if (btn === 'minus' || btn === 'plus') continue;
                        var txt = icons[i].querySelector('text');
                        if (!txt || txt.textContent.indexOf('▼') < 0) continue;
                        var rect = icons[i].querySelector('rect');
                        if (!rect) continue;
                        var ix = parseFloat(rect.getAttribute('x'));
                        var iy = parseFloat(rect.getAttribute('y'));
                        if (ix >= bbox.x-5 && ix <= bbox.x+bbox.width+5
                            && iy >= bbox.y-5 && iy <= bbox.y+bbox.height+5) {
                            expandBtn = icons[i]; break;
                        }
                    }
                    if (!expandBtn) return 'NO_EXPAND_BTN_ON_NOTE';
                    // Click the expand button's rect
                    var clickTarget = expandBtn.querySelector('rect');
                    clickTarget.dispatchEvent(new MouseEvent('click', {bubbles:true}));
                    return 'CLICKED';
                }
                return 'NO_WOW';
            })()
        """);

        Assert.Equal("CLICKED", expandResult);

        // Wait for re-render
        await _pg.WaitForTimeoutAsync(2000);
        await _pg.WaitForFunctionAsync("() => !window._plantumlRendering", null, new() { Timeout = 30000, PollingInterval = 200 });
        await _pg.WaitForFunctionAsync("() => { var c = document.querySelectorAll('[data-diagram-type=\"plantuml\"]'); for(var i=0;i<c.length;i++) if(c[i]._noteRendering) return false; return true; }", null, new() { Timeout = 30000, PollingInterval = 200 });

        // Check: did the WoW note expand (get taller)?
        var after = await _pg.EvaluateAsync<string>("""
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
                    var mainG = svgs[si].querySelector('g');
                    var children = Array.from(mainG.children);
                    var wowBB = wowEl.getBBox();
                    var notePath = null;
                    for (var ci = 0; ci < children.length; ci++) {
                        if (children[ci].tagName !== 'path') continue;
                        var fill = (children[ci].getAttribute('fill') || '').toLowerCase();
                        if (!fill || fill === 'none' || fill === '#000' || fill === '#000000') continue;
                        try {
                            var pb = children[ci].getBBox();
                            if (wowBB.x >= pb.x-2 && wowBB.x+wowBB.width <= pb.x+pb.width+2
                                && wowBB.y >= pb.y-2 && wowBB.y+wowBB.height <= pb.y+pb.height+2) {
                                if (!notePath || (pb.width*pb.height < notePath.area))
                                    notePath = {area:pb.width*pb.height, w:pb.width, h:pb.height};
                            }
                        } catch(e) {}
                    }
                    if (!notePath) return 'NO_NOTE_PATH_AFTER';
                    return 'AFTER:noteSize=' + notePath.w.toFixed(0) + 'x' + notePath.h.toFixed(0);
                }
                return 'NO_WOW_AFTER';
            })()
        """);

        Assert.StartsWith("AFTER:", after);

        // The WoW note should have grown (expanded). If it's still 179px tall,
        // the wrong note expanded instead.
        var beforeHeight = int.Parse(before.Split("noteSize=")[1].Split("x")[1].Split(" ")[0]);
        var afterHeight = int.Parse(after.Split("noteSize=")[1].Split("x")[1]);

        // Diagnose: what's the sourceIndexMap and group ordering?
        var diagnosis = await _pg.EvaluateAsync<string>("""
            (() => {
                var s = document.querySelector('details.scenario');
                var svgs = s.querySelectorAll('[data-diagram-type="plantuml"] svg');
                for (var si = 0; si < svgs.length; si++) {
                    var texts = svgs[si].querySelectorAll('text');
                    var hasWoW = false;
                    for (var ti = 0; ti < texts.length; ti++) {
                        if (texts[ti].textContent.indexOf('"WoW"') >= 0) { hasWoW = true; break; }
                    }
                    if (!hasWoW) continue;
                    var frag = svgs[si].closest('.puml-fragment') || svgs[si].closest('[data-diagram-type]');
                    var src = frag.getAttribute('data-plantuml') || '';
                    var blocks = window._parseNoteBlocks(src);
                    var groups = window._findNoteGroups(svgs[si]);
                    var info = ['blocks=' + blocks.length + ' groups=' + groups.length];
                    for (var bi = 0; bi < blocks.length; bi++) {
                        info.push('block[' + bi + ']: "' + blocks[bi].contentLines[0].substring(0, 50) + '"');
                    }
                    for (var gi = 0; gi < groups.length; gi++) {
                        var bb = window._getNoteBBox(groups[gi]);
                        var firstTxt = groups[gi].texts.length > 0 ? groups[gi].texts[0].textContent.substring(0, 30) : '(none)';
                        info.push('group[' + gi + ']: (' + bb.x.toFixed(0) + ',' + bb.y.toFixed(0) + ' ' + bb.width.toFixed(0) + 'x' + bb.height.toFixed(0) + ') "' + firstTxt + '"');
                    }
                    return info.join('\n');
                }
                return 'NO_WOW';
            })()
        """);

        Assert.True(afterHeight > beforeHeight,
            $"WoW note did not expand (wrong note expanded instead).\n{before}\n{after}");
    }
}
