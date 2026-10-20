# S12 D43's changelog line: a Fixed item at the end of [Unreleased]'s Fixed list. usage: python changelog-s12d43.py <root>
import sys
p = sys.argv[1].rstrip('/') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
a = '''  "Nothing to draw" message, and that message now names the toggles by the words they show (Assertions, Steps).
'''
b = a + '''- **A diagram opens in a new tab in Firefox too.** The diagram menu's "in new tab" items open the tab while the click
  is handled and write the picture or the text into it, titled for what it holds ("Diagram (PNG)", "Diagram (SVG)",
  "PlantUML source", "Box text"). The PNG items opened their tab only once the picture was drawn, after the click,
  which Firefox's popup blocker refuses, and every item opened a `blob:` URL, which a report opened from a file makes
  with an opaque origin. Where no tab opens, the `blob:` URL is tried as before.
'''
assert 'A diagram opens in a new tab in Firefox too.' not in s, 'in already'
assert s.count(a) == 1
open(p, 'w', encoding='utf-8', newline='').write(s.replace(a, b))
print('ok')
