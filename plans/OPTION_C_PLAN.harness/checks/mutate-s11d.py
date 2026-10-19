# S11d's mutations: each undoes one part of the Timeline's controls, rebuilds, runs the test that should catch it,
# expects it to FAIL, and puts the file back. Test runs start below normal priority.
# usage: python mutate-s11d.py [part of a test name, to run only its mutations]   (takes a few minutes)
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
JS = ROOT + 'src/Kronikol/Reports/report-timeline-script.js'
CSS = ROOT + 'src/Kronikol/Reports/stylesheets.css'
T = 'Kronikol.Tests.EndToEnd.TimelineTabTests.'
MUTATIONS = [
    ("rows follow no scenario", JS, "scenarios: byId[row.getAttribute('data-sid')] || [],", "scenarios: [],",
     T + 'Filtered_is_the_start_and_the_timeline_follows_the_filters'),
    ("only a scenario's own element is mapped", JS, "document.querySelectorAll('[data-stable-id]')",
     "document.querySelectorAll('details.scenario[data-stable-id]')", T + 'An_outlines_example_rows_show_and_hide_with_their_scenario'),
    ("the bars are never rescaled while the warm-up shows", JS, "var asWritten = withWarm && shown === rows.length;",
     "var asWritten = withWarm;", T + 'Two_scenarios_of_one_name_in_two_features_show_and_hide_apart'),
    ("the scale counts the warm-up while it is hidden", JS, "longest = Math.max(longest, withWarm ? r.total : r.own);",
     "longest = Math.max(longest, r.total);", T + 'With_the_warm_up_hidden_each_bar_and_time_is_the_scenarios_own'),
    ("a minute reads in seconds", JS, "return Math.floor(ms / 60000) + 'm ' + Math.floor(ms % 60000 / 1000) + 's';",
     "return (ms / 1000).toFixed(1) + 's';", T + 'The_page_writes_a_time_as_the_report_does'),
    ("Without warm-up sorts by the whole time", JS, "(own ? b.own - a.own : b.total - a.total)", "(b.total - a.total)",
     T + 'Order_without_warm_up_puts_the_slowest_by_its_own_time_first'),
    ("the warm-up stripe stays when hidden", JS, "if (r.shade) r.shade.hidden = !withWarm;", "",
     T + 'With_the_warm_up_hidden_each_bar_and_time_is_the_scenarios_own'),
    ("the note shows whenever Filtered", JS, "var partial = !full && shown < rows.length;", "var partial = !full;",
     T + 'Filtered_is_the_start_and_the_timeline_follows_the_filters'),
    ("the note never shows", JS, "note.hidden = !partial;", "note.hidden = true;",
     T + 'Two_scenarios_of_one_name_in_two_features_show_and_hide_apart'),
    ("the page's filters are not read at the start", JS, "        apply(state.order !== 'with');\n", "",
     T + 'A_filter_in_the_pages_link_reaches_the_timeline_as_it_opens'),
    ("a change of the filters is ignored", JS,
     "document.addEventListener('kron-filter-change', function () { if (state.view === 'filtered') apply(false); });", "",
     T + 'Filtered_is_the_start_and_the_timeline_follows_the_filters'),
    ("a bypassed bar is orange", CSS, ".timeline-bar-bypassed { background: var(--kron-bypass); }",
     ".timeline-bar-bypassed { background: orange; }", T + 'The_controls_the_bars_and_the_note_take_the_themes_colours'),
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
