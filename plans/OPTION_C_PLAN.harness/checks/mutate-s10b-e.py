# The S10b-3 follow-up's mutations: each puts back one reach the colour rules had, or a mapping without its fallback, rebuilds, runs the fact that should
# catch it, expects it to FAIL, and puts the stylesheet back. Since S11a maps an activity diagram's ink itself, the
# ink reaching it from the sequence block changes nothing, and that mutation is gone. usage: python mutate-s10b-e.py
import subprocess, sys

ROOT = 'C:/Code/Kronikol-v5/'
CSS = ROOT + 'src/Kronikol/Reports/inline-svg-styles.css'
T = 'Kronikol.Tests.EndToEnd.DiagramThemeColoursTests.'
PRE = '.plantuml-browser:where(:not(.iflow-diagram)) svg '
MUTATIONS = [
    ("the HTTP colour fills every element again",
     PRE + 'polygon[fill="#438dd5" i] { fill: var(--dg-http, #438dd5); }\n', PRE + '[fill="#438dd5" i] { fill: var(--dg-http, #438dd5); }\n',
     T + 'A_box_filled_in_a_dependency_colour_keeps_it_in_the_dark_theme'),
    ("an HTTP call's line keeps no fallback",
     PRE + ':is([style*="stroke:#438dd5" i], [stroke="#438dd5" i]) { stroke: var(--dg-http, #438dd5) !important; }\n',
     PRE + ':is([style*="stroke:#438dd5" i], [stroke="#438dd5" i]) { stroke: var(--dg-http) !important; }\n',
     'Kronikol.Tests.EndToEnd.ComponentDiagramPageTests.Every_line_the_engine_drew_is_still_painted_on_a_page_without_the_theme'),
]
killed = 0
for name, old, new, test in MUTATIONS:
    s = open(CSS, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(CSS, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj',
                            '--filter', 'FullyQualifiedName~' + test, '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        ok = r.returncode != 0 and 'Failed!' in r.stdout and 'error CS' not in r.stdout
        killed += ok
        print(('KILLED  ' if ok else 'SURVIVED') + ' ' + name, flush=True)
    finally:
        open(CSS, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
