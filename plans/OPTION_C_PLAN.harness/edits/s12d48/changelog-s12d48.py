# S12 D48's changelog item: an Added item at the end of [Unreleased]'s Added list (5.0.0, no bump).
# usage: python changelog-s12d48.py <root>
import sys

p = sys.argv[1].rstrip('/\\') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
item = '''- **Focus the keyboard moves is never left under the report bar.** Tab or Shift+Tab to a control the pinned bar covers,
  or a scenario's settings stuck under it, brings the control 12 px below them, and to one cut by the top or the foot of
  the window, 12 px inside it. For a second after, a control the page moves away, as a diagram drawn above it does, is
  brought back, until the reader scrolls, points or presses a key. Focus a click or a script gives stays where it lands,
  as does a control in the bar, in those settings or in the internal-flow popup, and a scenario taller than the window.
  On a phone, where the bar scrolls away, nothing moves.
'''
assert 'Focus the keyboard moves is never left under the report bar.' not in s, 'in already'
assert '\r' not in s, 'CHANGELOG.md has CR line endings here: the item is written with LF'
unreleased = s.index('## [Unreleased]\n')
added = s.index('\n### Added\n', unreleased)
end = s.index('\n\n### Changed (breaking)\n', added)
assert s.find('\n## [', unreleased + 1) > end, "the Added list's end is not in [Unreleased]"
assert all(len(line) <= 120 for line in item.splitlines()), 'a line is wider than 120'
open(p, 'w', encoding='utf-8', newline='').write(s[:end + 1] + item.rstrip('\n') + s[end:])
print('ok')
