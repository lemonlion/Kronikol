# S10b-3's fix: the dependency colours' fill rules reached the component diagram, which the page draws in a
# .plantuml-browser box too, where a system box is filled #438DD5 with a white name: the dark theme's lighter blue
# left that name at about 2.5:1. A sequence diagram fills only an arrowhead in a call's colour (its participants take
# their roles), so the fill rules keep to polygons. usage: python scripts-s10b-e.py
P = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/inline-svg-styles.css'
s = open(P, encoding='utf-8', newline='').read()
for c, t in (('438dd5', 'http'), ('0e7c86', 'db'), ('9b59b6', 'msg')):
    old = '.plantuml-browser svg [fill="#' + c + '" i] { fill: var(--dg-' + t + '); }\n'
    assert s.count(old) == 1, c
    s = s.replace(old, '.plantuml-browser svg polygon[fill="#' + c + '" i] { fill: var(--dg-' + t + '); }\n')
old = '''   ground, ink and lines, the report's assertion strips and bars, and the dependency colours a call is drawn in. A
   user's DependencyColors or PlantUmlTheme writes colours no rule here knows: those keep their own. */'''
assert s.count(old) == 1
s = s.replace(old, '''   ground, ink and lines, the report's assertion strips and bars, and the dependency colours a call is drawn in (its line
   and its arrowhead: a box filled in one, a component diagram's system box, keeps it). A user's DependencyColors or
   PlantUmlTheme writes colours no rule here knows: those keep their own. An activity diagram (.iflow-diagram) is
   left as the engine draws it. */''')
# and the whole block keeps to a page-drawn sequence or component diagram: a scenario's whole-test flow is an activity
# diagram in a .plantuml-browser box too (.iflow-diagram), whose black text on its light boxes the dark theme's ink
# made unreadable; its colours are S11's, with the popup's
a = s.index("/* A sequence diagram's own colours")
b = s.index('/* A diagram narrower than its box')
block = s[a:b]
n = block.count('.plantuml-browser svg ')
assert n == 13, n
# :where() adds nothing to a rule's specificity, so a role (S10b-2, doubled classes) still outranks these rules
s = s[:a] + block.replace('.plantuml-browser svg ', '.plantuml-browser:where(:not(.iflow-diagram)) svg ') + s[b:]
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
