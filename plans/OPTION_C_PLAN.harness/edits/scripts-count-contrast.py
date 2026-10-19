# v5 fix: More filters' count takes a new token, --kron-on-accent (the ink for text on the accent): white in the light
# block, as before (6.3:1 on #2559c7), and the dark page's ground ink #10161e in both dark blocks (7.5:1 on #7ea6f2),
# where white read at 2.4:1. usage: python scripts-count-contrast.py <root>
import sys

root = sys.argv[1].rstrip('/') + '/src/Kronikol/Reports/'
t = root + 'report-tokens.css'
s = open(t, encoding='utf-8', newline='').read()
assert '--kron-on-accent' not in s, 'in already'
light = '\n    --kron-on-solid: #ffffff;\n'
dark = '\n        --kron-on-solid: #ffffff;\n'
# the light block and the switch's dark block are indented 4, the system dark block 8 (each matched from its line's start)
assert s.count(light) == 2 and s.count(dark) == 1, (s.count(light), s.count(dark))
first = s.index(light)
second = s.index(light, first + 1)
s = (s[:first] + light + '    --kron-on-accent: #ffffff;\n'
     + s[first + len(light):second] + light + '    --kron-on-accent: #10161e;\n'
     + s[second + len(light):])
s = s.replace(dark, dark + '        --kron-on-accent: #10161e;\n')
open(t, 'w', encoding='utf-8', newline='').write(s)

c = root + 'stylesheets.css'
s = open(c, encoding='utf-8', newline='').read()
a = 'background: var(--kron-accent); color: var(--kron-on-solid); font-size: var(--kron-fs-label); font-weight: 600;'
assert s.count(a) == 1
s = s.replace(a, 'background: var(--kron-accent); color: var(--kron-on-accent); font-size: var(--kron-fs-label); font-weight: 600;')
open(c, 'w', encoding='utf-8', newline='').write(s)
print('ok: report-tokens.css, stylesheets.css')
