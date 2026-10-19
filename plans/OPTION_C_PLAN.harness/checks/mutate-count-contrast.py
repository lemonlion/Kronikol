# The count contrast fix's mutations: each puts one block's ink for text on the accent back to one that fails AA (or
# the count back on --kron-on-solid), rebuilds, runs StylesheetRulesTests' count theory, expects it to FAIL, and puts
# the file back. usage: python mutate-count-contrast.py [part of a test name]   |   python mutate-count-contrast.py --check [root]
import os, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
R = 'src/Kronikol/Reports/'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
T = 'Kronikol.Tests.Reports.StylesheetRulesTests.The_more_filters_count_reads_at_AA_in_every_theme'
MUTATIONS = [
    ("the light block's ink the dark accent's blue (2.6:1)", R + 'report-tokens.css',
     "\n    --kron-on-accent: #ffffff;\n", "\n    --kron-on-accent: #7ea6f2;\n", CORE, T),
    ("the switch's dark block white again", R + 'report-tokens.css',
     "\n    --kron-on-accent: #10161e;\n", "\n    --kron-on-accent: #ffffff;\n", CORE, T),
    ("the system's dark block white again", R + 'report-tokens.css',
     "\n        --kron-on-accent: #10161e;\n", "\n        --kron-on-accent: #ffffff;\n", CORE, T),
    ("the count back on --kron-on-solid", R + 'stylesheets.css',
     "color: var(--kron-on-accent);", "color: var(--kron-on-solid);", CORE, T),
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
