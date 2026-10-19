# S10b-3's second fix: a page that carries inline-svg-styles.css without the report's token sheet (ComponentDiagram.html,
# drawn by the page by default) resolved every mapped colour to nothing, and a stroke with no value computes to none: the
# component diagram's HTTP edges vanished. Each mapping falls back to the colour the engine painted, so such a page draws
# as it did before 5.0.0; the link rule falls back to the engine's link blue. usage: python scripts-s10b-f.py
P = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/inline-svg-styles.css'
s = open(P, encoding='utf-8', newline='').read()
a = s.index("/* A sequence diagram's own colours")
b = s.index('/* A diagram narrower than its box')
block = s[a:b]
FALLBACK = {'--dg-bg': 'transparent', '--dg-ink': '#000000', '--dg-line': '#181818', '--dg-pass': '#d4edda',
            '--dg-pass-edge': '#181818', '--dg-fail': '#f8d7da', '--dg-fail-edge': '#181818', '--dg-bar': '#000000',
            '--dg-http': '#438dd5', '--dg-db': '#0e7c86', '--dg-msg': '#9b59b6'}
n = 0
for tok, lit in FALLBACK.items():
    k = block.count('var(' + tok + ')')
    assert k, tok
    n += k
    block = block.replace('var(' + tok + ')', 'var(' + tok + ', ' + lit + ')')
assert n == 15, n
old = """   PlantUmlTheme writes colours no rule here knows: those keep their own. An activity diagram (.iflow-diagram) is
   left as the engine draws it. */"""
assert block.count(old) == 1
block = block.replace(old, """   PlantUmlTheme writes colours no rule here knows: those keep their own. An activity diagram (.iflow-diagram) is
   left as the engine draws it, and so is any diagram on a page without the theme's tokens (ComponentDiagram.html):
   each colour falls back to the engine's own. */""")
s = s[:a] + block + s[b:]
old = '{ fill: var(--kron-accent) !important; }'
assert s.count(old) == 1
s = s.replace(old, '{ fill: var(--kron-accent, #0000ff) !important; }')
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok', n)
