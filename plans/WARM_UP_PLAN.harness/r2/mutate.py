"""R2's mutations (plans/WARM_UP_PLAN.md section 5): each undoes one part of the report's marks, and the fact named
must turn red.

  python mutate.py <worktree> <out.txt> [first index]

Run in a worktree of its own, after one full build of tests/Kronikol.Tests and tests/Kronikol.Tests.EndToEnd. Each
mutation replaces one exact string (it must occur once), builds src/Kronikol for net10.0 alone and copies Kronikol.dll
and .pdb into the bin of the test project that names the fact (the report's scripts are embedded in it), runs the
filter with --no-build, records the counts, and restores the file.
"""
import pathlib
import re
import shutil
import subprocess
import sys

root, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
first_index = int(sys.argv[3]) if len(sys.argv) > 3 else 0
LIB_BIN = root / 'src/Kronikol/bin/Debug/net10.0'
PROJECTS = {
    'core': ('tests/Kronikol.Tests/Kronikol.Tests.csproj', root / 'tests/Kronikol.Tests/bin/Debug/net10.0'),
    'e2e': ('tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj', root / 'tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0'),
}
RG = 'src/Kronikol/Reports/ReportGenerator.cs'
JS = 'src/Kronikol/Reports/report-duration-filter-function.js'
MUTATIONS = [
    ('The filter reads wall time (T42)', JS,
     'c.items[i].dur = ms > 0 && ms - warmUp < thresholdMs;', 'c.items[i].dur = ms > 0 && ms < thresholdMs;',
     'e2e', 'FullyQualifiedName~The_percentile_filter_ranks_by_each_scenarios_own_time'),
    ('The marker drawn on every badge (T40)', RG,
     'durationBadge = scenarioWarmUpMs > 0', 'durationBadge = scenarioWarmUpMs >= 0',
     'e2e', 'FullyQualifiedName~The_marker_and_its_tooltip_are_on_the_marked_scenario_alone'),
    ('The marker drawn on every badge (unit)', RG,
     'durationBadge = scenarioWarmUpMs > 0', 'durationBadge = scenarioWarmUpMs >= 0',
     'core', 'FullyQualifiedName~A_scenario_with_no_warm_up_is_drawn_as_before'),
    ('The badge coloured by wall time', RG,
     'var durationClass = DurationClass(durationMs - scenarioWarmUpMs);', 'var durationClass = DurationClass(durationMs);',
     'core', 'FullyQualifiedName~The_badge_colour_follows_the_time_left'),
    ('The percentiles over wall time', RG,
     '.Select(s => s.Duration!.Value.TotalMilliseconds - WarmUpMsOf(warmUp, s.Id))', '.Select(s => s.Duration!.Value.TotalMilliseconds)',
     'core', 'FullyQualifiedName~The_percentiles_are_over_each_scenarios_time_left'),
    ('The old percentile pick', RG,
     'sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];',
     'sorted.Length == 0 ? 0 : sorted[Math.Min((int)(sorted.Length * p), sorted.Length - 1)];',
     'core', 'FullyQualifiedName~A_percentile_is_the_nearest_rank'),
    ('The warm-up rules on every page (T44)', RG,
     '+ (warmUp.ScenarioMs.Count > 0 ? Stylesheets.WarmUpStyleSheet : "")', '+ Stylesheets.WarmUpStyleSheet',
     'core', 'FullyQualifiedName~Only_a_page_that_draws_a_mark_carries_the_warm_up_rules|FullyQualifiedName~A_page_with_no_mark_is_the_page_written_without_the_pass'),
    ('No timeline shading (T41)', RG,
     '+ $"<div class=\\"timeline-warm-up\\" style=', '+ $"<div class=\\"timeline-warm\\" style=',
     'e2e', 'FullyQualifiedName~The_timeline_shades_the_warm_ups_share_of_the_bar'),
    ('A group sums no warm-up', RG,
     'var groupWarmUpMs = scenarios.Sum(s => WarmUpMsOf(warmUp, s.Id));', 'var groupWarmUpMs = 0.0;',
     'core', 'FullyQualifiedName~A_parameterized_group_sums_its_rows_warm_up_as_it_sums_their_durations'),
    ('A caller with the calls alone gets no marks', RG,
     'warmUp ??= trackedLogs is null ? WarmUpResult.None : WarmUpCalls.Find(features, trackedLogs);', 'warmUp ??= WarmUpResult.None;',
     'core', 'FullyQualifiedName~A_marked_scenario_keeps_its_wall_time_and_adds_its_warm_up_as_muted_text'),
    ('A merged report judges the merged calls', 'src/Kronikol/Reports/Merge/MergeableReportRenderer.cs',
     "// The shards' own marks, carried as the data file carries them.\n            warmUp: report.WarmUp);",
     "// The shards' own marks, carried as the data file carries them.\n            warmUp: null);",
     'core', 'FullyQualifiedName~Shards_that_carry_no_marks_get_none_though_their_merged_calls_would'),
    ('The timeline tooltip names skipped bars yellow again', RG,
     'grey = skipped, orange = bypassed', 'yellow = skipped',
     'core', 'FullyQualifiedName~The_timeline_names_the_colours_it_paints'),
]


def run(args):
    return subprocess.run(args, cwd=root, capture_output=True, text=True, encoding='utf-8', errors='replace')


lines = []
for name, rel, old, new, project, filt in MUTATIONS[first_index:]:
    path = root / rel
    text = path.read_text(encoding='utf-8')
    if text.count(old) != 1:
        lines.append(f'SETUP ERROR {name}: {text.count(old)} matches')
        out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
        continue
    path.write_text(text.replace(old, new), encoding='utf-8', newline='')
    try:
        b = run(['dotnet', 'build', 'src/Kronikol/Kronikol.csproj', '-f', 'net10.0', '-nologo', '-v', 'q'])
        if b.returncode != 0:
            lines.append(f'BUILD FAILED {name}: ' + ' | '.join(l for l in b.stdout.splitlines() if ' error ' in l)[:400])
            continue
        csproj, test_bin = PROJECTS[project]
        for leaf in ('Kronikol.dll', 'Kronikol.pdb'):
            shutil.copy2(LIB_BIN / leaf, test_bin / leaf)
        t = run(['dotnet', 'test', csproj, '--no-build', '-nologo', '-v', 'q', '--filter', filt])
        m = re.search(r'Failed:\s*(\d+), Passed:\s*(\d+)', t.stdout)
        verdict = f'failed {m.group(1)}, passed {m.group(2)}' if m else ('no counts: ' + t.stdout[-300:])
        caught = 'CAUGHT' if m and int(m.group(1)) > 0 else 'NOT CAUGHT'
        lines.append(f'{caught}  {name}  [{project}: {filt}]  {verdict}')
    finally:
        path.write_text(text, encoding='utf-8', newline='')
    out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
# Put the unmutated library back in both test bins.
b = run(['dotnet', 'build', 'src/Kronikol/Kronikol.csproj', '-f', 'net10.0', '-nologo', '-v', 'q'])
for _, test_bin in PROJECTS.values():
    for leaf in ('Kronikol.dll', 'Kronikol.pdb'):
        shutil.copy2(LIB_BIN / leaf, test_bin / leaf)
out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
print('\n'.join(lines))
