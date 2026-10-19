# S10b-2's mutations: each undoes one behaviour of the role pass or its stylesheet, rebuilds, runs the Playwright fact
# that should catch it, expects it to FAIL, and puts the source back. Test runs start below normal priority.
# usage: python mutate-s10b-b.py   (takes a few minutes)
import subprocess, sys

ROOT = 'C:/Code/Kronikol-v5/'
BRS = ROOT + 'src/Kronikol/Reports/plantuml-browser-render-script.js'
CSS = ROOT + 'src/Kronikol/Reports/inline-svg-styles.css'
T = 'Kronikol.Tests.EndToEnd.DiagramRolesTests.'
MUTATIONS = [
    ("no container is marked as a sequence diagram", BRS,
     "                target.el.setAttribute('data-k-sequence', '1');\n", "",
     T + 'Lifelines_and_participants_take_their_roles_and_colours'),
    ("the lifelines keep no role", BRS,
     "        lifelines.forEach(function(e) { addClass(e, 'k-lifeline'); counts.lifelines++; });\n", "",
     T + 'Lifelines_and_participants_take_their_roles_and_colours'),
    ("a note's kind is read from one stereotype", BRS,
     "/^note((?:<<[A-Za-z]+>>)*)\\s+(?:left|right|over)\\b/", "/^note((?:<<[A-Za-z]+>>)?)\\s+(?:left|right|over)\\b/",
     T + 'A_widened_event_note_keeps_its_event_colour'),
    ("the strips keep the engine's width", BRS,
     "                if (x0 - L < 0.5 && R - x1 < 0.5) continue;", "                continue;",
     T + 'A_strip_runs_the_drawings_full_width_with_its_label_centred'),
    ("a step bar's table has no ground of its own", BRS,
     "        stepTables.forEach(function(tb) {", "        [].forEach(function(tb) {",
     T + 'A_step_bars_table_sits_on_its_own_ground_with_a_shaded_header'),
    ("a note keeps its square corners", BRS,
     "            n.body.setAttribute('d', ['M', f(x + r), f(y),", "            n.body.setAttribute('data-d', ['M', f(x + r), f(y),",
     T + 'A_note_is_drawn_as_a_card_and_an_event_note_as_an_event_card'),
    ("only a container's first picture is tagged", BRS,
     "        var host = svg.closest('[data-k-sequence]');\n        if (!host) return true;",
     "        var host = svg.closest('[data-k-sequence]');\n        if (!host || host._taggedOnce) return true;\n        host._taggedOnce = true;",
     T + 'A_diagram_drawn_again_takes_its_roles_again'),
    ("the band takes no colour", CSS,
     ".plantuml-browser svg .k-step.k-step { fill: var(--dg-step) !important; stroke: var(--dg-step-edge) !important; }\n", "",
     T + 'A_step_bar_is_a_light_band_with_dark_ink'),
    ("the fold shows", CSS,
     ".plantuml-browser svg .k-fold.k-fold { visibility: hidden; }\n", "",
     T + 'A_note_is_drawn_as_a_card_and_an_event_note_as_an_event_card'),
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
