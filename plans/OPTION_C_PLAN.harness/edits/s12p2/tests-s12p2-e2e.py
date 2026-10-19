# S12 phone part 2's Playwright tests:
#   MobileFilterLineTests.cs (new, copied from beside this script): the readout on the Filters line, the keyboard, the
#     search help while folded, one fold across the phone breakpoint both ways, the dark search line
#   MobileResponsiveTests.cs: the fold read from the computed display (the rows were folded by an inline style, and now
#     by a class: the old pins read the inline style)
#   ViewportSweepTests.cs: the 1160 px constant's comment named a band block no sheet has had since S2
# usage: python tests-s12p2-e2e.py <worktree root>
import os
import shutil
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
HERE = os.path.dirname(os.path.abspath(__file__))
E = 'tests/Kronikol.Tests.EndToEnd/'
NEW = E + 'MobileFilterLineTests.cs'
assert not os.path.exists(ROOT + NEW), 'phone part 2 tests are in already'


def edit(path, pairs):
    raw = open(ROOT + path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    s = raw.decode('utf-8-sig')
    for old, new, count in pairs:
        assert s.count(old) == count, (path, s.count(old), old[:70])
        s = s.replace(old, new)
    open(ROOT + path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(s)


edit(E + 'MobileResponsiveTests.cs', [
    ("var f = document.querySelector('.filters'); return f && f.style.display !== 'none'; }",
     "var f = document.querySelector('.filters'); return f && getComputedStyle(f).display !== 'none'; }", 2),
    ("var f = document.querySelector('.filters'); return f && f.style.display === 'none'; }",
     "var f = document.querySelector('.filters'); return f && getComputedStyle(f).display === 'none'; }", 1),
])
edit(E + 'ViewportSweepTests.cs', [
    ('''    /// <summary>The breakpoint below which the filtering box takes a row of its own; stylesheets.css
    /// states it once, in the band block.</summary>
''', '''    /// <summary>The 4.x breakpoint below which the filtering box took a row of its own. No sheet has had it
    /// since 5.0.0's header (plans/OPTION_C_PLAN.md S2), but the 4.x header row broke at the width after it,
    /// so the sweep keeps that width.</summary>
''', 1),
    ('''    /// <summary>20 px steps (the narrowest band ever measured is 770 to 780 px wide) plus the first width
    /// of the band and the first of the row layout, where the filtering box is narrowest.</summary>
''', '''    /// <summary>20 px steps (the narrowest band ever measured is 770 to 780 px wide) plus 769, the first
    /// width past the phone breakpoint, and the width after the 4.x breakpoint.</summary>
''', 1),
])
shutil.copyfile(os.path.join(HERE, 'MobileFilterLineTests.cs'), ROOT + NEW)
print('ok:', NEW + ', MobileResponsiveTests.cs, ViewportSweepTests.cs')
