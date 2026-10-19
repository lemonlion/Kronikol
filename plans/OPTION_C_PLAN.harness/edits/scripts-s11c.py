# S11c: the internal-flow popup on the theme's tokens (mock c-round5.css 178-183, c-dark.css 93, c-contrast.css 24-30,
# c-extra.css 195, c-round17.css 107-110; stylesheet.md row 18). Selectors keep their 4.x specificity, so a user's
# InternalFlowPopupCustomStyleSheet, which follows this sheet, still wins on order; each colour falls back to its 4.x
# value for a page without the token sheet. The popup's error boxes and the generator's notes lose their inline
# colours for classes. usage: python scripts-s11c.py <root>
import sys
R = sys.argv[1].rstrip('/') + '/'

def edit(rel, pairs):
    p = R + rel
    s = open(p, encoding='utf-8', newline='').read()
    for old, new in pairs:
        assert s.count(old) == 1, (rel, old[:60], s.count(old))
        s = s.replace(old, new)
    open(p, 'w', encoding='utf-8', newline='').write(s)

edit('src/Kronikol/Reports/report-tokens.css', [
    ('\n    --kron-prop-val: #1a5276;\n', '\n    --kron-prop-val: #1a5276;\n    --kron-overlay: rgba(0, 0, 0, .4);\n'),
    ('\n    --kron-prop-val: #8fc3ec;\n', '\n    --kron-prop-val: #8fc3ec;\n    --kron-overlay: rgba(0, 0, 0, .6);\n'),
    ('\n        --kron-prop-val: #8fc3ec;\n', '\n        --kron-prop-val: #8fc3ec;\n        --kron-overlay: rgba(0, 0, 0, .6);\n'),
])

