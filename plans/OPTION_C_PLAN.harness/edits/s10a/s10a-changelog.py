# S10a's changelog entries, into the [Unreleased] section of the v5 tree. usage: python s10a-changelog.py <worktree root>
import sys
P = sys.argv[1].rstrip('/') + '/CHANGELOG.md'
s = open(P, encoding='utf-8', newline='').read()


def before(anchor, text):
    global s
    assert s.count(anchor) == 1, (anchor[:60], s.count(anchor))
    s = s.replace(anchor, text + anchor)


before('### Changed (breaking)\n', '''- **`systemUiFont` on `PlantUmlCreator.GetPlantUmlImageTagsPerTestId`.** True sets a sequence diagram in the
  system's own font (`skinparam defaultFontName system-ui`). The diagrams fetcher passes it for BrowserJs and NodeJs
  rendering, whose engine measures and paints that face; the JVM (Server and Local rendering) knows no `system-ui` and
  keeps its own font.

''')
before('- **The dependency filter shows a scenario with any of the chosen dependencies by default', '''- **Diagrams take Option C's look.** A sequence diagram starts with flat boxes, participants with rounded corners and
  thin edges, thin lifelines and note edges, and messages centred over their arrows, and, where a browser engine draws
  it (BrowserJs and NodeJs rendering), the system's own font; component diagrams and their diffs drawn by those engines
  take the font too. A diagram drawn with a `PlantUmlTheme` leaves the look out and keeps the theme's.
- **A service is drawn as a box.** `DependencyPalette.GetSequenceShape(DependencyType.HttpApi)` returns `participant`,
  which was `entity`, and a caller after the first that has no category is a `participant` too: a service reads as a
  box among the participants around it, not a circle on a line.
- **Databases are deep cyan.** `DependencyPalette.DefaultColors[DependencyType.Database]` is `#0E7C86`, which was
  `#E74C3C`: red read as a failure among the failures it sat beside. It colours a database's arrows and, with
  participant colours on, its participant. A component diagram's database box is `#0B6C75` with a `#09616A` edge, a
  shade deeper, so its white name reads at 6.15:1. `DependencyColors` still overrides it.
- **A step bar's table or doc string ends at its last row.** The blank line under it is gone; the one above stays.
''')
before('- **Making a note shorter from its lower part keeps your place.**', '''- **A component diagram's cache and queue boxes carry their names at AA.** White on the cache box's `#F39C12`
  measured 2.19:1 and on the queue box's `#9B59B6` 4.67:1. The boxes are `#B35900` (4.83:1) and `#8E44AD` (5.87:1),
  each with a deeper edge; the arrows keep the palette's colours.
''')
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
