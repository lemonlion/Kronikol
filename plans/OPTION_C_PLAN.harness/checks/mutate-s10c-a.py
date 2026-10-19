# S10c's mutations: each undoes one behaviour of the centring and link rules or of the copies' look, rebuilds, runs the
# Playwright facts that should catch it, expects them to FAIL, and puts the source back. Test runs start below normal
# priority. usage: python mutate-s10c-a.py   (takes a few minutes)
import subprocess, sys

ROOT = 'C:/Code/Kronikol-v5/'
CSS = ROOT + 'src/Kronikol/Reports/inline-svg-styles.css'
CMS = ROOT + 'src/Kronikol/Reports/context-menu-script.js'
E = 'Kronikol.Tests.EndToEnd.'
WHOLE = E + 'DiagramCentringTests.A_diagram_narrower_than_its_box_stands_in_the_middle_of_it'
PARTS = E + 'DiagramCentringTests.A_diagram_drawn_in_fragments_stands_in_the_middle_of_its_box'
LINK = E + 'DiagramLinkHoverTests'
SVG = E + 'DiagramExportLookTests.A_diagram_copied_as_SVG_carries_its_look'
PNG = E + 'DiagramExportLookTests.A_diagram_copied_as_PNG_draws_a_step_tables_ground_in_its_colour'
MUTATIONS = [
    ("a diagram keeps to the left", CSS,
     '.plantuml-browser > svg, .plantuml-inline-svg > svg, .puml-fragment > svg { display: block; margin-inline: auto; }\n', '', WHOLE),
    ("a fragment keeps to the left", CSS,
     '.plantuml-browser > svg, .plantuml-inline-svg > svg, .puml-fragment > svg {', '.plantuml-browser > svg, .plantuml-inline-svg > svg {', PARTS),
    ("the box keeps no padding on its right", CSS,
     '.plantuml-inline-svg, .plantuml-browser { padding-right: 1em; }\n', '', WHOLE),
    ("a link keeps the engine's blue", CSS,
     '.plantuml-browser svg text[data-iflow-fill="#0000ff" i][text-decoration="underline"] { fill: var(--kron-accent, #0000ff) !important; }\n', '', LINK),
    ("a copy carries no paint", CMS,
     "                    if (v && v !== computed(from[i].getAttribute(p))) to[i].style.setProperty(p, v);\n", '', SVG),
    ("a copy carries the dark theme's look", CMS,
     "        root.setAttribute('data-theme', 'light');\n", '', SVG),
    ("the page's theme is not put back", CMS,
     "            if (theme === null) root.removeAttribute('data-theme'); else root.setAttribute('data-theme', theme);\n", '', SVG),
    ("a copy shows the fold", CMS,
     "                if (cs.visibility === 'hidden') to[i].style.setProperty('visibility', 'hidden');\n", '', SVG),
    ("Copy as SVG copies the page's picture as it is", CMS,
     'serializeToString(svg.isConnected ? lookClone(svg).svg : svg)', 'serializeToString(svg)', SVG),
    ("Copy as PNG without transparency draws the page's picture as it is", CMS,
     '        var clone = look ? look.svg : svg.cloneNode(true);\n', '        var clone = svg.cloneNode(true);\n', PNG),
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
