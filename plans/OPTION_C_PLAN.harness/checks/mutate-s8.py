# S8's mutations: each undoes one behaviour in the source, rebuilds, runs the Playwright test that should catch it,
# expects it to FAIL, and puts the source back. usage: python mutate-s8.py   (from anywhere; takes a few minutes)
import subprocess, sys

ROOT = 'C:/Code/Kronikol-v5/'
R = ROOT + 'src/Kronikol/Reports/'
T = 'Kronikol.Tests.EndToEnd.ScenarioSettingsTests.'
MUTATIONS = [
    ('Enter on a button folds the picked scenario again', R + 'report-keyboard-navigation-function.js',
     "e.key === 'Enter' && focused && (e.target === document.body || e.target === document.documentElement)",
     "e.key === 'Enter' && focused", T + 'Enter_presses_a_button_while_the_arrow_keys_have_picked_a_scenario'),
    ('the toggle rewrites its label', R + 'collapsible-notes-script.js',
     "            b.setAttribute('aria-pressed', shown ? 'true' : 'false');\n",
     "            b.setAttribute('aria-pressed', shown ? 'true' : 'false');\n            b.textContent = b.textContent + (shown ? ' Shown' : ' Hidden');\n",
     T + 'A_toggle_keeps_its_word_and_its_width_when_switched'),
    ('the arrow keys do nothing on the tabs', R + 'toggle-script.js',
     "    if (j < 0) return;\n    e.preventDefault();\n    all[j].click();",
     "    return;\n    e.preventDefault();\n    all[j].click();",
     T + 'The_arrow_keys_move_between_the_views'),
    ("the title's gear opens nothing", R + 'report-scenario-settings-script.js',
     "            settings = section && section.querySelector(':scope > .scenario-settings');",
     "            settings = null;",
     T + 'On_a_phone_the_gear_on_the_sections_title_opens_the_settings_without_folding_the_section'),
    ('the open settings do not stick', R + 'collapsible-notes-styles.css',
     "        position: sticky; top: var(--kron-nav-h, 0px); background: var(--kron-strip);",
     "        background: var(--kron-strip);",
     T + 'The_open_settings_stay_under_the_report_bar_while_the_diagrams_are_in_view'),
    ('a label loses its bold width', R + 'collapsible-notes-styles.css',
     ".btn-label::after { content: attr(data-label);", ".btn-label::after { content: none;",
     T + 'A_payload_button_keeps_its_width_when_it_is_picked'),
]

killed = 0
for name, path, old, new, test in MUTATIONS:
    s = open(path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj',
                            '--filter', 'FullyQualifiedName=' + test], capture_output=True, text=True)
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'))
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
