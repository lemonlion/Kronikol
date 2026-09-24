# The byte check the plan's section 3.3 asks the executing session to run once: a report without a
# custom sheet must differ from the previous release's only by the stylesheet rules the release changes,
# never by the order change (S3) or the composed popup sheet (S5).
#
# Usage: python s3bytes.py <before-dir> <after-dir> <old-base.css> <new-base.css> <old-popup.css> <new-popup.css>
#                           [<old-theme.css> <new-theme.css>]   (violet.cs writes each build's theme text)
#
# For every page in <after-dir>: drop the run's clock lines from both pages, substitute the new base
# sheet and the new internal-flow sheet for the old ones in the BEFORE page, and compare bytes. A page
# that still differs (one with a custom sheet, the violet Specifications shapes) is checked for a move:
# everything outside its first <style> block identical, and inside it the characters that left one place
# the same characters that arrived at another; given the two theme texts, exactly: the page with the new
# theme cut out equals the previous release's with the old theme cut out.
import difflib, os, sys

before_dir, after_dir, old_base, new_base, old_popup, new_popup = sys.argv[1:7]
read = lambda p: open(p, encoding='utf-8', newline='').read()
old_base, new_base, old_popup, new_popup = map(read, (old_base, new_base, old_popup, new_popup))
themes = tuple(map(read, sys.argv[7:9])) if len(sys.argv) > 8 else None
CLOCK = ('Start Date:', 'Start Time:', 'End Time:')

def unclocked(html):
    return ''.join(l for l in html.splitlines(True) if not any(c in l for c in CLOCK))

for name in sorted(f for f in os.listdir(after_dir) if f.endswith('.html')):
    before = unclocked(read(os.path.join(before_dir, name)))
    after = unclocked(read(os.path.join(after_dir, name)))
    assert before.count(old_base) == 1 and after.count(new_base) == 1, name + ': base sheet not found verbatim'
    rewritten = before.replace(old_base, new_base)
    if old_popup in rewritten:
        rewritten = rewritten.replace(old_popup, new_popup)
    size = f'{len(before.encode())} -> {len(after.encode())} bytes'
    if rewritten == after:
        print(f'{name}: {size}; identical to the previous release once the two sheets are swapped')
        continue
    if themes and rewritten.count(themes[0]) == 1 and after.count('\n' + themes[1]) == 1:
        same = rewritten.replace(themes[0], '') == after.replace('\n' + themes[1], '')
        print(f'{name}: {size}; with the theme cut out of both, ' + ('identical: the theme moved and nothing else did' if same else 'NOT identical'))
        continue
    b0 = rewritten.index(new_base) + len(new_base)
    a0 = after.index(new_base) + len(new_base)
    b1, a1 = rewritten.index('</style>', b0), after.index('</style>', a0)
    outside = rewritten[:b0] == after[:a0] and rewritten[b1:] == after[a1:]
    ops = [o for o in difflib.SequenceMatcher(None, rewritten[b0:b1], after[a0:a1], autojunk=False).get_opcodes() if o[0] != 'equal']
    left = ''.join(rewritten[b0 + i1:b0 + i2] for tag, i1, i2, j1, j2 in ops)
    arrived = ''.join(after[a0 + j1:a0 + j2] for tag, i1, i2, j1, j2 in ops)
    print(f'{name}: {size}; outside the <style> block identical: {outside}; inside it {len(left)} characters '
          f'left one place and {len(arrived)} arrived at another: '
          + ('the same characters, a move' if sorted(left) == sorted(arrived) else 'NOT the same characters'))
