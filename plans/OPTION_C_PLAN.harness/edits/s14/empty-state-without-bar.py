# S14: the empty state answers the filters on every page, not only through the bar. Only the bar's sync showed it, and
# the bar is drawn only on a page with tabs, so a page with nothing beside its features (a Specifications report where
# no scenario recorded a duration) never said "No scenarios match the filters." The empty state reads the readout on
# its own, on every filter change and once at start (a link's filters are read before this script starts), and its
# Clear All is wired with or without the bar. Found by the review of the wiki's 5.0.0 pages.
# usage: python -I empty-state-without-bar.py <repo root>
import sys

p = sys.argv[1] + '/src/Kronikol/Reports/report-filter-bar-script.js'
t = open(p, encoding='utf-8', newline='').read()
assert '\r\n' not in t
edits = [
    ("    var hooks = { paint: function () {}, sync: function () {} };\n",
     "    var hooks = { paint: function () {}, sync: function () {} };\n"
     "    var empty = null;\n"),
    ("        var empty = $('.filters-empty'), home = { parent: box.parentNode, next: box.nextSibling };\n",
     "        var home = { parent: box.parentNode, next: box.nextSibling };\n"),
    ("            clear.hidden = !filtered;\n"
     "            if (empty) empty.hidden = !(filtered && m && m[1] === '0');\n",
     "            clear.hidden = !filtered;\n"),
    ("        clear.addEventListener('click', function () { clear_all_filters(); });\n"
     "        if (empty) $('.filters-empty-clear', empty).addEventListener('click', function () {\n"
     "            clear_all_filters();\n"
     "            // the empty state goes with the filters it reported, and focus with it: to the search, where a reader starts again\n"
     "            var search = doc.getElementById('searchbar');\n"
     "            if (search && search.getClientRects().length) search.focus({ preventScroll: true });\n"
     "        });\n",
     "        clear.addEventListener('click', function () { clear_all_filters(); });\n"),
    ("    function start() {\n",
     "    // ---- the empty state ----\n"
     "    // the Features panel says when the filters leave no scenario, on a page with the bar and on one without it (a page\n"
     "    // with nothing beside its features has no tabs, and so no bar)\n"
     "    function emptyState() {\n"
     "        var readout = empty && $('.filter-readout', box);\n"
     "        if (!readout) return;\n"
     "        var m = /(\\d+) of (\\d+) scenarios?/.exec(readout.textContent);\n"
     "        empty.hidden = !(readout.classList.contains('is-filtered') && m && m[1] === '0');\n"
     "    }\n"
     "\n"
     "    function start() {\n"),
    ("        nav = $('.toolbar-row.report-nav');\n"
     "        if (nav) bar();\n",
     "        nav = $('.toolbar-row.report-nav');\n"
     "        empty = $('.filters-empty');\n"
     "        if (empty) $('.filters-empty-clear', empty).addEventListener('click', function () {\n"
     "            clear_all_filters();\n"
     "            // the empty state goes with the filters it reported, and focus with it: to the search, where a reader starts again\n"
     "            var search = doc.getElementById('searchbar');\n"
     "            if (search && search.getClientRects().length) search.focus({ preventScroll: true });\n"
     "        });\n"
     "        // a link's filters were read before this started\n"
     "        emptyState();\n"
     "        if (nav) bar();\n"),
    ("    // what the filters leave showing changes the bar's copy and the count\n"
     "    doc.addEventListener('kron-filter-change', function () { hooks.sync(); hooks.paint(); });\n",
     "    // what the filters leave showing changes the empty state, the bar's copy and the count\n"
     "    doc.addEventListener('kron-filter-change', function () { emptyState(); hooks.sync(); hooks.paint(); });\n"),
]
for old, new in edits:
    assert t.count(old) == 1, old[:70]
    t = t.replace(old, new)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok')
