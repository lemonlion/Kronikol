# S10b-3's mutations: each takes one of the stylesheet's colour rules out, rebuilds, runs the Playwright theory that
# should catch it, expects it to FAIL, and puts the stylesheet back. Test runs start below normal priority.
# usage: python mutate-s10b-c.py   (takes a few minutes)
import subprocess, sys

ROOT = 'C:/Code/Kronikol-v5/'
CSS = ROOT + 'src/Kronikol/Reports/inline-svg-styles.css'
T = 'Kronikol.Tests.EndToEnd.DiagramThemeColoursTests.'
GROUND = T + 'The_diagrams_ground_ink_and_lines_take_the_theme'
STRIPS = T + 'The_assertion_strips_and_a_bar_that_is_not_a_step_take_the_theme'
CALLS = T + 'A_call_to_a_dependency_takes_the_themes_colour_for_it'
MUTATIONS = [
    ("the ground stays the engine's", '.plantuml-browser svg { background: var(--dg-bg) !important; }\n', GROUND),
    ("the ink stays black", '.plantuml-browser svg text[fill="#000000" i] { fill: var(--dg-ink); }\n', GROUND),
    ("the lines stay the engine's", '.plantuml-browser svg :is([style*="stroke:#181818" i], [stroke="#181818" i]) { stroke: var(--dg-line) !important; }\n', GROUND),
    ("the arrowheads stay the engine's", '.plantuml-browser svg polygon[fill="#181818" i] { fill: var(--dg-line); }\n', GROUND),
    ("a passed assertion keeps its green", '.plantuml-browser svg :is(path, polygon)[fill="#d4edda" i] { fill: var(--dg-pass); stroke: var(--dg-pass-edge) !important; }\n', STRIPS),
    ("a failed assertion keeps its red", '.plantuml-browser svg :is(path, polygon)[fill="#f8d7da" i] { fill: var(--dg-fail); stroke: var(--dg-fail-edge) !important; }\n', STRIPS),
    ("a bar stays black", '.plantuml-browser svg polygon[fill="#000000" i] { fill: var(--dg-bar); }\n', STRIPS),
    ("an HTTP call's head keeps the palette's blue", '.plantuml-browser svg [fill="#438dd5" i] { fill: var(--dg-http); }\n', CALLS),
    ("an HTTP call keeps the palette's blue", '.plantuml-browser svg :is([style*="stroke:#438dd5" i], [stroke="#438dd5" i]) { stroke: var(--dg-http) !important; }\n', CALLS),
    ("a database call's head keeps the palette's cyan", '.plantuml-browser svg [fill="#0e7c86" i] { fill: var(--dg-db); }\n', CALLS),
    ("a database call keeps the palette's cyan", '.plantuml-browser svg :is([style*="stroke:#0e7c86" i], [stroke="#0e7c86" i]) { stroke: var(--dg-db) !important; }\n', CALLS),
    ("a message's head keeps the palette's violet", '.plantuml-browser svg [fill="#9b59b6" i] { fill: var(--dg-msg); }\n', CALLS),
    ("a message keeps the palette's violet", '.plantuml-browser svg :is([style*="stroke:#9b59b6" i], [stroke="#9b59b6" i]) { stroke: var(--dg-msg) !important; }\n', CALLS),
]

killed = 0
for name, old, test in MUTATIONS:
    s = open(CSS, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(CSS, 'w', encoding='utf-8', newline='').write(s.replace(old, ''))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj',
                            '--filter', 'FullyQualifiedName~' + test, '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(CSS, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
