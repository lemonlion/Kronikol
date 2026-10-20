# S13's mutations: each undoes one part of the change (the labs page's theme, its sheet, the shared sheets' fallbacks,
# the violet theme on the tokens, the search help), rebuilds, runs the test that should catch it, expects it to FAIL, and
# puts the file back. Test runs start below normal priority. Two parts no browser fact can see are held by core facts
# alone: the labs page's boot script (without it the switch script sets the theme itself, a frame late) and a fallback
# left in a shared sheet (the token wins over it wherever the token sheet is).
# usage: python mutate-s13.py [part of a test name, to run only its mutations]   (takes a while: each run rebuilds)
#        python mutate-s13.py --check [root]   checks that every mutation's text is in its file exactly once, under root
#                                              (default: this checkout), and runs nothing
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is (plans/OPTION_C_PLAN.harness/checks/)
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
LABS = 'src/Kronikol/Reports/LabsReportGenerator.cs'
SHEETS = 'src/Kronikol/Constants/Stylesheets.cs'
LABS_CSS = 'src/Kronikol/Reports/labs-styles.css'
HISTORY_CSS = 'src/Kronikol/Reports/history-styles.css'
DIAGNOSTICS_CSS = 'src/Kronikol/Reports/report-diagnostics-styles.css'
BASE_CSS = 'src/Kronikol/Reports/stylesheets.css'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
E2E = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
C = 'Kronikol.Tests.Reports.ThemedPagesHtmlTests.'
R = 'Kronikol.Tests.Reports.StylesheetRulesTests.'
H = 'Kronikol.Tests.History.HistoryRingsMarkupTests.'
L = 'Kronikol.Tests.Reports.LabsReportTests.'
P = 'Kronikol.Tests.EndToEnd.ThemedPagesTests.'
T = 'Kronikol.Tests.EndToEnd.ToolbarStylingTests.'
LP = 'Kronikol.Tests.EndToEnd.LabsPageTests.'
HR = 'Kronikol.Tests.EndToEnd.HistoryRingsTests.'
DC = 'Kronikol.Tests.EndToEnd.DiagnosticsCardTests.'

# the parts, each the text it changes and what it becomes
BOOT = ('        html.Append("<script id=\\"kronikol-theme-boot\\">").Append(ReportGenerator.LoadResource("report-theme-boot-script.js")).Append("</script>\\n");\n', '')
TOKENS = ('            .Append(Stylesheets.ReportTokensStyleSheet)\n', '')
TOKENS_LATE = ("            .Append(Stylesheets.ReportTokensStyleSheet)\n            .Append(Stylesheets.HtmlReportStyleSheet).Append('\\n')\n",
               "            .Append(Stylesheets.HtmlReportStyleSheet).Append('\\n')\n            .Append(Stylesheets.ReportTokensStyleSheet)\n")
SWITCH = ('            .Append(ReportGenerator.ThemeSwitchButton).Append("</div>");', '            .Append("</div>");')
SWITCH_SCRIPT = ('        html.Append("<script id=\\"kronikol-theme-switch\\">").Append(ReportGenerator.LoadResource("report-theme-switch-script.js")).Append("</script>\\n");\n', '')
TITLE_LINE = ('html.Append("<div class=\\"title-line\\"><h1>")', 'html.Append("<div class=\\"labs-title\\"><h1>")')
BADGE = ('            background: var(--kron-warn-soft);\n', '            background: rgb(255, 243, 205);\n')
TABLE_CARD = ('            background: var(--kron-card);\n', '')
HISTORY_FALLBACK = ('            background: var(--kron-quiet);\n', '            background: var(--kron-quiet, #f7f9fc);\n')
DIAGNOSTICS_FALLBACK = ('            background: var(--kron-card);\n', '            background: var(--kron-card, rgb(255, 251, 243));\n')
DARK = ('                :root[data-theme="dark"] {\n', '                :root[data-theme="dusk"] {\n')
SYSTEM = ('                    :root:not([data-theme]) {\n', '                    :root:not([data-theme]) .none {\n')
DARK_CARD = ('\n                    --violet-card: #1d1b25;', '\n                    --violet-card: #ddd6fe;')
ACCENT = ('                    --kron-accent: #6d28d9;\n', '')
HOVER = ('                    --kron-hover: #ede9fe;\n', '')
SELECTED = ('                    --kron-selected: #c4b5fd;\n', '')
LITERAL = ('.back-to-top { background: var(--violet-solid);', '.back-to-top { background: #8b5cf6;')
TICK = ('.step-status.passed { background: var(--violet-solid); color: var(--kron-on-solid); }', '.step-status.passed { background: var(--violet-solid); }')
TAB_HOVER = ('.diagram-toggle-active:hover { background: var(--violet-solid-hover); color: var(--kron-on-solid); }',
             '.diagram-toggle-active:hover { background: var(--violet-solid-hover); }')
