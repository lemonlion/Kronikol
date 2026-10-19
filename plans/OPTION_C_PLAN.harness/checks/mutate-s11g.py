# S11g's mutations: each puts one part of the diagnostics card back to a literal or takes a rule out, rebuilds, runs
# the Playwright theory that should catch it, expects it to FAIL, and puts the stylesheet back. Runs start below normal.
# usage: python mutate-s11g.py   (takes a few minutes)
import os, subprocess

# the checkout this file is in, whichever worktree that is
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
CSS = ROOT + 'src/Kronikol/Reports/report-diagnostics-styles.css'
TEST = 'Kronikol.Tests.EndToEnd.DiagnosticsCardTests'
MUTATIONS = [
    ("the card keeps its 4.x ground", '            background: var(--kron-card, rgb(255, 251, 243));\n', '            background: rgb(255, 251, 243);\n'),
    ("the card keeps its 4.x edge", '            border: 1px solid var(--kron-hairline, rgb(224, 200, 160));\n', '            border: 1px solid rgb(224, 200, 160);\n'),
    ("a render failure is drawn as any kind", '        .report-diagnostic-kind-renderfailure,\n', ''),
    ("the first item carries a rule over it", '        .report-diagnostics-list > li:first-child { border-top: none; }\n', ''),
    ("no rule between items", '            border-top: 1px solid var(--kron-hairline-soft, rgb(240, 232, 215));\n', '            border-top: none;\n'),
    ("each item carries a rule under it", '            border-top: 1px solid var(--kron-hairline-soft, rgb(240, 232, 215));\n',
     '            border-top: 1px solid var(--kron-hairline-soft, rgb(240, 232, 215));\n            border-bottom: 1px solid currentColor;\n'),
    ("the heading is a disclosure in its tab", '        .report-panel > .report-diagnostics > summary { list-style: none; cursor: default; }\n', ''),
    ("the heading is no disclosure on the labs page either", '        .report-panel > .report-diagnostics > summary { list-style: none; cursor: default; }\n',
     '        .report-diagnostics > summary { list-style: none; cursor: default; }\n'),
]

killed = 0
for name, old, new in MUTATIONS:
    s = open(CSS, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(CSS, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj',
                            '--filter', 'FullyQualifiedName~' + TEST, '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(CSS, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
