# S12 phone part 2's mutations: each undoes one part of the change, rebuilds, runs the facts that hold it, expects them
# to FAIL, and puts the file back. A mutation counts only if its facts pass on the unmutated tree.
# usage: python mutate-s12p2.py [part of a test name]   |   python mutate-s12p2.py --check [root]
import os, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
R = 'src/Kronikol/Reports/'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
E2E = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
RULES = 'Kronikol.Tests.Reports.StylesheetRulesTests'
BAR = 'Kronikol.Tests.Reports.FilterBarTests.On_a_phone_the_rows_fold_under_a_Filters_button_on_its_own_line'
LINE = 'Kronikol.Tests.EndToEnd.MobileFilterLineTests'
OLD_FOLD = '''    // #2 Collapsible filter section on mobile
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

'''
MUTATIONS = [
    # core
    ("the rows fold only on a desktop again", R + 'stylesheets.css',
     "        .filtering-box.rows-folded:not(.help-open) > .filters { display: none; }\n",
     "        @media (min-width: 768.02px) { .filtering-box.rows-folded:not(.help-open) > .filters { display: none; } }\n", CORE, RULES),
    ("the phone's search line grey again", R + 'stylesheets.css',
     "                background: var(--kron-card);\n                padding: 0.4em 0;\n",
     "                background: rgb(224, 224, 224);\n                padding: 0.4em 0;\n", CORE, RULES),
    ("the Filters button's ink a literal grey", R + 'stylesheets.css',
     "            background: none;\n            color: var(--kron-label);\n            font: inherit;\n",
     "            background: none;\n            color: rgb(100, 100, 100);\n            font: inherit;\n", CORE, RULES),
    ("an empty media block back", R + 'stylesheets.css',
     "\n        .filtering-box {\n            min-width: 0;\n",
     "\n        @media (min-width: 768.02px) {\n        }\n\n        .filtering-box {\n            min-width: 0;\n", CORE, RULES),
    ("the Filters toggle a div again", R + 'ReportGenerator.cs',
     '<button type="button" class="mobile-filter-toggle" aria-expanded="false" aria-controls="filter-rows" title="Show or hide the filters">Filters</button>',
     '<div class="mobile-filter-toggle" title="Show or hide the filters">Filters</div>', CORE, BAR),
    # Playwright
    ("the Filters button does not fold the rows", R + 'report-filter-bar-script.js',
     "e.target.closest('.filtering-box .more-filters, .filtering-box .mobile-filter-toggle')",
     "e.target.closest('.filtering-box .more-filters')", E2E, LINE),
    ("the Filters button's aria-expanded is never painted", R + 'report-filter-bar-script.js',
     "            if (phoneToggle) phoneToggle.setAttribute('aria-expanded', String(open));\n", "", E2E, LINE),
    ("the readout never rides the Filters line", R + 'report-filter-bar-script.js',
     "        if (phone.matches) { if (readout.parentNode !== line) line.appendChild(readout); }\n",
     "        if (false) { if (readout.parentNode !== line) line.appendChild(readout); }\n", E2E, LINE),
    ("the readout is placed only at load", R + 'report-filter-bar-script.js',
     "        if (phone.addEventListener) phone.addEventListener('change', placeReadout);\n        else if (phone.addListener) phone.addListener(placeReadout);\n",
     "", E2E, LINE),
    ("the Filters line shows on a desktop too", R + 'stylesheets.css',
     "        .mobile-filter-line { display: none; }\n", "        .mobile-filter-line { display: flex; }\n", E2E, LINE),
    ("the inline-style fold back at load", R + 'report-init-script.js',
     "    // Restore filters from URL hash if present.", OLD_FOLD + "    // Restore filters from URL hash if present.", E2E, LINE),
]

if len(sys.argv) > 1 and sys.argv[1] == '--check':
    base = (sys.argv[2].rstrip('/') + '/') if len(sys.argv) > 2 else ROOT
    bad = 0
    for name, path, old, new, project, test in MUTATIONS:
        n = open(base + path, encoding='utf-8-sig', newline='').read().count(old)
        if n != 1:
            bad += 1
            print(f'NOT ONCE ({n}): {name}')
    print(f'{len(MUTATIONS) - bad} of {len(MUTATIONS)} anchors found once')
    sys.exit(1 if bad else 0)

ONLY = sys.argv[1] if len(sys.argv) > 1 else ''
MUTATIONS = [m for m in MUTATIONS if ONLY in m[5]]
killed = 0
for name, path, old, new, project, test in MUTATIONS:
    raw = open(ROOT + path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    s = raw.decode('utf-8-sig')
    assert s.count(old) == 1, (name, s.count(old))
    enc = 'utf-8-sig' if bom else 'utf-8'
    open(ROOT + path, 'w', encoding=enc, newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + project, '--filter', 'FullyQualifiedName~' + test,
                            '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(ROOT + path, 'w', encoding=enc, newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
