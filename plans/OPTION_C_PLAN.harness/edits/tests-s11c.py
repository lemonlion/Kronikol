# S11c's tests: the popup pins that held 4.x's look turn to the tabs, the left-out note is read for the theme's muted
# ink with no inline colour, and IflowPopupThemeTests (Playwright) and PopupNotesByClassTests (core) are copied in.
# usage: python tests-s11c.py <root>
import shutil, sys, os
R = sys.argv[1].rstrip('/') + '/'
E = R + 'tests/Kronikol.Tests.EndToEnd/'

def edit(path, pairs):
    s = open(path, encoding='utf-8', newline='').read()
    for old, new in pairs:
        assert s.count(old) == 1, (path, old[:60], s.count(old))
        s = s.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(s)

edit(E + 'IflowPopupTests.cs', [
    ('''        // Toggle buttons: border-radius
''', '''        // Toggle buttons: tabs, rounded at the top (5.0.0)
'''),
    ('''            Assert.Equal("4px", radius);
''', '''            Assert.Equal("5px 5px 0px 0px", radius);
'''),
    ('''        // Active toggle: blue background
        var activeBg = await popup.Locator(".iflow-toggle-active")
            .EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.Equal("rgb(66, 133, 244)", activeBg);
''', '''        // Active toggle: a tab in the accent, underlined and unfilled (5.0.0), the accent's 4.x blue on a page without
        // the token sheet
        var active = await popup.Locator(".iflow-toggle-active")
            .EvaluateAsync<string[]>("el => { const cs = getComputedStyle(el); return [cs.backgroundColor, cs.color, cs.boxShadow]; }");
        Assert.Equal("rgba(0, 0, 0, 0)", active[0]);
        Assert.Equal("rgb(66, 133, 244)", active[1]);
        Assert.Contains("inset", active[2]);
'''),
])

edit(E + 'SpansLeftOutPopupTests.cs', [
    ('''        await Expect(note).ToHaveTextAsync("2 spans that started during this call are left out: a request of another test ran at the same time, and neither the trace nor the span tree says which of the two they belong to.");
        await popup.Locator''', '''        await Expect(note).ToHaveTextAsync("2 spans that started during this call are left out: a request of another test ran at the same time, and neither the trace nor the span tree says which of the two they belong to.");
        // in the theme's muted ink, from the popup's sheet, where it carried an inline grey (5.0.0)
        var ink = await note.EvaluateAsync<string[]>("""
            n => { const p = document.createElement('div'); p.style.color = 'var(--kron-muted)'; document.body.appendChild(p);
                   const muted = getComputedStyle(p).color; p.remove(); return [getComputedStyle(n).color, muted, n.getAttribute('style') ?? '']; }
            """);
        Assert.Equal(ink[1], ink[0]);
        Assert.Equal("", ink[2]);
        await popup.Locator'''),
])

here = os.path.dirname(os.path.abspath(__file__))
shutil.copyfile(os.path.join(here, 'IflowPopupThemeTests.cs'), E + 'IflowPopupThemeTests.cs')
shutil.copyfile(os.path.join(here, 'PopupNotesByClassTests.cs'), R + 'tests/Kronikol.Tests/InternalFlow/PopupNotesByClassTests.cs')
print('ok')
