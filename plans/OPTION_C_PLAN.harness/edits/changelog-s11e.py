# S11e's changelog lines: an Added item after the Timeline's, a Changed (breaking) item at the end of that list.
# usage: python changelog-s11e.py <root>
import sys
p = sys.argv[1].rstrip('/') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
a1 = "  colours, and a bypassed scenario's bar is blue, as the run's ring draws it (orange before).\n"
b1 = a1 + (
    "- **The Components tab has its controls.** Where the page draws the diagram: Show Filtered (the start) or Full,\n"
    "  Direction Left to right (the start) or Top to bottom, and Call summaries Shown (the start) or Hidden. Filtered draws\n"
    "  the components and connections used by the scenarios the filters leave showing, read from the run's calls rather\n"
    "  than matched by name, and a note says how many scenarios it follows.\n")
a2 = "  settings. On a phone each bar is a sheet, its zones (Payloads, Format, Toggle) one under another.\n"
b2 = a2 + (
    "- **The component diagram's title heads the Components tab and leaves its drawing.** `ComponentDiagramOptions.Title`\n"
    "  is the tab's heading under every renderer; `ComponentDiagram.html` still draws it. A run whose calls give the diagram\n"
    "  no connection has no Components tab, where it drew the title alone.\n")
assert 'The Components tab has its controls.' not in s, 'in already'
assert s.count(a1) == 1 and s.count(a2) == 1
s = s.replace(a1, b1).replace(a2, b2)
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
