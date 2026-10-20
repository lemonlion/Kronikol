# S13's changelog (5.0.0, no bump): an Added item after "Colour and type tokens.", a Changed (breaking) item at the end
# of that list, and a Fixed item at the end of the Fixed list, all in [Unreleased].
# usage: python changelog-s13.py <root>
import sys

p = sys.argv[1].rstrip('/\\') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
added = '''- **The labs page takes the theme.** `TestRunReport.labs.html` opens light or dark as the report does, with the
  report's switch at the right of its title, and draws its parts in the report's tokens, its table of every scenario a
  card as the History section and the diagnostics are. Of the report's scripts it runs only the theme's two.
'''
changed = '''- **The violet theme is written on the tokens.** `Stylesheets.VioletThemeStyleSheet`, the default
  `HtmlSpecificationsCustomStyleSheet`, sets the report's colour tokens for the light and the dark theme, so a
  Specifications report opened dark is dark and violet. A chosen control, the passed step and the back-to-top button
  keep 4.x's solid violet, with white on it (the tick and the arrow included); an attachment's name, a label and the
  search field's edge take the theme's selected colours. The accent is a darker violet (`#6d28d9`, AA on white and on
  the violet card), and controls 4.x's rules never named, such as a report tab or More filters, take the theme's
  colours. A sheet built on the 4.x constant's rules should set the tokens instead.
'''
fixed = '''- **The search help reads on the Specifications report.** The violet theme drew its panel dark under dark text, 1.21:1
  (4.x too). The help takes the report's tokens now, in the light and the dark theme.
'''
for item in (added, changed, fixed):
    head = item[:item.index('**', 4) + 2]
    assert head not in s, ('in already', head)
    assert all(len(line) <= 120 for line in item.splitlines()), ('a line is wider than 120', head)
    assert '\u2014' not in item and '\u2013' not in item, 'a dash'
assert '\r' not in s, 'CHANGELOG.md has CR line endings here: the items are written with LF'
unreleased = s.index('## [Unreleased]\n')
release = s.index('\n## [', unreleased + 1)


def end_of_list(at, stop):
    """The end of the list that runs from `at` to the next heading before `stop`, before its blank lines."""
    nxt = s.find('\n### ', at + 1)
    end = nxt if 0 <= nxt < stop else stop
    while s[end - 1] == '\n':
        end -= 1
    return end


tokens = s.index('- **Colour and type tokens.**', unreleased)
assert tokens < release
after_tokens = s.index('\n- **', tokens) + 1
changed_at = s.index('\n### Changed (breaking)\n', unreleased)
fixed_at = s.index('\n### Fixed\n', unreleased)
assert after_tokens < changed_at < fixed_at < release, 'the lists are not in the order Added, Changed, Fixed'
changed_end = end_of_list(changed_at, release)
fixed_end = end_of_list(fixed_at, release)
s = (s[:after_tokens] + added + s[after_tokens:changed_end] + '\n' + changed.rstrip('\n')
     + s[changed_end:fixed_end] + '\n' + fixed.rstrip('\n') + s[fixed_end:])
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
