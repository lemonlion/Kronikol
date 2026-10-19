# S11f's mutations: each undoes one part of the History rings and timed bars, rebuilds, runs the test that should catch
# it (the core class where it can, Playwright where only a page shows it), expects it to FAIL, and puts the file back.
# The 99 cap is not mutated: with the rate floored, a run short of every scenario never reaches 100 either way.
# usage: python mutate-s11f.py [part of a test name, to run only its mutations]   (takes a few minutes)
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
RG = ROOT + 'src/Kronikol/Reports/ReportGenerator.cs'
HH = ROOT + 'src/Kronikol/History/HistoryHtml.cs'
CSS = ROOT + 'src/Kronikol/Reports/history-styles.css'
CORE = ('tests/Kronikol.Tests/Kronikol.Tests.csproj', 'Kronikol.Tests.History.HistoryRingsMarkupTests.')
E2E = ('tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj', 'Kronikol.Tests.EndToEnd.HistoryRingsTests.')
MUTATIONS = [
    ("the rate rounds", RG, '(int)Math.Min(99, Math.Floor(100.0 * passed / total))', '(int)Math.Min(99, Math.Round(100.0 * passed / total))',
     CORE, 'A_ring_reads_100_percent_only_when_every_scenario_of_its_run_passed'),
    ("the fail arc is drawn always", RG, 'if (failed > 0) segments.Add((100.0 * failed / total, "fail", "Failed", failed));',
     'segments.Add((100.0 * failed / total, "fail", "Failed", failed));', CORE, 'A_fail_arc_is_drawn_only_on_a_run_that_failed'),
    ("the track is left out", HH, 'run.Total, track: true, arcTitles: false)', 'run.Total, track: false, arcTitles: false)',
     CORE, 'A_run_of_no_scenarios_is_drawn_as_the_track_alone_with_no_rate'),
    ("History's arcs keep their titles", HH, 'run.Total, track: true, arcTitles: false)', 'run.Total, track: true, arcTitles: true)',
     CORE, 'A_fail_arc_is_drawn_only_on_a_run_that_failed'),
    ("an hour reads in minutes", RG, 'return string.Create(CultureInfo.InvariantCulture, $"{(long)total.TotalHours}h {total.Minutes}m");',
     'return string.Create(CultureInfo.InvariantCulture, $"{(long)total.TotalMinutes}m {total.Seconds}s");',
     CORE, 'A_bar_carries_its_runs_time_in_whole_units_cut_and_never_rounded_up'),
    ("no bar is short", HH, 'private const double ShortBarPercent = 30;', 'private const double ShortBarPercent = 0;',
     CORE, 'A_bar_too_short_to_hold_its_time_is_marked'),
    ("the pass arc has no fallback", CSS, '.history-ring .ring-pass { stroke: var(--kron-pass-ring, #3f9a62); }',
     '.history-ring .ring-pass { stroke: var(--kron-pass-ring); }', CORE, 'Every_verdict_kind_keeps_its_own_look'),
    ("always-failing is not drawn as a failure", CSS,
     '.history-verdict-broke, .history-verdict-failing, .history-verdict-always-failing {',
     '.history-verdict-broke, .history-verdict-failing {', CORE, 'Every_verdict_kind_keeps_its_own_look'),
    ("the bars open on the oldest run", CSS, '            direction: rtl;\n', '',
     E2E, 'A_window_of_fifty_runs_keeps_every_time_legible_and_opens_on_the_newest_run'),
    ("the rings stay on one row", CSS, '            grid-template-columns: repeat(auto-fit, minmax(56px, 1fr));\n',
     '            grid-template-columns: none;\n            grid-auto-flow: column;\n',
     E2E, 'A_window_of_fifty_runs_keeps_every_time_legible_and_opens_on_the_newest_run'),
    ("a failed run's rate is plain ink", CSS, '.history-ring-failed .ring-rate { fill: var(--kron-fail-ink, #7c2a24); }', '',
     E2E, 'The_rings_the_bars_and_the_card_take_the_themes_tokens'),
    ("a short bar's time sits in it", CSS, '.history-bar-short .history-time {', '.history-bar-short-unused .history-time {',
     E2E, 'A_bars_time_sits_in_the_middle_of_a_tall_bar_and_just_above_a_short_one'),
]
ONLY = sys.argv[1] if len(sys.argv) > 1 else ''
MUTATIONS = [m for m in MUTATIONS if ONLY in m[5]]

killed = 0
for name, path, old, new, (project, cls), test in MUTATIONS:
    s = open(path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + project, '--filter', 'FullyQualifiedName~' + cls + test,
                            '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
