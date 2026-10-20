# S12 D43 (round 17 item 21): the diagram menu's "in new tab" items open the tab while the click is handled and write
# the picture or the text into it; where no tab opens, the blob: URL is tried as before. usage: python scripts-s12d43.py <root>
import sys

ROOT = sys.argv[1].rstrip('/') + '/'
CMS = ROOT + 'src/Kronikol/Reports/context-menu-script.js'
s = open(CMS, encoding='utf-8', newline='').read()
assert 'function openTextInTab(' not in s, 'D43 is in already'
assert '\r' not in s

HELPERS_AT = '    function createMenuItem(label, action) {\n'
HELPERS = '''    // "In new tab" (5.0.0, plans/OPTION_C_PLAN.md S12, round 17 item 21): the tab is opened while the click is handled,
    // which a popup blocker allows, and the picture or the text is written into it. A PNG item used to open its tab
    // only once the picture was drawn, after the click, which Firefox refused, and a blob: URL made by a file:// page has
    // an opaque origin. Where no tab opens, the blob: URL is tried as before.
    function escapeTabText(s) {
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function openTab() {
        try { return window.open('', '_blank'); } catch (e) { return null; }
    }

    function writeTab(tab, title, body, ground) {
        tab.document.open();
        tab.document.write('<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">' +
            '<title>' + escapeTabText(title) + '</title><body style="margin:0;padding:16px;background:' + ground + '">' + body + '</body>');
        tab.document.close();
    }

    // The tab is opened by the click, before the picture is drawn; the picture is written once it is.
    function openPngInTab(tab, canvas) {
        if (!tab || tab.closed) {
            canvas.toBlob(function(blob) {
                window.open(URL.createObjectURL(blob));
            }, 'image/png');
            return;
        }
        writeTab(tab, 'Diagram (PNG)', '<img alt="Diagram" style="max-width:none" src="' + canvas.toDataURL('image/png') + '">', '#f5f7fb');
    }

    function openSvgInTab(markup) {
        var tab = openTab();
        if (!tab) {
            window.open(URL.createObjectURL(new Blob([markup], { type: 'image/svg+xml' })));
            return;
        }
        writeTab(tab, 'Diagram (SVG)', '<img alt="Diagram" style="max-width:none" src="data:image/svg+xml;base64,' +
            btoa(unescape(encodeURIComponent(markup))) + '">', '#ffffff');
    }

    function openTextInTab(text, title) {
        var tab = openTab();
        if (!tab) {
            window.open(URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' })));
            return;
        }
        writeTab(tab, title, '<pre style="margin:0;white-space:pre-wrap;font:13px/1.45 ui-monospace,Consolas,monospace">' +
            escapeTabText(text) + '</pre>', '#ffffff');
    }

'''

SITES = [
    ('''                createMenuItem('Open as PNG image in new tab', function() {
                    svgToCanvas(svg, function(canvas) {
                        canvas.toBlob(function(blob) {
                            window.open(URL.createObjectURL(blob));
                        }, 'image/png');
                    });
                }),
''', '''                createMenuItem('Open as PNG image in new tab', function() {
                    var tab = openTab();
                    svgToCanvas(svg, function(canvas) { openPngInTab(tab, canvas); });
                }),
'''),
    ('''                createMenuItem('Open as PNG image (no transparency) in new tab', function() {
                    svgToCanvasWithBg(svg, function(canvas) {
                        canvas.toBlob(function(blob) {
                            window.open(URL.createObjectURL(blob));
                        }, 'image/png');
                    });
                }),
''', '''                createMenuItem('Open as PNG image (no transparency) in new tab', function() {
                    var tab = openTab();
                    svgToCanvasWithBg(svg, function(canvas) { openPngInTab(tab, canvas); });
                }),
'''),
    ('''                createMenuItem('Open as SVG image in new tab', function() {
                    var blob = new Blob([serializeSvg(svg)], { type: 'image/svg+xml' });
                    window.open(URL.createObjectURL(blob));
                })
''', '''                createMenuItem('Open as SVG image in new tab', function() {
                    openSvgInTab(serializeSvg(svg));
                })
'''),
    ('''                        createMenuItem('Open full ' + typeLabel + ' in new tab', function() {
                            var blob = new Blob([origSource2], { type: 'text/plain;charset=utf-8' });
                            window.open(URL.createObjectURL(blob));
                        }),
                        createMenuItem('Open current ' + typeLabel + ' in new tab', function() {
                            var blob = new Blob([source], { type: 'text/plain;charset=utf-8' });
                            window.open(URL.createObjectURL(blob));
                        })
''', '''                        createMenuItem('Open full ' + typeLabel + ' in new tab', function() {
                            openTextInTab(origSource2, typeLabel + ' source');
                        }),
                        createMenuItem('Open current ' + typeLabel + ' in new tab', function() {
                            openTextInTab(source, typeLabel + ' source');
                        })
'''),
    ('''                    menu.appendChild(createMenuItem('Open ' + typeLabel + ' source in new tab', function() {
                        var blob = new Blob([source], { type: 'text/plain;charset=utf-8' });
                        window.open(URL.createObjectURL(blob));
                    }));
''', '''                    menu.appendChild(createMenuItem('Open ' + typeLabel + ' source in new tab', function() {
                        openTextInTab(source, typeLabel + ' source');
                    }));
'''),
    ('''                        createMenuItem('Open full box text in new tab', function() {
                            var blob = new Blob([_fullNoteText], { type: 'text/plain;charset=utf-8' });
                            window.open(URL.createObjectURL(blob));
                        }),
                        createMenuItem('Open current box text in new tab', function() {
                            var blob = new Blob([_currentNoteText], { type: 'text/plain;charset=utf-8' });
                            window.open(URL.createObjectURL(blob));
                        })
''', '''                        createMenuItem('Open full box text in new tab', function() {
                            openTextInTab(_fullNoteText, 'Box text');
                        }),
                        createMenuItem('Open current box text in new tab', function() {
                            openTextInTab(_currentNoteText, 'Box text');
                        })
'''),
    ('''                    menu.appendChild(createMenuItem('Open box text in new tab', function() {
                        var blob = new Blob([_fullNoteText], { type: 'text/plain;charset=utf-8' });
                        window.open(URL.createObjectURL(blob));
                    }));
''', '''                    menu.appendChild(createMenuItem('Open box text in new tab', function() {
                        openTextInTab(_fullNoteText, 'Box text');
                    }));
'''),
]

assert s.count(HELPERS_AT) == 1, 'createMenuItem'
for old, new in SITES:
    assert s.count(old) == 1, old.splitlines()[0]
for old, new in SITES:
    s = s.replace(old, new)
s = s.replace(HELPERS_AT, HELPERS + HELPERS_AT)
# every site now goes through the helpers: the only window.open(URL.createObjectURL left are the three fallbacks
assert s.count('window.open(URL.createObjectURL(') == 3, s.count('window.open(URL.createObjectURL(')
open(CMS, 'w', encoding='utf-8', newline='').write(s)
print('ok: src/Kronikol/Reports/context-menu-script.js')
