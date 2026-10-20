# S13b's mutations: each puts one colour back as the value it was (or undoes one part of the change: the menu's ink, a
# dead rule, a value meant for both themes), rebuilds, runs the test that should catch it, expects it to FAIL, and puts
# the file back. Test runs start below normal priority. Not mutated: the deleted script (a mutation cannot bring a file
# back; The_rules_and_the_script_for_markup_no_page_writes_are_gone reads the assembly's resources for it).
# usage: python mutate-s13b.py [part of a test name, to run only its mutations]   (takes a while: each run rebuilds)
#        python mutate-s13b.py --check [root]   checks that every mutation's text is in its file exactly once, under root
#                                               (default: this checkout), and runs nothing
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is (plans/OPTION_C_PLAN.harness/checks/)
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
SHEET = 'src/Kronikol/Reports/stylesheets.css'
MENU = 'src/Kronikol/Reports/context-menu-styles.css'
SVG = 'src/Kronikol/Reports/inline-svg-styles.css'
HISTORY_CSS = 'src/Kronikol/Reports/history-styles.css'
HISTORY_HTML = 'src/Kronikol/History/HistoryHtml.cs'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
E2E = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
C = 'Kronikol.Tests.Reports.ReportSheetColoursTests.'
H = 'Kronikol.Tests.History.HistoryHtmlTests.'
EVERY = C + 'Every_colour_a_report_sheet_paints_follows_the_theme'
D = 'Kronikol.Tests.EndToEnd.DarkThemeColoursTests.'
SPARK = 'Kronikol.Tests.EndToEnd.HistorySparklineTests.The_sparkline_paints_one_colour_stop_per_run_as_a_single_element'

