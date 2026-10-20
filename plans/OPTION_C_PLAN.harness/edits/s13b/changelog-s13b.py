# S13b's changelog (5.0.0, no bump): three Fixed items at the end of [Unreleased]'s Fixed list.
# usage: python changelog-s13b.py <root>
import sys

p = sys.argv[1].rstrip('/\\') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
items = '''- **A diagram's menu reads in the dark theme.** The right-click menu was white and took the page's ink, so in the dark
  theme its items were light text on white. It is drawn on the page's card in the page's ink, in both themes.
- **The last fixed colours follow the theme.** A missing attachment's name, a parameter table's flatten toggle, the
  "No interactions captured" line, the custom duration field and its unit, a diagram's loading text, its selection
  ring and its zoom slider read the report's tokens, so they read in the dark theme, and the ring and the slider take
  the violet theme's accent. The loading text was 2.85:1 on white in the light theme too, and is AA now.
- **The history sparkline takes the theme.** Its runs are drawn in the report's pass, fail, skip and track colours,
  so a run reads on the dark card; in the light theme its greens and reds are the report's own, where they were 4.x's.
'''
assert "A diagram's menu reads in the dark theme." not in s, 'in already'
assert '\r' not in s, 'CHANGELOG.md has CR line endings here: the items are written with LF'
assert all(len(line) <= 120 for line in items.splitlines()), 'a line is wider than 120'
assert '\u2014' not in items and '\u2013' not in items, 'a dash'
unreleased = s.index('## [Unreleased]\n')
release = s.index('\n## [', unreleased + 1)
fixed = s.index('\n### Fixed\n', unreleased)
assert fixed < release, 'no Fixed list in [Unreleased]'
nxt = s.find('\n### ', fixed + 1)
end = nxt if 0 <= nxt < release else release
while s[end - 1] == '\n':
    end -= 1
open(p, 'w', encoding='utf-8', newline='').write(s[:end] + '\n' + items.rstrip('\n') + s[end:])
print('ok')
