# S10b-1's mutations: each undoes one rule of the render shim's look, rebuilds, runs the Playwright fact that should
# catch it, expects it to FAIL, and puts the source back. Test runs start below normal priority.
# usage: python mutate-s10b-a.py   (takes a few minutes)
import subprocess, sys

ROOT = 'C:/Code/Kronikol-v5/'
BRS = ROOT + 'src/Kronikol/Reports/plantuml-browser-render-script.js'
T = 'Kronikol.Tests.EndToEnd.DiagramLookShimTests.'
MUTATIONS = [
    ("the shim draws the bars as the source asks", BRS,
     "                drawn = drawn.replace(STEP_BAR, 'rnote across $1<b>');\n", "",
     T + 'A_step_bar_is_drawn_as_a_rectangle_with_its_label_in_bold'),
    ("an older bar keeps its blank last line", BRS,
     "var drawn = STEP_BODY.test(line) ? line.replace(/(?:\\\\n)+(\\r?)$/, '$1') : line;", "var drawn = line;",
     T + 'An_older_step_bars_blank_line_under_its_table_is_not_drawn'),
    ("the label is not bold", BRS,
     "'rnote across $1<b>'", "'rnote across $1'",
     T + 'A_step_bar_is_drawn_as_a_rectangle_with_its_label_in_bold'),
]

killed = 0
for name, path, old, new, test in MUTATIONS:
    s = open(path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj',
                            '--filter', 'FullyQualifiedName~' + test, '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
