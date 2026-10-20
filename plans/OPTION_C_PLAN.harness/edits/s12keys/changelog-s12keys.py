# S12 keyboard links' changelog: a Fixed item at the end of [Unreleased]'s Fixed list (5.0.0, no bump).
# usage: python changelog-s12keys.py <root>
import sys

p = sys.argv[1].rstrip('/\\') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
item = '''- **The keyboard opens an internal-flow arrow's popup.** With the browser engine (BrowserJs, the default) an arrow's
  link label was text only a pointer could open. A link with data is one Tab stop now, named from its label; Enter or
  Space opens its popup, and where links show only on hover, focus shows it. A diagram the Java engine drew keeps its
  links as they were.
'''
assert "The keyboard opens an internal-flow arrow's popup." not in s, 'in already'
assert '\r' not in s, 'CHANGELOG.md has CR line endings here: the item is written with LF'
assert all(len(line) <= 120 for line in item.splitlines()), 'a line is wider than 120'
unreleased = s.index('## [Unreleased]\n')
release = s.index('\n## [', unreleased + 1)
assert s.index('\n### Fixed\n', unreleased) < release, 'no Fixed list in [Unreleased]'
end = release
while s[end - 1] == '\n':
    end -= 1
open(p, 'w', encoding='utf-8', newline='').write(s[:end] + '\n' + item.rstrip('\n') + s[end:])
print('ok')
