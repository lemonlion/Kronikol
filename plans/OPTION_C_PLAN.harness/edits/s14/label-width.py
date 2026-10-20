# S14: the filter rows' label column is 92 px as a width too, not only as a flex basis (the preview's round 6,
# c-round6.css 4-20; the inventory's 3.9 "Measured browser workarounds to keep"). Firefox takes a flex item's max-content
# contribution from its width, so the Status group was sized from its label's text while the label was laid out at
# 92 px, and its buttons ran 50 px past the group and under Duration. A label inline beside Status, or alone on its line
# in a narrow box, is as wide as its text. usage: python -I label-width.py <repo root>
import sys

p = sys.argv[1] + '/src/Kronikol/Reports/stylesheets.css'
t = open(p, encoding='utf-8', newline='').read()
assert '\r\n' not in t
edits = [
    ("        .filter-row.pair-fits .duration-filters-label, .filter-row.trio-fits .happy-filters-label { flex-basis: auto; }\n",
     "        .filter-row.pair-fits .duration-filters-label, .filter-row.trio-fits .happy-filters-label { flex-basis: auto; width: auto; }\n"),
    ("        .status-filters-label, .duration-filters-label, .happy-filters-label, .tag-filters-label, .dependency-filters-label {\n"
     "            flex: 0 0 92px; margin-right: 8px;",
     "        /* 92px as a width too, not only as a flex basis: Firefox takes a flex item's max-content contribution from its\n"
     "           width, so the Status group was sized from its label's text while the label was laid out at 92px, and its\n"
     "           buttons ran 50px past the group and under Duration (the preview's round 6) */\n"
     "        .status-filters-label, .duration-filters-label, .happy-filters-label, .tag-filters-label, .dependency-filters-label {\n"
     "            flex: 0 0 92px; width: 92px; margin-right: 8px;"),
    ("            .dependency-filters-label { flex-basis: 100%; margin: 0 8px 2px 0; }\n",
     "            .dependency-filters-label { flex-basis: 100%; width: auto; margin: 0 8px 2px 0; }\n"),
]
for old, new in edits:
    assert t.count(old) == 1, old[:70]
    t = t.replace(old, new)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok')
