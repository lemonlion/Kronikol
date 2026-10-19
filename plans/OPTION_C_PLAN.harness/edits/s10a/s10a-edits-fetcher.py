# S10a (the code): the fetcher sets the system font where a browser engine draws: the page's engine (BrowserJs) and the
# same engine under Node (NodeJs); the JVM (Server, Local) knows no system-ui. usage: python s10a-edits-fetcher.py <root>
import sys
P = sys.argv[1].rstrip('/') + '/src/Kronikol/DefaultDiagramsFetcher.cs'
s = open(P, encoding='utf-8', newline='').read()
old = "            diagramNoteWrapWidth: options.DiagramNoteWrapWidth).ToArray();"
new = ("            diagramNoteWrapWidth: options.DiagramNoteWrapWidth,\n"
       "            // the JS engine, in the page or under Node, draws in the system font; the JVM knows no system-ui (5.0.0)\n"
       "            systemUiFont: options.PlantUmlRendering is PlantUmlRendering.BrowserJs or PlantUmlRendering.NodeJs).ToArray();")
assert s.count(old) == 1, s.count(old)
open(P, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('ok')