DOT = ('                .details-radio-btn.toggle-btn.details-active::before { background: var(--kron-on-solid); border-color: var(--kron-on-solid); }\n', '')
HELP = ('            background: var(--kron-strip);\n            font-size: 0.85em;\n', '            background: rgb(250, 250, 250);\n            font-size: 0.85em;\n')
VIOLET_HELP = ('                .back-to-top:hover { background: var(--violet-solid-hover); }\n',
               '                .back-to-top:hover { background: var(--violet-solid-hover); }\n                .search-help-panel { border-color: #5B21B6; background: #1E1534; }\n')

MUTATIONS = [
    # core: the labs page
    ("the labs page chooses no theme before its styles", LABS, *BOOT, CORE, C + 'The_labs_page_chooses_its_theme_before_its_first_style'),
    ("the labs page without the token sheet", LABS, *TOKENS, CORE, C + 'The_labs_page_opens_its_styles_with_the_token_sheet'),
    ("the token sheet after the base sheet", LABS, *TOKENS_LATE, CORE, C + 'The_labs_page_opens_its_styles_with_the_token_sheet'),
    ("no switch on the labs title line", LABS, *SWITCH, CORE, C + 'The_labs_page_has_the_reports_theme_switch_on_its_title_line'),
    ("no switch script on the labs page", LABS, *SWITCH_SCRIPT, CORE, C + 'The_labs_page_has_the_reports_theme_switch_on_its_title_line'),
    ("no switch script on the labs page (the pin)", LABS, *SWITCH_SCRIPT, CORE, L + 'The_names_link_into_the_report_by_stable_id_and_the_page_runs_only_the_theme_scripts'),
    ("the title line not the report's", LABS, *TITLE_LINE, CORE, C + 'The_labs_page_has_the_reports_theme_switch_on_its_title_line'),
    ("the labs badge on its 4.x yellow", LABS_CSS, *BADGE, CORE, C + 'The_labs_pages_own_parts_are_drawn_in_the_tokens'),
    ("the table of every scenario no card", LABS_CSS, *TABLE_CARD, CORE, C + 'The_labs_pages_own_parts_are_drawn_in_the_tokens'),
    # core: the shared sheets
    ("a history colour falls back again", HISTORY_CSS, *HISTORY_FALLBACK, CORE, C + 'A_sheet_carried_only_beside_the_token_sheet_has_no_colour_of_its_own_to_fall_back_on'),
    ("a history colour falls back again (the pin)", HISTORY_CSS, *HISTORY_FALLBACK, CORE, H + 'Every_verdict_kind_keeps_its_own_look_and_every_colour_is_a_token'),
    ("a diagnostics colour falls back again", DIAGNOSTICS_CSS, *DIAGNOSTICS_FALLBACK, CORE, C + 'A_sheet_carried_only_beside_the_token_sheet_has_no_colour_of_its_own_to_fall_back_on'),
    # core: the violet theme
    ("the violet without its dark block", SHEETS, *DARK, CORE, C + 'The_violet_theme_sets_the_reports_tokens_for_the_light_and_the_dark_theme'),
    ("the violet without the system's dark block", SHEETS, *SYSTEM, CORE, C + 'The_violet_theme_sets_the_reports_tokens_for_the_light_and_the_dark_theme'),
    ("a dark card the light one", SHEETS, *DARK_CARD, CORE, C + 'The_violet_theme_sets_the_reports_tokens_for_the_light_and_the_dark_theme'),
    ("the accent left blue", SHEETS, *ACCENT, CORE, C + 'The_violet_theme_sets_the_reports_tokens_for_the_light_and_the_dark_theme'),
    ("the hover token not set", SHEETS, *HOVER, CORE, R + 'Violet_hovers_take_the_themes_tint_from_its_hover_token'),
    ("a colour written as a literal again", SHEETS, *LITERAL, CORE, C + 'Every_colour_the_violet_theme_draws_outside_its_token_blocks_is_a_variable'),
    ("the passed tick under the pass ink", SHEETS, *TICK, CORE, C + 'Every_solid_violet_the_theme_paints_carries_the_ink_made_for_it'),
    ("a hovered chosen tab under the accent", SHEETS, *TAB_HOVER, CORE, C + 'Every_solid_violet_the_theme_paints_carries_the_ink_made_for_it'),
    ("the chosen radio's dot in the accent", SHEETS, *DOT, CORE, C + 'Every_solid_violet_the_theme_paints_carries_the_ink_made_for_it'),
    # core: the search help
    ("the search help back on its light grey", BASE_CSS, *HELP, CORE, C + 'The_search_help_is_drawn_in_the_tokens_and_the_violet_leaves_it_to_them'),
    ("the violet's dark search help back", SHEETS, *VIOLET_HELP, CORE, C + 'The_search_help_is_drawn_in_the_tokens_and_the_violet_leaves_it_to_them'),
    # the browser
    ("the labs page without the token sheet", LABS, *TOKENS, E2E, P + 'The_labs_page_follows_the_systems_setting_and_paints_its_parts_in_the_tokens'),
    ("the table of every scenario no card", LABS_CSS, *TABLE_CARD, E2E, P + 'The_labs_page_follows_the_systems_setting_and_paints_its_parts_in_the_tokens'),
    ("no switch on the labs title line", LABS, *SWITCH, E2E, P + 'The_switch_on_the_labs_page_flips_its_theme_and_the_pick_holds_over_a_reload'),
    ("no switch script on the labs page", LABS, *SWITCH_SCRIPT, E2E, P + 'The_switch_on_the_labs_page_flips_its_theme_and_the_pick_holds_over_a_reload'),
    ("the violet without its dark block", SHEETS, *DARK, E2E, P + 'The_specifications_report_is_violet_in_the_light_and_the_dark_theme'),
    ("a hovered chosen tab under the accent", SHEETS, *TAB_HOVER, E2E, P + 'A_hovered_chosen_diagram_tab_keeps_its_white_ink_on_a_darker_violet'),
    ("the selected token not set", SHEETS, *SELECTED, E2E, P + 'The_violet_reaches_the_controls_its_4x_rules_never_named'),
    ("the hover token not set", SHEETS, *HOVER, E2E, T + 'A_hovered_header_control_takes_the_themes_tint'),
    ("the search help back on its light grey", BASE_CSS, *HELP, E2E, P + 'The_search_help_reads_in_every_theme'),
    ("no switch script on the labs page (the pin)", LABS, *SWITCH_SCRIPT, E2E, LP + 'The_page_runs_only_the_theme_scripts'),
    ("the labs page without the token sheet (the rings)", LABS, *TOKENS, E2E, HR + 'The_labs_page_draws_the_rings_and_the_timed_bars_with_none_of_the_reports_scripts'),
    ("the labs page without the token sheet (the card)", LABS, *TOKENS, E2E, DC + 'On_the_labs_page_the_diagnostics_stay_a_disclosure_in_the_themes_tokens'),
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
