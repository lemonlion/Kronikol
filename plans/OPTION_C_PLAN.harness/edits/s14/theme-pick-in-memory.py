# S14 fix: where the browser refuses the page its storage, the reader's pick held only until the system's setting
# changed, since the switch read the pick back from storage alone. It is kept on the page too.
# usage: python -I theme-pick-in-memory.py <repo root>
import sys

p = sys.argv[1] + '/src/Kronikol/Reports/report-theme-switch-script.js'
t = open(p, encoding='utf-8', newline='').read()
nl = '\r\n' if '\r\n' in t else '\n'
t = t.replace('\r\n', '\n')
edits = [
    ("    var system = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;\n"
     "    function stored() {\n"
     "        try {",
     "    var system = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;\n"
     "    // the reader's pick on this page, which holds where the browser refuses to store it\n"
     "    var picked = null;\n"
     "    function stored() {\n"
     "        if (picked) return picked;\n"
     "        try {"),
    ("        if (keep) { try { localStorage.setItem('kronikol.theme', t); } catch (e) { /* a private window: this page only */ } }\n",
     "        if (keep) {\n"
     "            picked = t;\n"
     "            try { localStorage.setItem('kronikol.theme', t); } catch (e) { /* storage refused: this page only */ }\n"
     "        }\n"),
]
for old, new in edits:
    assert t.count(old) == 1, old
    t = t.replace(old, new)
open(p, 'w', encoding='utf-8', newline='').write(t.replace('\n', nl))
print('ok')
