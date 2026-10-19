# S9a's mutations: each undoes one behaviour in the source, rebuilds, runs the Playwright test that should catch it,
# expects it to FAIL, and puts the source back. usage: python mutate-s9a.py   (from anywhere; takes a few minutes)
import subprocess, sys

ROOT = 'C:/Code/Kronikol-v5/'
CNS = ROOT + 'src/Kronikol/Reports/collapsible-notes-script.js'
M = 'Kronikol.Tests.EndToEnd.FilterToggleModelTests.'
E = 'Kronikol.Tests.EndToEnd.EventsAndSetupToggleTests.'
MUTATIONS = [
    ("a scenario's choice is not read for a later diagram", CNS,
     "return scenario && scenario[flag] !== undefined ? !!scenario[flag] : !!window[flag];",
     "return !!window[flag];", M + 'A_scenarios_choice_holds_for_the_diagrams_it_draws_after_it'),
    ("the report's toggle keeps the scenarios' choices", CNS,
     "            sc[flag] = undefined;\n", "", M + 'The_reports_choice_holds_over_a_scenarios_own'),
    ('the events filter takes nothing out', CNS,
     "return stripParticipantCalls(source, 'queue');", "return source;",
     E + 'The_reports_Events_toggle_takes_the_brokers_calls_out_and_puts_them_back'),
    ('the setup filter runs after the steps filter', CNS,
     "        var s = applySetupFilter(source, filterShown(container, '_setupVisible'));\n        s = applyAssertionFilter(s, filterShown(container, '_assertionsVisible'));\n        s = applyStepsFilter(s, filterShown(container, '_stepsVisible'));",
     "        var s = applyAssertionFilter(source, filterShown(container, '_assertionsVisible'));\n        s = applyStepsFilter(s, filterShown(container, '_stepsVisible'));\n        s = applySetupFilter(s, filterShown(container, '_setupVisible'));",
     E + 'The_setup_goes_with_the_step_bars_hidden_too'),
    ('the setup frame is not found', CNS,
     "if (!/^partition\\b.*\\bSetup$/.test(stmts[k].t)) continue;", "continue;",
     E + 'The_Setup_toggle_takes_a_Setup_frame_out_whole'),
]

killed = 0
for name, path, old, new, test in MUTATIONS:
    s = open(path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj',
                            '--filter', 'FullyQualifiedName~' + test], capture_output=True, text=True)
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
