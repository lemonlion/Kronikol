# S11b's mutations: each undoes one part of the flame palette, rebuilds, runs the test that should catch it, expects it
# to FAIL, and puts the file back. Test runs start below normal priority.
# usage: python mutate-s11b.py   (takes a few minutes)
import os, subprocess

# the checkout this file is in, whichever worktree that is
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
JS = ROOT + 'src/Kronikol/Reports/flame-chart-render-script.js'
CSS = ROOT + 'src/Kronikol/Reports/internal-flow-popup-styles.css'
TOKENS = ROOT + 'src/Kronikol/Reports/report-tokens.css'
CS = ROOT + 'src/Kronikol/InternalFlow/InternalFlowRenderer.cs'
E2E = ('tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj', 'Kronikol.Tests.EndToEnd.FlameChartPaletteTests')
CORE = ('tests/Kronikol.Tests/Kronikol.Tests.csproj', '.CultureInvariantOutputTests')
MUTATIONS = [
    ("the page's flame script writes no hue", JS, "'%;--fh:' + hue", "'%'", E2E),
    ("a bar has no fill", CSS, '    background: hsl(var(--fh, 215) var(--dg-flame-fill, 58% 93%));\n', '', E2E),
    ("a bar's edge is 1px", CSS, '    border-left: 3px solid hsl(', '    border-left: 1px solid hsl(', E2E),
    ("a row is 24px", CSS, '    position: relative;\n    height: 20px;', '    position: relative;\n    height: 24px;', E2E),
    ("rows touch", CSS, '    margin: 2px 0;\n    overflow: hidden;', '    margin: 0;\n    overflow: hidden;', E2E),
    ("a bar's text is grey", CSS, '    color: var(--kron-ink, #333);\n    cursor: pointer;', '    color: #777;\n    cursor: pointer;', E2E),
    ("the dark theme keeps the light fill", TOKENS, '\n    --dg-flame-fill: 26% 21%;', '', E2E),
    ("the renderer writes no hue", CS, ';--fh:{hue}', '', CORE),
]

killed = 0
for name, path, old, new, (project, test) in MUTATIONS:
    s = open(path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + project, '--filter', 'FullyQualifiedName~' + test,
                            '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
