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

    private const string CheckExpandButtonJs = """
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
                    if (icons[i].getAttribute('data-note-btn') === 'minus') hasMinus = true;
                    var txt = icons[i].querySelector('text');
                    if (txt && (txt.textContent.indexOf('▼') >= 0 || txt.textContent.indexOf('▲') >= 0)) hasExpand = true;
                }
                return 'hasMinus=' + hasMinus + ' hasExpand=' + hasExpand + ' noteSize=' + bbox.width.toFixed(0) + 'x' + bbox.height.toFixed(0);
            }
            return 'NO_WOW';
        })()
    """;

    /// <summary>
    /// Step 1: Load original v3.0.37 HTML, confirm bug exists (no expand button).
    /// </summary>
    [Fact]
    public async Task BugTest_original_no_expand_button()
    {
        var dest = Path.Combine(_tmp, "BugTest.html");
        File.Copy(SrcPath, dest, true);
        await NavigateExpandAndRender(dest);
        var result = await _pg.EvaluateAsync<string>(CheckExpandButtonJs);
        Assert.Contains("hasMinus=true", result);
        Assert.Contains("hasExpand=false", result);
    }

    /// <summary>
    /// Steps 2-6: Copy, monkey-patch the origContentLines logic, verify expand button appears.
    /// </summary>
    [Fact]
    public async Task BugTestMonkeyPatch_expand_button_appears()
    {
        var dest = Path.Combine(_tmp, "BugTestMonkeyPatch.html");
        File.Copy(SrcPath, dest, true);

        // Patch the HTML: add forceIsLong for continuation notes
        var html = File.ReadAllText(dest);

        // 1) Add forceIsLong variable after origContentLines
        var oldOrig = "var origContentLines = ownerNoteBlocks[globalIdx] ? ownerNoteBlocks[globalIdx].contentLines : noteBlocks[srcIdx].contentLines;";
        var newOrig = "var origContentLines = ownerNoteBlocks[globalIdx] ? ownerNoteBlocks[globalIdx].contentLines : noteBlocks[srcIdx].contentLines;\n                var forceIsLong = !!(fragContinuationMap && svgIdx === 0);";
        html = html.Replace(oldOrig, newOrig);

        // 2) Update isLongNote checks to include forceIsLong
        html = html.Replace(
            "if (!isLongNote(origContentLines, container._truncateLines, owner._headersHidden) && step === 1) step = 2;",
            "if (!forceIsLong && !isLongNote(origContentLines, container._truncateLines, owner._headersHidden) && step === 1) step = 2;");

        // 3) Update createNoteButtons signature and longNote check
        html = html.Replace(
            "function createNoteButtons(svg, bbox, noteStep, onExpand, onContract, onTruncate, onCycle, contentLines, grp, container) {",
            "function createNoteButtons(svg, bbox, noteStep, onExpand, onContract, onTruncate, onCycle, contentLines, grp, container, forceIsLong) {");
        html = html.Replace(
            "var longNote = isLongNote(contentLines, container._truncateLines, hdrHidden);",
            "var longNote = forceIsLong || isLongNote(contentLines, container._truncateLines, hdrHidden);");

        // 4) Pass forceIsLong to createNoteButtons
        html = html.Replace(
            "origContentLines, grp, container);",
            "origContentLines, grp, container, forceIsLong);");

        // 5) Update isLongNote calls inside onExpand/onCycle closures
        html = html.Replace(
            "var long = isLongNote(origContentLines, container._truncateLines, owner._headersHidden);\n                        var curStep = owner._noteSteps[globalIdx] || 0;\n                        setNoteState(owner, globalIdx, (long && curStep === 0) ? 1 : 2);",
            "var long = forceIsLong || isLongNote(origContentLines, container._truncateLines, owner._headersHidden);\n                        var curStep = owner._noteSteps[globalIdx] || 0;\n                        setNoteState(owner, globalIdx, (long && curStep === 0) ? 1 : 2);");

        Assert.True(html.Contains("forceIsLong"), "Monkey-patch did not apply");
        File.WriteAllText(dest, html);

        await NavigateExpandAndRender(dest);

        // Diagnose: what does isLongNote return for the continuation note?
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
                    var src = frag ? frag.getAttribute('data-plantuml') || '' : '';
                    var blocks = window._parseNoteBlocks(src);
                    var truncLines = container._truncateLines || window._truncateLines || 40;
                    var headersHidden = container._headersHidden || false;
                    // Check isLongNote for block[0]
                    var b0Lines = blocks.length > 0 ? blocks[0].contentLines.length : 0;
                    // isLongNote isn't on window, compute manually
                    var b0ContentLines = blocks.length > 0 ? blocks[0].contentLines : [];
                    var visibleCount = 0;
                    for (var li = 0; li < b0ContentLines.length; li++) {
                        if (!headersHidden || !/^<color:gray>/.test(b0ContentLines[li].trim())) visibleCount++;
                    }
                    var isLong = visibleCount > truncLines;
                    // Check the step for globalIdx
                    var steps = container._noteSteps || {};
                    return 'block0Lines=' + b0Lines + ' truncLines=' + truncLines
                        + ' headersHidden=' + headersHidden
                        + ' isLongNote=' + isLong
                        + ' step0=' + (steps[0] || 0)
                        + ' step1=' + (steps[1] || 0)
                        + ' step2=' + (steps[2] || 0)
                        + ' _isLongNote_exists=' + (typeof window._isLongNote);
                }
                return 'NO_WOW';
            })()
        """);

        var result = await _pg.EvaluateAsync<string>(CheckExpandButtonJs);
        Assert.Contains("hasMinus=true", result);
        Assert.Contains("hasExpand=true", result);
    }
}
