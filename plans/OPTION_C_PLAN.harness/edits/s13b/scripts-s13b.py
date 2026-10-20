# S13b's product edits: the colours the tokens never reached (the base sheet, the diagram's menu, the inline-SVG sheet,
# the sparkline) and the rules and the script for markup no page writes. Checks every anchor (its count) in memory and
# writes nothing unless all hold; keeps BOMs and line endings; refuses a second run.
# usage: python scripts-s13b.py <root>
import os, sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
SHEET = 'src/Kronikol/Reports/stylesheets.css'
MENU = 'src/Kronikol/Reports/context-menu-styles.css'
SVG = 'src/Kronikol/Reports/inline-svg-styles.css'
HISTORY_CSS = 'src/Kronikol/Reports/history-styles.css'
HISTORY_HTML = 'src/Kronikol/History/HistoryHtml.cs'
RG = 'src/Kronikol/Reports/ReportGenerator.cs'
DEAD_SCRIPT = 'src/Kronikol/Reports/report-toggle-examples-detail-function.js'

EDITS = {
    SHEET: [
        ("        #searchbar:disabled {\n            background: rgb(245, 245, 245);\n",
         "        #searchbar:disabled {\n            background: var(--kron-strip);\n"),
        ("            border: 1px solid rgb(180, 180, 180);\n            border-radius: 0.4em;\n            font-size: 0.85em;\n        }\n\n"
         "        .duration-filters-unit {\n            font-size: 0.85em;\n            color: rgb(100, 100, 100);\n        }\n",
         "            border: 1px solid var(--kron-control-border);\n            border-radius: 0.4em;\n            font-size: 0.85em;\n        }\n\n"
         "        .duration-filters-unit {\n            font-size: 0.85em;\n            color: var(--kron-muted);\n        }\n"),
        ("            font-style: italic;\n            color: #a33;\n        }\n",
         "            font-style: italic;\n            color: var(--kron-fail);\n        }\n"),
        ("\n        .examples-table {\n            border-collapse: collapse;\n            margin: 0.5em 0 0.5em 1em;\n            font-size: 0.9em;\n        }\n\n"
         "        .examples-table th, .examples-table td {\n            border: 1px solid #ddd;\n            padding: 0.3em 0.6em;\n            text-align: left;\n        }\n\n"
         "        .examples-table thead {\n            background: #f0f0f0;\n        }\n\n"
         "        .examples-row-passed { background: #f0fff0; }\n        .examples-row-failed { background: #fff0f0; cursor: pointer; }\n"
         "        .examples-row-skipped { background: #fff8e1; }\n        .examples-row-bypassed { background: #f0f0ff; }\n"
         "        .examples-row-expandable:hover { background: #ffe0e0; }\n        .examples-row-expanded { background: #ffe0e0; }\n"
         "        .examples-detail-row td { padding: 0.5em 1em; background: #fff5f5; }\n",
         ""),
        ("\n            .examples-table {\n                display: block;\n                overflow-x: auto;\n                margin-left: 0;\n            }\n",
         ""),
        ("        .row-surplus td:first-child { color: green; }\n        .row-missing td:first-child { color: red; }\n",
         ""),
        ("            background: none;\n            border: 1px solid #bbb;\n            border-radius: 3px;\n",
         "            background: none;\n            border: 1px solid var(--kron-control-border);\n            border-radius: 3px;\n"),
        ("            vertical-align: middle;\n            color: #555;\n        }\n        .flatten-toggle:hover { background: #ddd; color: #333; }\n",
         "            vertical-align: middle;\n            color: var(--kron-muted);\n        }\n        .flatten-toggle:hover { background: var(--kron-hover); color: var(--kron-ink); }\n"),
        ("        .no-interactions {\n            color: #8a8f98;\n",
         "        .no-interactions {\n            color: var(--kron-faint);\n"),
        ("\n        body > details > details > details.example {\n            background-color: white;\n            padding: 1em;\n            margin-top: 0;\n        }\n",
         ""),
    ],
    MENU: [
        (".diagram-ctx-menu {\n    position: fixed;\n    background: #fff;\n    border: 1px solid #ccc;\n",
         ".diagram-ctx-menu {\n    position: fixed;\n    background: var(--kron-card, #fff);\n    border: 1px solid var(--kron-hairline, #ccc);\n"
         "    color: var(--kron-ink, #242b36);\n"),
        (".diagram-ctx-menu > div:hover {\n    background: #e8f0fe;\n}\n",
         ".diagram-ctx-menu > div:hover {\n    background: var(--kron-hover, #e8f0fe);\n}\n"),
        ("    border-top: 1px solid #e0e0e0;\n",
         "    border-top: 1px solid var(--kron-hairline-soft, #e0e0e0);\n"),
        ("    font-size: 8px;\n    color: #666;\n",
         "    font-size: 8px;\n    color: var(--kron-muted, #666);\n"),
        ("    top: -4px;\n    background: #fff;\n    border: 1px solid #ccc;\n",
         "    top: -4px;\n    background: var(--kron-card, #fff);\n    border: 1px solid var(--kron-hairline, #ccc);\n"),
        (".diagram-ctx-menu .submenu > div:hover {\n    background: #e8f0fe;\n}\n",
         ".diagram-ctx-menu .submenu > div:hover {\n    background: var(--kron-hover, #e8f0fe);\n}\n"),
    ],
    SVG: [
        ("    align-items: center;\n    color: #999;\n}\n",
         "    align-items: center;\n    color: var(--kron-faint, #767676);\n}\n"),
        ("    box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.5);\n",
         "    box-shadow: 0 0 0 3px color-mix(in srgb, var(--kron-accent, #3b82f6) 50%, transparent);\n"),
        ("    accent-color: rgb(59, 130, 246);\n",
         "    accent-color: var(--kron-accent, rgb(59, 130, 246));\n"),
    ],
    HISTORY_CSS: [
        ("            border: 1px solid rgba(0, 0, 0, 0.15);\n            vertical-align: middle;\n            background: rgb(230, 230, 230);\n        }\n",
         "            border: 1px solid var(--kron-hairline);\n            vertical-align: middle;\n"
         "            /* each kind of run, by the names HistoryHtml writes into the gradient, which is the whole paint (the inline\n"
         "               background shorthand leaves no ground of this sheet's under it) (5.0.0, plans/OPTION_C_PLAN.md S13b) */\n"
         "            --sp: var(--kron-pass-ring);\n            --sf: var(--kron-fail-solid);\n            --ss: var(--kron-skip);\n"
         "            --sa: var(--kron-track);\n            --su: var(--kron-hairline);\n            --sn: var(--kron-hairline-soft);\n"
         "            --so: var(--kron-warn);\n        }\n"),
    ],
    HISTORY_HTML: [
        ("        HistoryFormat.Passed => \"#228b22\",\n        HistoryFormat.Failed => \"#bf0000\",\n"
         "        HistoryFormat.Skipped or HistoryFormat.SkippedAfterFailure => \"#949494\",\n        HistoryFormat.Absent => \"#e6e6e6\",\n"
         "        HistoryFormat.Unknown => \"#c8c8c8\",\n        HistoryFormat.NotATest => \"#dcdcdc\",\n        _ => \"#b8a000\"\n",
         "        HistoryFormat.Passed => \"var(--sp)\",\n        HistoryFormat.Failed => \"var(--sf)\",\n"
         "        HistoryFormat.Skipped or HistoryFormat.SkippedAfterFailure => \"var(--ss)\",\n        HistoryFormat.Absent => \"var(--sa)\",\n"
         "        HistoryFormat.Unknown => \"var(--su)\",\n        HistoryFormat.NotATest => \"var(--sn)\",\n        _ => \"var(--so)\"\n"),
    ],
    RG: [
        ("        // Toggle examples detail row\n        var toggleExamplesDetailFunction = LoadResource(\"report-toggle-examples-detail-function.js\");\n\n",
         ""),
        ("                                {{toggleExamplesDetailFunction}}\n",
         ""),
    ],
}


def read(path):
    raw = open(ROOT + path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw[3:].decode('utf-8') if bom else raw.decode('utf-8')
    crlf = '\r\n' in text
    return text.replace('\r\n', '\n'), bom, crlf


def write(path, text, bom, crlf):
    if crlf:
        text = text.replace('\n', '\r\n')
    data = text.encode('utf-8')
    open(ROOT + path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + data)


history, _, _ = read(HISTORY_CSS)
assert '--sp: var(--kron-pass-ring)' not in history, 'S13b is in already: the sparkline reads its runs from the tokens'
assert os.path.isfile(ROOT + DEAD_SCRIPT), 'S13b is in already, or this is not a worktree root: ' + DEAD_SCRIPT

results = {}
for path, edits in EDITS.items():
    text, bom, crlf = read(path)
    for old, new in edits:
        n = text.count(old)
        assert n == 1, (path, n, old[:80])
        text = text.replace(old, new)
    results[path] = (text, bom, crlf)

for path, (text, bom, crlf) in results.items():
    write(path, text, bom, crlf)
os.remove(ROOT + DEAD_SCRIPT)
print('ok: ' + ', '.join(sorted(results)) + '; removed ' + DEAD_SCRIPT)
