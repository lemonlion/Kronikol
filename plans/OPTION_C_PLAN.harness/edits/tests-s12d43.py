# S12 D43's tests: the Playwright class OpenInNewTabTests (copied in), DiagramContextMenuTests' two "open in new tab"
# facts turned round (the tab opens in the click and the blob: URL is only the fallback), and NoteCopyFidelityTests'
# box-text tab checked for its title. usage: python tests-s12d43.py <root>
import os, sys

ROOT = sys.argv[1].rstrip('/') + '/'
HERE = os.path.dirname(os.path.abspath(__file__))
CORE = ROOT + 'tests/Kronikol.Tests/Reports/DiagramContextMenuTests.cs'
COPY = ROOT + 'tests/Kronikol.Tests.EndToEnd/NoteCopyFidelityTests.cs'
NEW = ROOT + 'tests/Kronikol.Tests.EndToEnd/OpenInNewTabTests.cs'
assert not os.path.exists(NEW), 'the tests are in already'

OLD_CORE = '''    // Open in new tab — uses blob URLs not data URIs
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void OpenInNewTab_uses_blob_url_not_data_uri()
    {
        // PNG open should use createObjectURL, not FileReader + readAsDataURL
        Assert.DoesNotContain("readAsDataURL", _script);
        // SVG "Open in new tab" should not use window.open('data:image/svg+xml...')
        // (data:image/svg+xml;base64 still exists in svgToCanvas for img.src, which is fine)
        Assert.DoesNotContain("window.open('data:image/svg+xml", _script);
    }

    [Fact]
    public void OpenInNewTab_uses_createObjectURL_for_images()
    {
        // The "Open image in new tab" submenu items should use URL.createObjectURL
        Assert.Contains("window.open(URL.createObjectURL(blob))", _script);
    }
'''
NEW_CORE = '''    // Open in new tab: the tab opens while the click is handled and the picture or text is written into it
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void OpenInNewTab_opens_the_tab_before_the_picture_is_drawn()
    {
        // a popup blocker lets through a tab opened while the click is handled; the PNG is drawn after the click
        foreach (var item in new[] { "'Open as PNG image in new tab'", "'Open as PNG image (no transparency) in new tab'" })
        {
            var at = _script.IndexOf(item, StringComparison.Ordinal);
            Assert.True(at > 0, item);
            var open = _script.IndexOf("var tab = openTab();", at, StringComparison.Ordinal);
            var drawn = _script.IndexOf("openPngInTab(tab, canvas)", at, StringComparison.Ordinal);
            Assert.True(open > at && open < drawn, item);
        }
        Assert.DoesNotContain("readAsDataURL", _script);
        Assert.DoesNotContain("window.open('data:", _script);
    }

    [Fact]
    public void OpenInNewTab_writes_into_the_tab_and_opens_a_blob_url_only_where_no_tab_opened()
    {
        Assert.Contains("window.open('', '_blank')", _script);
        Assert.Contains("openSvgInTab(serializeSvg(svg))", _script);
        Assert.Contains("openTextInTab(source, typeLabel + ' source')", _script);
        Assert.Contains("openTextInTab(_fullNoteText, 'Box text')", _script);
        // the three fallbacks (PNG, SVG, text) are the only blob: URLs a tab is opened on
        Assert.Equal(3, _script.Split("window.open(URL.createObjectURL(").Length - 1);
    }
'''

OLD_COPY = '''        var shown = await popup.EvaluateAsync<string>("() => document.body.innerText");
'''
NEW_COPY = '''        // the tab is opened in the click and the text written into it, titled for what it holds
        await popup.WaitForFunctionAsync("() => document.title === 'Box text'", null, new() { Timeout = 15000, PollingInterval = 200 });
        Assert.False(popup.Url.StartsWith("blob:", StringComparison.Ordinal), "the tab shows " + popup.Url);
        var shown = await popup.EvaluateAsync<string>("() => document.body.innerText");
'''

core = open(CORE, encoding='utf-8-sig', newline='').read()
copy = open(COPY, encoding='utf-8-sig', newline='').read()
assert core.count(OLD_CORE) == 1, 'DiagramContextMenuTests anchor'
assert copy.count(OLD_COPY) == 1, 'NoteCopyFidelityTests anchor'
bom_core = open(CORE, 'rb').read(3) == b'\xef\xbb\xbf'
bom_copy = open(COPY, 'rb').read(3) == b'\xef\xbb\xbf'
source = open(os.path.join(HERE, 'OpenInNewTabTests.cs'), encoding='utf-8', newline='').read()
open(CORE, 'w', encoding='utf-8-sig' if bom_core else 'utf-8', newline='').write(core.replace(OLD_CORE, NEW_CORE))
open(COPY, 'w', encoding='utf-8-sig' if bom_copy else 'utf-8', newline='').write(copy.replace(OLD_COPY, NEW_COPY))
open(NEW, 'w', encoding='utf-8', newline='').write(source)
print('ok: DiagramContextMenuTests.cs, NoteCopyFidelityTests.cs, OpenInNewTabTests.cs')
