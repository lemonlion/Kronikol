# S11a's mutations: each takes one of the carried colours' rules out, rebuilds, runs the Playwright theory that should
# catch it, expects it to FAIL, and puts the stylesheet back. Test runs start below normal priority.
# usage: python mutate-s11a.py [part of a test name, to run only its mutations]   (takes a few minutes)
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
CSS = ROOT + 'src/Kronikol/Reports/inline-svg-styles.css'
T = 'Kronikol.Tests.EndToEnd.DiagramActivityAndComponentColoursTests.'
FLOW = T + 'A_whole_test_flow_takes_the_theme'
COMP = T + 'The_component_diagrams_person_database_edge_and_labels_take_the_theme'
HEAD = T + 'A_notes_header_line_takes_the_themes_soft_ink'
MUTATIONS = [
    ("an activity diagram keeps the engine's ground", '.iflow-diagram svg { background: var(--dg-bg, transparent) !important; }\n', FLOW),
    ("an activity diagram's ink stays black", '.iflow-diagram svg text[fill="#000000" i] { fill: var(--dg-ink, #000000); }\n', FLOW),
    ("an activity diagram's lines stay the engine's", '.iflow-diagram svg :is([style*="stroke:#181818" i], [stroke="#181818" i]) { stroke: var(--dg-line, #181818) !important; }\n', FLOW),
    ("an activity diagram's arrowheads stay the engine's", '.iflow-diagram svg polygon[fill="#181818" i] { fill: var(--dg-line, #181818); }\n', FLOW),
    ("an activity keeps its pale blue", '.iflow-diagram svg rect[fill="#f0f4ff" i] { fill: var(--dg-activity, #f0f4ff); }\n', FLOW),
    ("an activity keeps its grey edge", '.iflow-diagram svg :is([style*="stroke:#666666" i], [stroke="#666666" i]) { stroke: var(--dg-activity-edge, #666666) !important; }\n', FLOW),
    ("a lane keeps its grey", '.iflow-diagram svg :is([style*="stroke:#cccccc" i], [stroke="#cccccc" i]) { stroke: var(--dg-lane, #cccccc) !important; }\n', FLOW),
    ("the person box keeps its blue", '#component-diagram svg [fill="#08427b" i] { fill: var(--dg-person, #08427b); }\n', COMP),
    ("the database box keeps its edge", '#component-diagram svg :is([style*="stroke:#09616a" i], [stroke="#09616a" i]) { stroke: var(--dg-db-edge, #09616a) !important; }\n', COMP),
    ("an edge label keeps its grey", '#component-diagram svg text[fill="#666666" i] { fill: var(--dg-ink-soft, #666666); }\n', COMP),
    ("a note's header line keeps its grey", '.plantuml-browser:where(:not(.iflow-diagram)) svg text[fill="#686868" i] { fill: var(--dg-ink-soft, #686868); }\n', HEAD),
]

ONLY = sys.argv[1] if len(sys.argv) > 1 else ''
MUTATIONS = [m for m in MUTATIONS if ONLY in m[2]]
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
