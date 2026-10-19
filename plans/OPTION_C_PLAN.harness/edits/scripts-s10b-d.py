# S10b-3 (the stylesheet): the colours the engine and the emitter paint in a sequence diagram, mapped to the theme's
# tokens (P36), so a diagram the page draws takes the theme in both its light and its dark values: the preview's
# c-round5.css 90-110, keyed on the product's own literals since S10a (a database is #0E7C86 where the preview mapped
# #E74C3C). The light values are Option C's softer ink, lines and grounds. Only what a sequence diagram draws and the
# role pass leaves alone is mapped here, each rule held by DiagramThemeColoursTests: the ground, the ink, the lines and
# arrowheads, the assertion strips, a bar that is not a step, and the three dependency colours the palette draws calls
# in. The participants and notes take their colours from their roles (S10b-2); the internal-flow activity diagram's
# colours and the component diagram's go with their tabs in S11, where a report draws them. usage: python scripts-s10b-d.py
P = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/inline-svg-styles.css'
s = open(P, encoding='utf-8', newline='').read()
assert 'var(--dg-bg)' not in s
s = s.rstrip('\n') + '\n' + '''
/* A sequence diagram's own colours, mapped to the theme's tokens (5.0.0, plans/OPTION_C_PLAN.md S10): the engine's
   ground, ink and lines, the report's assertion strips and bars, and the dependency colours a call is drawn in. A
   user's DependencyColors or PlantUmlTheme writes colours no rule here knows: those keep their own. */
.plantuml-browser svg { background: var(--dg-bg) !important; }
.plantuml-browser svg text[fill="#000000" i] { fill: var(--dg-ink); }
.plantuml-browser svg :is([style*="stroke:#181818" i], [stroke="#181818" i]) { stroke: var(--dg-line) !important; }
.plantuml-browser svg polygon[fill="#181818" i] { fill: var(--dg-line); }
.plantuml-browser svg :is(path, polygon)[fill="#d4edda" i] { fill: var(--dg-pass); stroke: var(--dg-pass-edge) !important; }
.plantuml-browser svg :is(path, polygon)[fill="#f8d7da" i] { fill: var(--dg-fail); stroke: var(--dg-fail-edge) !important; }
.plantuml-browser svg polygon[fill="#000000" i] { fill: var(--dg-bar); }
.plantuml-browser svg [fill="#438dd5" i] { fill: var(--dg-http); }
.plantuml-browser svg :is([style*="stroke:#438dd5" i], [stroke="#438dd5" i]) { stroke: var(--dg-http) !important; }
.plantuml-browser svg [fill="#0e7c86" i] { fill: var(--dg-db); }
.plantuml-browser svg :is([style*="stroke:#0e7c86" i], [stroke="#0e7c86" i]) { stroke: var(--dg-db) !important; }
.plantuml-browser svg [fill="#9b59b6" i] { fill: var(--dg-msg); }
.plantuml-browser svg :is([style*="stroke:#9b59b6" i], [stroke="#9b59b6" i]) { stroke: var(--dg-msg) !important; }
'''
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
