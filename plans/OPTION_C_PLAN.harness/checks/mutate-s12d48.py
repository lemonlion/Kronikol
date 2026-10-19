# S12 D48's mutations: each undoes one part of the focus script or of where the page loads it, rebuilds, runs the test that
# should catch it, expects it to FAIL, and puts the file back. Test runs start below normal priority. The timing paths
# (the Tab flag going down, each way the watch ends, every scroll checking again) are held by focus-script-check.js,
# which runs in node with no browser.
# usage: python mutate-s12d48.py [part of a test name, to run only its mutations]   (takes a while: each run rebuilds)
#        python mutate-s12d48.py --check [root]   checks that every mutation's text is in its file exactly once, under
#                                                 root (default: this checkout), and that every mutation of the script
#                                                 still parses (node --check), and runs no test
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is (plans/OPTION_C_PLAN.harness/checks/)
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
RG = 'src/Kronikol/Reports/ReportGenerator.cs'
JS = 'src/Kronikol/Reports/report-focus-script.js'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
E2E = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
C = 'Kronikol.Tests.Reports.FocusScriptTests.'
P = 'Kronikol.Tests.EndToEnd.FocusClearOfTheBarTests.'
MUTATIONS = [
    # the page: which focus is moved, where to, and what is left alone
    ("a click's focus is moved too", JS,
     '        if (!tabbing || !nav || !t || t === doc.body',
     '        if (!nav || !t || t === doc.body',
     E2E, P + 'Clicking_in_and_out_of_a_diagram_half_under_the_bar_leaves_the_page_where_it_is'),
    ("a control taller than the band that shows in it is moved", JS,
     '        if (r.height > bottom - top && r.bottom > top + 24 && r.top < bottom - 24) return;\n', '',
     E2E, P + 'A_scenario_taller_than_the_window_that_Tab_reaches_stays_where_it_is'),
    ("a control in anything fixed is moved", JS,
     "        for (var n = t; n && n.nodeType === 1; n = n.parentElement) if (getComputedStyle(n).position === 'fixed') return true;\n",
     '', E2E, P + 'Tab_into_the_internal_flow_popup_leaves_the_page_where_it_is'),
    ("nothing checks again after the first frame", JS,
     '        [150, 400, 800].forEach(function (ms) { timers.push(setTimeout(watch, ms)); });\n', '',
     E2E, P + 'A_scenario_pushed_away_in_the_second_after_Tab_is_brought_back'),
    ("no room is kept at the window's foot", JS, 'var doc = document, GAP = 12;', 'var doc = document, GAP = 0;',
     E2E, P + 'Tab_to_a_scenario_cut_by_the_foot_of_the_window_brings_it_12_px_above_the_foot'),
    ("settings stuck under the bar cover nothing", JS,
     '            if (r.height && r.top <= top + 1 && r.bottom > top) top = r.bottom;\n', '',
     E2E, P + 'Tab_to_a_link_under_a_scenarios_stuck_settings_brings_it_12_px_below_them'),
    ("the bar covers the window's top at rest too", JS,
     'var top = mark && mark.getBoundingClientRect().top < line - 0.5 ? nav.getBoundingClientRect().bottom : 0;',
     'var top = nav.getBoundingClientRect().bottom;',
     E2E, P + 'Tab_to_a_control_above_the_bar_at_rest_leaves_the_page_where_it_is'),
    ("no room is kept at the window's top while the bar rests", JS,
     'nav.getBoundingClientRect().bottom : 0;', 'nav.getBoundingClientRect().bottom : -GAP;',
     E2E, P + 'Shift_Tab_to_the_search_cut_by_the_top_of_the_window_while_the_bar_rests_brings_it_12_px_in'),
    ("the bar's own controls are moved", JS, '        if (nav.contains(t)) return true;\n', '',
     E2E, P + 'Tab_between_the_bars_controls_leaves_the_page_where_it_is'),
    ("a control in settings stuck under the bar is moved", JS,
     "        if (s && Math.abs(s.getBoundingClientRect().top - (parseFloat(getComputedStyle(s).top) || 0)) <= 1) return true;\n",
     '', E2E, P + 'Tab_onto_the_gear_of_settings_stuck_under_the_bar_leaves_them_there'),
    ("a phone's page is moved", JS,
     "function sticks(nav) { var p = getComputedStyle(nav).position; return p === 'sticky' || p === 'fixed'; }",
     'function sticks(nav) { return true; }',
     E2E, P + 'On_a_phone_Tab_leaves_the_page_where_the_browser_put_it'),
    # the report: where the script is, and when
    ("the script is not in the head", RG,
     '{{scenarioSettingsScript}}{{timelineScript}}{{componentsScript}}{{focusScript}}',
     '{{scenarioSettingsScript}}{{timelineScript}}{{componentsScript}}',
     CORE, C + 'A_page_with_the_report_bar_has_the_focus_script_in_its_head_after_the_tabs_script'),
    ("the script is in every page", RG, '        var focusScript = useTabs\n', '        var focusScript = true\n',
     CORE, C + 'A_page_without_the_report_bar_has_no_focus_script'),
    ("the script's tag holds another script", RG, 'LoadResource("report-focus-script.js")', 'LoadResource("report-tabs-script.js")',
     CORE, C + 'A_page_with_the_report_bar_has_the_focus_script_in_its_head_after_the_tabs_script'),
]

if len(sys.argv) > 1 and sys.argv[1] == '--check':
    import shutil, tempfile
    base = (sys.argv[2].rstrip('/\\') + '/') if len(sys.argv) > 2 else ROOT
    node = shutil.which('node')
    bad = 0
    for name, path, old, new, project, test in MUTATIONS:
        try:
            s = open(base + path, encoding='utf-8', newline='').read()
        except FileNotFoundError:
            s = None
        n = 'no such file' if s is None else s.count(old)
        if n != 1:
            bad += 1
            print(f'NOT ONCE ({n}): {name}')
        elif path.endswith('.js') and node:
            # a mutation that does not parse would fail every fact at once, for the wrong reason
            with tempfile.TemporaryDirectory() as d:
                mutated = os.path.join(d, 'mutated.js')
                open(mutated, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
                if subprocess.run([node, '--check', mutated], capture_output=True).returncode != 0:
                    bad += 1
                    print(f'DOES NOT PARSE: {name}')
    print(f'{len(MUTATIONS) - bad} of {len(MUTATIONS)} anchors found once'
          + ('' if node else ' (node not found: the script mutations were not parsed)')
          + ('' if bad or not node else ', and every mutation of the script parses'))
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
