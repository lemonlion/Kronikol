# S12 phone part 2 (applied after D49, which titles the Filters toggle): on a phone the filter rows fold under a real
# button on a line of its own, with More filters' class, so one fold holds at every width; the readout rides that line;
# the button and the sticky search line take their colours from the tokens; an empty media block goes.
#   ReportGenerator.cs              the toggle is a button in div.mobile-filter-line
#   report-init-script.js           the inline-style fold goes (the stylesheet folds the rows from the first paint)
#   report-filter-bar-script.js     the button sets rows-folded, paint sets its aria-expanded, placeReadout moves the readout
#   stylesheets.css                 the fold rules at every width; the button and its line; tokens; the empty block
# usage: python scripts-s12p2.py <worktree root>
import re
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
R = 'src/Kronikol/Reports/'
files = {}


def text(path):
    if path not in files:
        raw = open(ROOT + path, 'rb').read()
        files[path] = (raw.startswith(b'\xef\xbb\xbf'), raw.decode('utf-8-sig'))
    return files[path][1]


def edit(path, old, new, count=1):
    s = text(path)
    assert s.count(old) == count, (path, s.count(old), old[:70])
    files[path] = (files[path][0], s.replace(old, new))


assert 'mobile-filter-line' not in text(R + 'ReportGenerator.cs'), 'phone part 2 is in already'

edit(R + 'ReportGenerator.cs',
     '<div class="mobile-filter-toggle" title="Show or hide the filters">Filters</div>',
     '<div class="mobile-filter-line"><button type="button" class="mobile-filter-toggle" aria-expanded="false" aria-controls="filter-rows" title="Show or hide the filters">Filters</button></div>')

INIT = R + 'report-init-script.js'
edit(INIT, '''    // #2 Collapsible filter section on mobile
    var isMobile = window.matchMedia('(max-width: 768px)').matches;
    if (isMobile) {
        var filtersDiv = document.querySelector('.filters');
        var filterToggle = document.querySelector('.mobile-filter-toggle');
        if (filtersDiv && filterToggle) {
            filtersDiv.style.display = 'none';
            filterToggle.addEventListener('click', function() {
                var isOpen = filterToggle.classList.toggle('filter-open');
                filtersDiv.style.display = isOpen ? 'flex' : 'none';
            });
        }
    }

''', '')
edit(INIT, '''    // layout. Run first, it was aimed before the mobile blocks above folded the filter panel away, and
    // ended the panel's height too far down, with the scenario off the top of a phone's screen.
''', '''    // layout (a phone's filter rows are folded by the stylesheet, from the first paint).
''')

BAR = R + 'report-filter-bar-script.js'
edit(BAR, '''// decided on their measured widths (D18). On a phone the bar scrolls away and the box keeps its own Filters toggle.
''', '''// decided on their measured widths (D18). On a phone the bar scrolls away and the box keeps its own Filters button, which
// folds the rows with More filters' class, so they stay as they were across the phone breakpoint, and whose line the
// readout rides (5.0.0, S12).
''')
edit(BAR, "        var more = $('.more-filters', box), count = more && $('.more-filters-count', more);\n",
     "        var more = $('.more-filters', box), count = more && $('.more-filters-count', more), phoneToggle = $('.mobile-filter-toggle', box);\n")
edit(BAR, "            more.setAttribute('aria-expanded', String(open));\n",
     "            more.setAttribute('aria-expanded', String(open));\n"
     "            if (phoneToggle) phoneToggle.setAttribute('aria-expanded', String(open));\n")
