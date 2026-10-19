# S11c's mutations: each puts one of the popup's colours back to its literal (or its tab back to 4.x's), rebuilds, runs
# the test that should catch it, expects it to FAIL, and puts the file back. Test runs start below normal priority.
# usage: python mutate-s11c.py [part of a test name, to run only its mutations]   (takes a few minutes)
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
CSS = ROOT + 'src/Kronikol/Reports/internal-flow-popup-styles.css'
TOKENS = ROOT + 'src/Kronikol/Reports/report-tokens.css'
T = 'Kronikol.Tests.EndToEnd.'
THEME = T + 'IflowPopupThemeTests.The_popup_takes_the_theme'
TABS = T + 'IflowPopupThemeTests.The_popups_tabs_take_the_accent_and_the_quiet_ink'
LEFT = T + 'SpansLeftOutPopupTests.A_popup_that_left_spans_out_says_how_many_and_why_above_its_diagram'
PINS = T + 'IflowPopupTests.Popup_styles_are_applied_from_stylesheet'
MUTATIONS = [
    ("the card stays white", CSS, '    background: var(--kron-card, #fff);\n', '    background: #fff;\n', THEME),
    ("the heading stays #333", CSS, '    font-size: 15px;\n    color: var(--kron-ink, #333);', '    font-size: 15px;\n    color: #333;', THEME),
    ("the close mark stays #666", CSS, '    font-size: 20px;\n    color: var(--kron-muted, #666);', '    font-size: 20px;\n    color: #666;', THEME),
    ("the overlay is a literal", CSS, '    background: var(--kron-overlay, rgba(0,0,0,0.4));\n', '    background: rgba(0,0,0,0.4);\n', THEME),
    ("the dark theme keeps the light overlay", TOKENS, '\n    --kron-overlay: rgba(0, 0, 0, .6);', '', THEME),
    ("the chosen tab is 4.x blue", CSS, '    color: var(--kron-accent, #4285f4);\n    font-weight: 600;', '    color: #4285f4;\n    font-weight: 600;', TABS),
    ("the chosen tab has no underline", CSS, '    box-shadow: inset 0 -2px 0 var(--kron-accent, #4285f4);', '    box-shadow: none;', TABS),
    ("the chosen tab is filled", CSS, '.iflow-toggle-active, .iflow-toggle-active:hover {\n    background: none;', '.iflow-toggle-active, .iflow-toggle-active:hover {\n    background: #4285f4;', TABS),
    ("the other tab's ink is a literal", CSS, '    color: var(--kron-ink-quiet, #333);', '    color: #333;', TABS),
    ("the left-out note stays #666", CSS, '.iflow-left-out { font-size: 0.85em; color: var(--kron-muted, #666);', '.iflow-left-out { font-size: 0.85em; color: #666;', LEFT),
    ("a tab keeps 4.x's corners", CSS, '    border-radius: 5px 5px 0 0;', '    border-radius: 4px;', PINS),
]
ONLY = sys.argv[1] if len(sys.argv) > 1 else ''
MUTATIONS = [m for m in MUTATIONS if ONLY in m[4]]

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
