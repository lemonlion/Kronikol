# S11c's changelog lines, after S11b's in the look entry. usage: python changelog-s11c.py <root>
import sys
p = sys.argv[1].rstrip('/') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
a = "  where a source had any hue, lightened with depth.\n"
assert s.count(a) == 1
s = s.replace(a, a + "  The internal-flow popup takes the theme too, its Activity and Flame Chart views drawn as tabs, and its overlay\n"
                     "  is the token `--kron-overlay`. Its notes and failure text are styled by class (`iflow-left-out`,\n"
                     "  `iflow-diagnostics`, `iflow-more-tests`, `iflow-render-error`), where they carried inline colours.\n")
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