edit(BAR, '''    function start() {
        box = $('.filtering-box');
        if (!box) return;
''', '''    // ---- the phone's Filters line ----
    // On a phone the rows fold under the Filters button, whose line is otherwise empty: the readout rides it there, so it
    // costs no height (the preview's c-dom.r5.js), and goes back before Clear All on a wider window. The element itself
    // moves, so the filters, which write it, and the bar, which copies it, keep it.
    function placeReadout() {
        var readout = $('.filter-readout', box), line = $('.mobile-filter-line', box);
        var clear = $('.filtering-box-export .export-btn[onclick="clear_all_filters()"]', box);
        if (!readout || !line || !clear) return;
        if (phone.matches) { if (readout.parentNode !== line) line.appendChild(readout); }
        else if (readout.nextSibling !== clear) clear.parentNode.insertBefore(readout, clear);
    }

    function start() {
        box = $('.filtering-box');
        if (!box) return;
        placeReadout();
        if (phone.addEventListener) phone.addEventListener('change', placeReadout);
        else if (phone.addListener) phone.addListener(placeReadout);
''')
edit(BAR, '''    // More filters answers from the moment the page has it, before the rest of this starts
    doc.addEventListener('click', function (e) {
        var more = e.target && e.target.closest ? e.target.closest('.filtering-box .more-filters') : null;
''', '''    // More filters, and on a phone the Filters button, answer from the moment the page has them, before the rest of this
    // starts: both set the one fold class
    doc.addEventListener('click', function (e) {
        var more = e.target && e.target.closest ? e.target.closest('.filtering-box .more-filters, .filtering-box .mobile-filter-toggle') : null;
''')

CSS = R + 'stylesheets.css'
edit(CSS, '''        @media (min-width: 768.02px) {
            .filtering-box.rows-folded:not(.help-open) > .filters { display: none; }
            .filtering-box.rows-folded.help-open > .filters > :not(.search-help-panel) { display: none; }
        }
''', '''        /* the rows fold with one class at every width: More filters sets it on a desktop, the Filters button on a phone */
        .filtering-box.rows-folded:not(.help-open) > .filters { display: none; }
        .filtering-box.rows-folded.help-open > .filters > :not(.search-help-panel) { display: none; }
''')
edit(CSS, '''        /* #2 Collapsible filter section — hidden on desktop */
        .mobile-filter-toggle {
            display: none;
            align-items: center;
            gap: 0.5em;
            cursor: pointer;
            padding: 0.4em 0;
            font-size: 0.9em;
            color: rgb(100, 100, 100);
            user-select: none;
        }
        .mobile-filter-toggle::before {
            content: '▸';
            font-size: 0.8em;
            transition: transform 0.15s ease;
        }
        .mobile-filter-toggle.filter-open::before {
            transform: rotate(90deg);
        }
''', '''        /* #2 The phone's Filters button, on a line of its own that the readout rides there (no line on a desktop) */
        .mobile-filter-line { display: none; }
        .mobile-filter-toggle {
            display: inline-flex;
            flex: none;
            align-items: center;
            gap: 0.5em;
            padding: 0.4em 0;
            border: 0;
            background: none;
            color: var(--kron-label);
            font: inherit;
            font-size: var(--kron-fs-label);
            font-weight: 600;
            letter-spacing: var(--kron-label-track);
            text-transform: uppercase;
            cursor: pointer;
            user-select: none;
        }
        .mobile-filter-toggle:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: 1px; }
        .mobile-filter-toggle::before {
            content: '▸';
            font-size: 0.8em;
            transition: transform 0.15s ease;
        }
        .mobile-filter-toggle[aria-expanded="true"]::before {
            transform: rotate(90deg);
        }
        .mobile-filter-line > .filter-readout {
            min-width: 0; margin: 0; padding: 0; border: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
        }
''')
edit(CSS, '''                z-index: 100;
                background: rgb(224, 224, 224);
                padding: 0.4em 0;
''', '''                z-index: 100;
                background: var(--kron-card);
                padding: 0.4em 0;
''')
edit(CSS, '''            /* #2 Collapsible filter section — toggle styling */
            .mobile-filter-toggle {
                display: flex;
            }
''', '''            /* #2 the Filters button's line, with the readout at its right */
            .mobile-filter-line {
                display: flex;
                align-items: center;
                justify-content: space-between;
                gap: 12px;
            }
''')
s = text(CSS)
s, n = re.subn(r'(        \.run-link:hover \{ text-decoration: underline; \}\n)\s*@media \(min-width: 768\.02px\) \{\s*\}\n\s*(        \.filtering-box \{\n)',
              r'\1\n\2', s)
assert n == 1, ('the empty media block', n)
files[CSS] = (files[CSS][0], s)

for path, (bom, s) in files.items():
    open(ROOT + path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(s)
print('ok:', ', '.join(files))