MUTATIONS = [
    # core
    ("the disabled search back on its grey", SHEET,
     "            background: var(--kron-strip);\n            cursor: not-allowed;\n",
     "            background: rgb(245, 245, 245);\n            cursor: not-allowed;\n", CORE, EVERY),
    ("the duration field's edge back on its grey", SHEET,
     "            border: 1px solid var(--kron-control-border);\n            border-radius: 0.4em;\n",
     "            border: 1px solid rgb(180, 180, 180);\n            border-radius: 0.4em;\n", CORE, EVERY),
    ("the duration unit back on its grey", SHEET,
     "            font-size: 0.85em;\n            color: var(--kron-muted);\n        }\n",
     "            font-size: 0.85em;\n            color: rgb(100, 100, 100);\n        }\n", CORE, EVERY),
    ("a missing attachment back on its red", SHEET,
     "            font-style: italic;\n            color: var(--kron-fail);\n",
     "            font-style: italic;\n            color: #a33;\n", CORE, EVERY),
    ("the flatten toggle's edge back on its grey", SHEET,
     "            border: 1px solid var(--kron-control-border);\n            border-radius: 3px;\n            cursor: pointer;\n            font-weight: bold;\n",
     "            border: 1px solid #bbb;\n            border-radius: 3px;\n            cursor: pointer;\n            font-weight: bold;\n", CORE, EVERY),
    ("the flatten toggle's hover back on its greys", SHEET,
     ".flatten-toggle:hover { background: var(--kron-hover); color: var(--kron-ink); }",
     ".flatten-toggle:hover { background: #ddd; color: #333; }", CORE, EVERY),
    ("no interactions back on its grey", SHEET,
     "        .no-interactions {\n            color: var(--kron-faint);\n",
     "        .no-interactions {\n            color: #8a8f98;\n", CORE, EVERY),
    ("an examples row back", SHEET,
     "        .no-interactions {\n",
     "        .examples-row-passed { background: var(--kron-pass-soft); }\n        .no-interactions {\n", CORE,
     C + 'The_rules_and_the_script_for_markup_no_page_writes_are_gone'),
    ("the example rule under the body back", SHEET,
     "        \n        .h2 {\n",
     "        body > details > details > details.example { padding: 1em; }\n        \n        .h2 {\n", CORE,
     C + 'The_rules_and_the_script_for_markup_no_page_writes_are_gone'),
    ("the scrim no longer a value", SHEET,
     "background: rgba(0,0,0,0.85);",
     "background: var(--kron-scrim, rgba(0,0,0,0.85));", CORE,
     C + 'Each_colour_meant_for_both_themes_is_still_written_where_it_is_named'),
    ("the menu without its ink", MENU,
     "    border: 1px solid var(--kron-hairline, #ccc);\n    color: var(--kron-ink, #242b36);\n",
     "    border: 1px solid var(--kron-hairline, #ccc);\n", CORE, C + 'The_diagram_menu_sets_its_own_ink_on_its_own_ground'),
    ("the menu back on white", MENU,
     ".diagram-ctx-menu {\n    position: fixed;\n    background: var(--kron-card, #fff);\n",
     ".diagram-ctx-menu {\n    position: fixed;\n    background: #fff;\n", CORE, EVERY),
    ("the menu's hover back on its blue", MENU,
     ".diagram-ctx-menu > div:hover {\n    background: var(--kron-hover, #e8f0fe);\n",
     ".diagram-ctx-menu > div:hover {\n    background: #e8f0fe;\n", CORE, C + 'The_diagram_menu_sets_its_own_ink_on_its_own_ground'),
    ("the loading text back on its grey", SVG,
     "    color: var(--kron-faint, #767676);\n",
     "    color: #999;\n", CORE, EVERY),
    ("the selection ring back on its blue", SVG,
     "    box-shadow: 0 0 0 3px color-mix(in srgb, var(--kron-accent, #3b82f6) 50%, transparent);\n",
     "    box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.5);\n", CORE, EVERY),
    ("the zoom slider back on its blue", SVG,
     "    accent-color: var(--kron-accent, rgb(59, 130, 246));\n",
     "    accent-color: rgb(59, 130, 246);\n", CORE, EVERY),
    ("the sparkline's dead ground back", HISTORY_CSS,
     "            vertical-align: middle;\n            /* each kind of run",
     "            vertical-align: middle;\n            background: var(--kron-track);\n            /* each kind of run", CORE,
     C + 'The_sparkline_reads_each_kind_of_run_from_a_token'),
    ("a kind of run written as a colour", HISTORY_CSS,
     "            --sf: var(--kron-fail-solid);\n",
     "            --sf: #bf0000;\n", CORE, EVERY),
    ("the page writing a failure's colour again", HISTORY_HTML,
     "        HistoryFormat.Failed => \"var(--sf)\",\n",
     "        HistoryFormat.Failed => \"#bf0000\",\n", CORE, H + 'A_regression_carries_its_verdict_a_sparkline_and_a_pill_beside_its_duration'),
    ("the page writing a not-a-test colour again", HISTORY_HTML,
     "        HistoryFormat.NotATest => \"var(--sn)\",\n",
     "        HistoryFormat.NotATest => \"#dcdcdc\",\n", CORE, H + 'A_position_that_was_not_a_test_is_named_in_the_sparkline_tooltip'),
    # Playwright
    ("the menu back on white (in the browser)", MENU,
     ".diagram-ctx-menu {\n    position: fixed;\n    background: var(--kron-card, #fff);\n",
     ".diagram-ctx-menu {\n    position: fixed;\n    background: #fff;\n", E2E, D + 'The_diagram_menu_is_drawn_on_the_card_in_the_pages_ink'),
    ("a missing attachment back on its red (in the browser)", SHEET,
     "            font-style: italic;\n            color: var(--kron-fail);\n",
     "            font-style: italic;\n            color: #a33;\n", E2E, D + 'An_attachment_the_report_does_not_hold_is_named_in_the_fail_colour'),
    ("the flatten toggle's ink back on its grey", SHEET,
     "            vertical-align: middle;\n            color: var(--kron-muted);\n        }\n        .flatten-toggle:hover",
     "            vertical-align: middle;\n            color: #555;\n        }\n        .flatten-toggle:hover", E2E,
     D + 'The_flatten_toggle_takes_the_muted_ink_inside_a_controls_border'),
    ("no interactions back on its grey (in the browser)", SHEET,
     "        .no-interactions {\n            color: var(--kron-faint);\n",
     "        .no-interactions {\n            color: #8a8f98;\n", E2E, D + 'The_line_that_says_no_interactions_were_captured_takes_the_faint_ink'),
    ("a kind of run written as a colour (in the browser)", HISTORY_CSS,
     "            --sf: var(--kron-fail-solid);\n",
     "            --sf: #bf0000;\n", E2E, D + 'The_sparkline_paints_each_run_in_its_kinds_token'),
    ("the page writing a failure's colour again (in the browser)", HISTORY_HTML,
     "        HistoryFormat.Failed => \"var(--sf)\",\n",
     "        HistoryFormat.Failed => \"#bf0000\",\n", E2E, SPARK),
    ("a pass written as 4.x's green", HISTORY_CSS,
     "            --sp: var(--kron-pass-ring);\n",
     "            --sp: #228b22;\n", E2E, SPARK),
]

if len(sys.argv) > 1 and sys.argv[1] == '--check':
    base = (sys.argv[2].rstrip('/\\') + '/') if len(sys.argv) > 2 else ROOT
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
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ' [' + test.split('.')[-2] + ']' + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(ROOT + path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
