# S11b's changelog line, after S11a's in the look entry. usage: python changelog-s11b.py <root>
import sys
p = sys.argv[1].rstrip('/') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
a = "  as do the component diagram's person box, database edge and edge labels, and a note's header line.\n"
assert s.count(a) == 1
s = s.replace(a, a + "  The flame chart draws each source in one of eight hues, as a pale bar with a stronger edge in the theme's shades,\n"
                     "  where a source had any hue, lightened with depth.\n")
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
