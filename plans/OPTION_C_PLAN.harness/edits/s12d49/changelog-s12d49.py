# S12 D49's changelog: an Added item at the end of [Unreleased]'s Added list, the Tags item's chip tooltip, and a Fixed
# item at the end of [Unreleased]'s Fixed list (5.0.0, no bump). usage: python changelog-s12d49.py <root>
import sys

p = sys.argv[1].rstrip('/\\') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
added = '''- **Every control says what it does.** Each tab of the report bar, each payload control, each filter, each heading of
  a scenario's sections, each link and the page's own buttons has a tooltip saying what it does or what it holds. A
  payload control's tooltip names the diagrams it acts on: every sequence diagram in the report on the report bar,
  the scenario's own on a scenario's bar. Clear All's names the search and every filter row the box has. A chip's gives
  the number of scenarios it picks out and then what a click does. A composite step's says how many steps it holds,
  given when it is first pointed at or focused. In their tabs, the History and Diagnostics headings say what the
  section is, not that a click opens it.
'''
fixed = '''- **Three controls have names.** The back-to-top button (an arrow), the payload line count dropdown and the
  internal-flow popup's close (a cross) had no name a screen reader could read; they are "Back to top", "Payload
  length" and "Close".
'''
assert 'Every control says what it does.' not in s, 'in already'
assert '\r' not in s, 'CHANGELOG.md has CR line endings here: the items are written with LF'
for item in (added, fixed):
    assert all(len(line) <= 120 for line in item.splitlines()), 'a line is wider than 120'

unreleased = s.index('## [Unreleased]\n')
release = s.index('\n## [', unreleased + 1)
chip = '''(its tooltip says how many scenarios carry it),
  an Untagged chip when a scenario has none, and an ANY / ALL switch, ANY by default
'''
assert s.count(chip) == 1 and unreleased < s.index(chip) < release, 'the Tags item'
s = s.replace(chip, '''(its tooltip says how many scenarios carry it,
  and what a click does), an Untagged chip when a scenario has none, and an ANY / ALL switch, ANY by default
''')

release = s.index('\n## [', unreleased + 1)
end_added = s.index('\n\n### Changed (breaking)\n', s.index('\n### Added\n', unreleased))
assert end_added < release, "the Added list's end is not in [Unreleased]"
s = s[:end_added + 1] + added.rstrip('\n') + s[end_added:]

release = s.index('\n## [', unreleased + 1)
fixed_at = s.index('\n### Fixed\n', unreleased)
assert fixed_at < release, 'no Fixed list in [Unreleased]'
# the Fixed list ends at the blank line before the next release's heading
end_fixed = release
while s[end_fixed - 1] == '\n':
    end_fixed -= 1
s = s[:end_fixed] + '\n' + fixed.rstrip('\n') + s[end_fixed:]
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
