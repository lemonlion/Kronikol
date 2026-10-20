# S12 D43's mutations: each undoes one part of "open in new tab", rebuilds, runs the Playwright test that should catch
# it, expects it to FAIL, and puts the file back. Test runs start below normal priority.
# usage: python mutate-s12d43.py [part of a test name, to run only its mutations]   (each run rebuilds)
#        python mutate-s12d43.py --check [root]   checks that every mutation's text is in its file exactly once, under
#                                                 root (default: this checkout), and runs nothing
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is (plans/OPTION_C_PLAN.harness/checks/)
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
CMS = 'src/Kronikol/Reports/context-menu-script.js'
E2E = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
P = 'Kronikol.Tests.EndToEnd.OpenInNewTabTests.'
MUTATIONS = [
    ("the PNG tab opens only once the picture is drawn", CMS,
     "                    var tab = openTab();\n                    svgToCanvas(svg, function(canvas) { openPngInTab(tab, canvas); });",
     "                    svgToCanvas(svg, function(canvas) { openPngInTab(openTab(), canvas); });",
     E2E, P + 'A_PNG_item_opens_its_tab_in_the_click_and_writes_the_picture_into_it'),
    ("the PNG without transparency opens its tab only once drawn", CMS,
     "                    var tab = openTab();\n                    svgToCanvasWithBg(svg, function(canvas) { openPngInTab(tab, canvas); });",
     "                    svgToCanvasWithBg(svg, function(canvas) { openPngInTab(openTab(), canvas); });",
     E2E, P + 'A_PNG_item_opens_its_tab_in_the_click_and_writes_the_picture_into_it'),
    ("the PNG is never written into its tab", CMS,
     "        if (!tab || tab.closed) {", "        if (true) {",
     E2E, P + 'A_PNG_item_opens_its_tab_in_the_click_and_writes_the_picture_into_it'),
    ("the SVG is never written into its tab", CMS,
     "        if (!tab) {\n            window.open(URL.createObjectURL(new Blob([markup], { type: 'image/svg+xml' })));",
     "        if (true) {\n            window.open(URL.createObjectURL(new Blob([markup], { type: 'image/svg+xml' })));",
     E2E, P + 'The_SVG_item_writes_the_picture_into_its_tab'),
    ("the text is written unescaped", CMS,
     "            escapeTabText(text) + '</pre>', '#ffffff');",
     "            text + '</pre>', '#ffffff');",
     E2E, P + 'The_source_item_writes_the_source_into_its_tab'),
    ("the source tab is not titled for what it holds", CMS,
     "                            openTextInTab(source, typeLabel + ' source');",
     "                            openTextInTab(source, 'Diagram source');",
     E2E, P + 'The_source_item_writes_the_source_into_its_tab'),
    ("a box text tab is titled as a source (the preview's F12)", CMS,
     "                    menu.appendChild(createMenuItem('Open box text in new tab', function() {\n                        openTextInTab(_fullNoteText, 'Box text');",
     "                    menu.appendChild(createMenuItem('Open box text in new tab', function() {\n                        openTextInTab(_fullNoteText, 'Diagram source');",
     E2E, 'Kronikol.Tests.EndToEnd.NoteCopyFidelityTests.Open_box_text_in_new_tab_yields_the_same_text_as_the_copy'),
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
