# S12 phone part 2's changelog: a Fixed item at the end of [Unreleased]'s Fixed list (5.0.0, no bump).
# usage: python changelog-s12p2.py <root>
import sys

p = sys.argv[1].rstrip('/\\') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
item = '''- **On a phone the filter rows fold under a real button, and the fold holds at every width.** The Filters toggle is a
  button the keyboard reaches and a screen reader hears as open or folded, on a line of its own that also says what
  the filters leave showing. The search help opens while the rows are folded (it could not). Rows opened or folded
  stay so when the window crosses the phone's width, either way (rows opened on a phone stayed hidden on a wider
  window, and a page opened wide then narrowed showed its rows under a toggle that did nothing), and they start folded
  from the first paint. The button and the sticky search line take the theme's colours (both were grey in every
  theme).
'''
assert 'On a phone the filter rows fold under a real button' not in s, 'in already'
assert '\r' not in s, 'CHANGELOG.md has CR line endings here: the item is written with LF'
assert all(len(line) <= 120 for line in item.splitlines()), 'a line is wider than 120'
unreleased = s.index('## [Unreleased]\n')
release = s.index('\n## [', unreleased + 1)
assert s.index('\n### Fixed\n', unreleased) < release, 'no Fixed list in [Unreleased]'
# the Fixed list ends at the blank line before the next release's heading
end = release
while s[end - 1] == '\n':
    end -= 1
open(p, 'w', encoding='utf-8', newline='').write(s[:end] + '\n' + item.rstrip('\n') + s[end:])
print('ok')
