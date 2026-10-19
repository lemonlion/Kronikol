# S12a's mutations (round 17 item 18, steps lined up with their headings): each moves one rule the alignment depends on,
# rebuilds, runs StepAlignmentTests' Chromium fact, expects it to FAIL, and puts the file back. The facts are regression
# facts (S1 ported the rules), so these mutations are their red. Not mutated: the Steps heading's own padding
# (stylesheets.css .scenario-steps > summary), whose left side the shared heading rule after it sets again (an
# equivalent mutation: it survived). Test runs start below normal priority.
# usage: python mutate-s12a.py [part of a test name]   |   python mutate-s12a.py --check [root]
import os, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
SS = 'src/Kronikol/Reports/stylesheets.css'
E2E = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
T = 'Kronikol.Tests.EndToEnd.StepAlignmentTests.Headings_and_steps_share_their_columns_in_Chromium'
MUTATIONS = [
    ("a collapsible step's triangle leaves the headings' column", SS,
     ".scenario-background > .step-collapsible > summary::before { left: -11px; }",
     ".scenario-background > .step-collapsible > summary::before { left: -15px; }", E2E, T),
    ("the headings' triangles move", SS,
     "content: '\\25B6\\FE0E'; position: absolute; left: 14px; top: 50%;",
     "content: '\\25B6\\FE0E'; position: absolute; left: 10px; top: 50%;", E2E, T),
    ("a heading's text moves off the circles' column", SS,
     "            position: relative;\n            padding-left: 27px;\n            list-style: none;",
     "            position: relative;\n            padding-left: 31px;\n            list-style: none;", E2E, T),
    ("the status circle moves", SS,
     "            margin: 0 7px 0 2px;", "            margin: 0 7px 0 6px;", E2E, T),
    ("the steps move off the headings' columns", SS,
     "            margin: 0 14px 0 25px;\n            padding: 3px 0;",
     "            margin: 0 14px 0 29px;\n            padding: 3px 0;", E2E, T),
]

if len(sys.argv) > 1 and sys.argv[1] == '--check':
    base = (sys.argv[2].rstrip('/') + '/') if len(sys.argv) > 2 else ROOT
    bad = 0
    for name, path, old, new, project, test in MUTATIONS:
        n = open(base + path, encoding='utf-8', newline='').read().count(old)
        if n != 1:
            bad += 1
            print(f'NOT ONCE ({n}): {name}')
    print(f'{len(MUTATIONS) - bad} of {len(MUTATIONS)} anchors found once')
    sys.exit(1 if bad else 0)

ONLY = sys.argv[1] if len(sys.argv) > 1 else ''
MUTATIONS = [m for m in MUTATIONS if ONLY in m[5]]
killed = 0
for name, path, old, new, project, test in MUTATIONS:
    s = open(ROOT + path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(ROOT + path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + project, '--filter', 'FullyQualifiedName~' + test,
                            '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(ROOT + path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
