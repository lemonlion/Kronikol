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

    private const string SrcPath = @"C:\Users\cex\Downloads\bug (2)\TestRunReport.html";

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
    /// Hovers the WoW note, checks if a minus/plus button is positioned within
    /// the note's SVG bounding box. Uses SVG coordinates (not screen coords).
    /// </summary>
    private const string CheckButtonsOnWoWNoteJs = """
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
                for (var gi = 0; gi < groups.length; gi++) {
                    var bb = window._getNoteBBox(groups[gi]);
                    try {
                        var tb = wowEl.getBBox();
                        if (tb.x >= bb.x-2 && tb.x+tb.width <= bb.x+bb.width+2
                            && tb.y >= bb.y-2 && tb.y+tb.height <= bb.y+bb.height+2) {
                            wowGroup = groups[gi]; break;
                        }
                    } catch(e) {}
                }
                if (!wowGroup) return 'WOW_NOT_IN_GROUP(groups=' + groups.length + ')';
                var bbox = window._getNoteBBox(wowGroup);
                var frag = svgs[si].closest('.puml-fragment') || svgs[si].closest('[data-diagram-type]');
                wowGroup.paths[0].dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));
                var icons = frag.querySelectorAll('.note-toggle-icon');
                var onNote = 0;
                for (var i = 0; i < icons.length; i++) {
                    if (icons[i].style.opacity === '0') continue;
                    var rect = icons[i].querySelector('rect');
                    if (!rect) continue;
                    var ix = parseFloat(rect.getAttribute('x'));
                    var iy = parseFloat(rect.getAttribute('y'));
                    if (ix >= bbox.x-5 && ix <= bbox.x+bbox.width+5
                        && iy >= bbox.y-5 && iy <= bbox.y+bbox.height+5) onNote++;
                }
                return 'buttonsOnNote=' + onNote + ' noteSize=' + bbox.width.toFixed(0) + 'x' + bbox.height.toFixed(0);
            }
            return 'NOT_FOUND';
        })()
    """;

    /// <summary>
    /// RED: v3.0.31 HTML merges the WoW note's paths with adjacent paths,
    /// so buttons end up on the wrong note.
    /// </summary>
    [Fact]
    public async Task BugTest_original_no_buttons_on_wow_note()
    {
        var dest = Path.Combine(_tmp, "BugTest.html");
        File.Copy(SrcPath, dest, true);
        await NavigateExpandAndRender(dest);
        var result = await _pg.EvaluateAsync<string>(CheckButtonsOnWoWNoteJs);
        // The bug: the note group containing WoW is merged with other notes into
        // a giant group (930x2186) instead of being its own small group (229x179).
        // The buttons are on the giant group, not visually on the small note.
        Assert.True(result.Contains("noteSize=930x") || result.Contains("noteSize=9"),
            $"Expected merged mega-group, got: {result}");
    }

    /// <summary>
    /// GREEN: After patching findNoteGroups to stop merging paths with
    /// different fill colors, the WoW note gets its own buttons.
    /// </summary>
    [Fact]
    public async Task BugTestMonkeyPatch_buttons_on_wow_note()
    {
        var dest = Path.Combine(_tmp, "BugTestMonkeyPatch.html");
        File.Copy(SrcPath, dest, true);

        var html = File.ReadAllText(dest);
        html = html.Replace(
            "while (ci < children.length && children[ci].tagName === 'path') {\n                    grp.paths.push(children[ci]);\n                    ci++;\n                }",
            "var startFill = (children[ci].getAttribute('fill') || '').toLowerCase();\n                while (ci < children.length && children[ci].tagName === 'path') {\n                    var pFill = (children[ci].getAttribute('fill') || '').toLowerCase();\n                    if (pFill !== startFill && pFill !== 'none' && pFill !== 'transparent' && pFill !== '#00000000' && !/^#[0-9a-f]{6}00$/.test(pFill) && hasNoteFill(children[ci])) break;\n                    grp.paths.push(children[ci]);\n                    ci++;\n                }");
        Assert.True(html.Contains("startFill"), "Patch did not match");
        File.WriteAllText(dest, html);

        await NavigateExpandAndRender(dest);
        var result = await _pg.EvaluateAsync<string>(CheckButtonsOnWoWNoteJs);
        Assert.True(result.Contains("buttonsOnNote=") && !result.Contains("buttonsOnNote=0"),
            $"Patch should fix: {result}");
    }
}