C = 'src/Kronikol/Reports/internal-flow-popup-styles.css'
F = "-apple-system, 'Segoe UI', sans-serif"
edit(C, [
    ('    background: rgba(0,0,0,0.4);\n', '    background: var(--kron-overlay, rgba(0,0,0,0.4));\n'),
    ('    background: #fff;\n    border-radius: 8px;', '    background: var(--kron-card, #fff);\n    border-radius: 8px;'),
    ("    font: 14px/1.5 " + F + ";\n}\n.iflow-popup-close", "    font: 14px/1.5 var(--kron-font, " + F + ");\n}\n.iflow-popup-close"),
    ('    font-size: 20px;\n    color: #666;', '    font-size: 20px;\n    color: var(--kron-muted, #666);'),
    ('.iflow-popup-close:hover {\n    color: #000;', '.iflow-popup-close:hover {\n    color: var(--kron-ink, #000);'),
    ('    font-size: 15px;\n    color: #333;', '    font-size: 15px;\n    color: var(--kron-ink, #333);'),
    ('    color: #888;\n    font-style: italic;\n    padding: 20px 0;', '    color: var(--kron-muted, #888);\n    font-style: italic;\n    padding: 20px 0;'),
    ('    color: #c00;\n    font-style: normal;', '    color: var(--kron-fail, #c00);\n    font-style: normal;'),
    ('    font: 13px/1.6 monospace;', '    font: 13px/1.6 var(--kron-mono, monospace);'),
    ('    border-left: 1px solid #ddd;', '    border-left: 1px solid var(--kron-hairline, #ddd);'),
    ('.iflow-source { color: #888;', '.iflow-source { color: var(--kron-faint, #888);'),
    ('.iflow-duration { color: #666;', '.iflow-duration { color: var(--kron-muted, #666);'),
    ('''.iflow-toggle-btn {
    padding: 4px 14px;
    border: 1px solid #ccc;
    background: #f5f5f5;
    cursor: pointer;
    font-size: 13px;
    border-radius: 4px;
    margin-right: 4px;
}
.iflow-toggle-btn:hover { background: #e8f0fe; }
.iflow-toggle-active { background: #4285f4; color: #fff; border-color: #4285f4; }
.iflow-toggle-active:hover { background: #3367d6; }
''', '''/* The popup's two views as tabs, drawn as a scenario's diagram tabs are (5.0.0, plans/OPTION_C_PLAN.md S11): quiet ink,
   the chosen one in the accent with its underline and no fill. One class each, as before, so a custom popup sheet wins. */
.iflow-toggle-btn {
    margin: 0 4px 0 0;
    padding: 6px 11px;
    border: 0;
    border-radius: 5px 5px 0 0;
    background: none;
    color: var(--kron-ink-quiet, #333);
    font: 400 13px/1.25 var(--kron-font, ''' + F + ''');
    box-shadow: inset 0 -2px 0 transparent;
    cursor: pointer;
}
.iflow-toggle-btn:hover { background: var(--kron-hover, #e8f0fe); color: var(--kron-ink, #000); }
.iflow-toggle-active, .iflow-toggle-active:hover {
    background: none;
    color: var(--kron-accent, #4285f4);
    font-weight: 600;
    box-shadow: inset 0 -2px 0 var(--kron-accent, #4285f4);
}
.iflow-toggle-btn:focus-visible { outline: 2px solid var(--kron-accent, #4285f4); outline-offset: 1px; }
'''),
    ("    font: 11px " + F + ";\n    color: #666;\n    padding: 2px 4px;", "    font: 11px var(--kron-font, " + F + ");\n    color: var(--kron-muted, #666);\n    padding: 2px 4px;"),
    ('.iflow-flame-label {\n    padding: 0 4px;', '.iflow-flame-label {\n    padding: 0 6px;\n    font-family: var(--kron-mono);'),
    ('    border-left: 1px dashed rgba(0,0,0,0.3);', '    border-left: 1px dashed color-mix(in srgb, var(--kron-ink, #000) 16%, transparent);'),
    ('.iflow-boundary-marker:hover { border-left-color: rgba(0,0,0,0.6);', '.iflow-boundary-marker:hover { border-left-color: color-mix(in srgb, var(--kron-ink, #000) 45%, transparent);'),
    ('font-weight: 600; color: #555; }', 'font-weight: 600; color: var(--kron-ink-soft, #555); }'),
    ('''    border: 1px solid #ccc;
    background: #f5f5f5;
    cursor: pointer;
    font-size: 13px;
    border-radius: 4px;
    margin-right: 1em;
}
.collapse-all-notes-btn:hover, .toggle-headers-btn:hover { background: #e8f0fe; }''', '''    border: 1px solid var(--kron-control-border, #ccc);
    background: var(--kron-field, #f5f5f5);
    color: var(--kron-ink);
    cursor: pointer;
    font-size: 13px;
    border-radius: 4px;
    margin-right: 1em;
}
.collapse-all-notes-btn:hover, .toggle-headers-btn:hover { background: var(--kron-hover, #e8f0fe); }'''),
    ('.iflow-test-band { border-bottom: 1px solid #eee;', '.iflow-test-band { border-bottom: 1px solid var(--kron-hairline-soft, #eee);'),
    ('.iflow-test-band-label { font: 11px/1.4 monospace; color: #888;', '.iflow-test-band-label { font: 11px/1.4 var(--kron-mono, monospace); color: var(--kron-faint, #888);'),
    ('border: 1px solid #e0e0e0; border-radius: 4px; margin: 4px 0; cursor: pointer; font: 13px/1.5 ' + F + '; }',
     'border: 1px solid var(--kron-hairline, #e0e0e0); border-radius: 4px; margin: 4px 0; cursor: pointer; font: 13px/1.5 var(--kron-font, ' + F + '); }'),
    ('.iflow-rel-list li:hover { background: #f0f4ff; border-color: #4285f4; }', '.iflow-rel-list li:hover { background: var(--kron-hover, #f0f4ff); border-color: var(--kron-accent, #4285f4); }'),
    ('border-collapse: collapse; font: 12px/1.5 ' + F + ';', 'border-collapse: collapse; font: 12px/1.5 var(--kron-font, ' + F + ');'),
    ('border-bottom: 2px solid #ddd; color: #555; }', 'border-bottom: 2px solid var(--kron-hairline, #ddd); color: var(--kron-ink-soft, #555); }'),
    ('border-bottom: 1px solid #eee; cursor: pointer; }', 'border-bottom: 1px solid var(--kron-hairline-soft, #eee); cursor: pointer; }'),
    ('.iflow-rel-summary-table tr:hover td { background: #f0f4ff; }', '.iflow-rel-summary-table tr:hover td { background: var(--kron-hover, #f0f4ff); }'),
    ('svg a.iflow-link-hover:hover text { fill: #0000EE;', 'svg a.iflow-link-hover:hover text { fill: var(--kron-accent, #0000EE);'),
    ('svg text.iflow-link-hover:hover { fill: #0000EE;', 'svg text.iflow-link-hover:hover { fill: var(--kron-accent, #0000EE);'),
])
s = open(R + C, encoding='utf-8', newline='').read()
s = s.rstrip('\n') + '''
/* What the popup writes in place of a diagram it cannot draw, and the notes the generator adds under one (5.0.0): in
   classes on the theme's colours, where each carried an inline colour. */
.iflow-render-error { color: var(--kron-fail, #c00); padding: 1em; border: 1px solid var(--kron-fail, #c00); border-radius: 6px; }
.iflow-render-error-text { color: var(--kron-fail, #c00); }
.iflow-left-out { font-size: 0.85em; color: var(--kron-muted, #666); margin: 0 0 8px; }
.iflow-diagnostics { font-size: 0.85em; color: var(--kron-muted, #666); }
.iflow-more-tests { color: var(--kron-faint, #888); font-style: italic; }
'''
open(R + C, 'w', encoding='utf-8', newline='').write(s)

box = '<div style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px">'
J = 'src/Kronikol/Reports/internal-flow-popup-script.js'
js = open(R + J, encoding='utf-8', newline='').read()
assert js.count(box) == 2 and js.count("el.style.color = '#c00';") == 1
js = js.replace(box, '<div class="iflow-render-error">').replace("el.style.color = '#c00';", "el.classList.add('iflow-render-error-text');")
open(R + J, 'w', encoding='utf-8', newline='').write(js)

G = 'src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs'
g = open(R + G, encoding='utf-8', newline='').read()
d_old = '<details style=\\"font-size:0.85em;color:#666\\">'
assert g.count(d_old) == 2, g.count(d_old)
g = g.replace(d_old, '<details class=\\"iflow-diagnostics\\">')
edit_pairs = [
    ('<p class=\\"iflow-left-out\\" style=\\"font-size:0.85em;color:#666;margin:0 0 8px\\">', '<p class=\\"iflow-left-out\\">'),
    ('<td colspan=\\"3\\" style=\\"color:#888;font-style:italic\\">', '<td colspan=\\"3\\" class=\\"iflow-more-tests\\">'),
]
for old, new in edit_pairs:
    assert g.count(old) == 1, old
    g = g.replace(old, new)
open(R + G, 'w', encoding='utf-8', newline='').write(g)
print('ok')
