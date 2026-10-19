p = 'C:/Code/Kronikol-v5/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
start = s.index('## [Unreleased]')
end = s.index('\n## [', start + 10)
section = s[start:end]

ADDED = '''- **The report bar carries the filters.** Right of the tabs it shows what the filters leave showing (shortened, then
  left out, where the bar is narrow) and Clear All while anything is filtered; once the bar floats over the list, a
  Filters button brings the filter box into the bar, where it pins with the bar and covers nothing. Esc, the box's
  close button, a phone's width or scrolling back up puts it back, and opening and closing keep what was being read
  just below the bar. On a phone the bar scrolls away as before and the box keeps its own Filters toggle.
- **The Features tab says when no scenario is left,** with a Clear All that puts focus back in the search.
- **The report bar shows when it is pinned:** its corners square and it casts a shadow while the list scrolls under it.
'''

CHANGED = '''- **On a desktop the filter rows fold under More filters.** A More filters button ends the search line, and the rows
  under it (Status, Duration, Happy paths, Tags, Dependencies, Categories) stay folded until it is pressed; while they
  are folded it counts the filters on in them. The readout and Clear All stay in view either way, and the search help
  opens on its own. The box carries `rows-folded` while they are folded, so a script or test that uses a row's control
  on a desktop opens them first. On a phone (768 px wide or less) the box's own Filters toggle folds them, as before.
  The search field sits in a `span.search-field`, which holds the deep search's chip at its right end.
- **Duration and Happy paths join the Status line only where they fit whole,** decided on their measured widths (a
  reader's text spacing and late fonts included), and otherwise each takes a line of its own.
'''

def append_to(section, heading, text):
    i = section.index(heading)
    j = section.find('\n### ', i + len(heading))
    j = len(section) if j < 0 else j
    return section[:j].rstrip('\n') + '\n' + text + section[j:]

section = append_to(section, '### Added', ADDED)
section = append_to(section, '### Changed (breaking)', CHANGED)
s = s[:start] + section + s[end:]
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok', '\u2014' in ADDED + CHANGED)
