# S11g's changelog lines, after S11c's in the look entry. usage: python changelog-s11g.py <root>
import sys
p = sys.argv[1].rstrip('/') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
a = "  `iflow-diagnostics`, `iflow-more-tests`, `iflow-render-error`), where they carried inline colours.\n"
assert s.count(a) == 1
s = s.replace(a, a + "  The Diagnostics tab is a card on the theme's colours, with one rule between its entries, a failure's kind in the\n"
                     "  failure colour and a degraded capture's in amber.\n")
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
