"""R1's mutations (plans/WARM_UP_PLAN.md section 5): each breaks one part of the rule or its records, and the fact named
must turn red.

  python mutate.py <worktree> <out.txt> [first index]

Run in a worktree of its own, after one full build of tests/Kronikol.Tests. Each mutation replaces one exact string (it
must occur once), builds src/Kronikol for net10.0 alone and copies Kronikol.dll and .pdb into the test project's bin,
runs the filter with --no-build, records the counts, and restores the file. Every mutation here is in src/Kronikol.
"""
import pathlib
import re
import shutil
import subprocess
import sys

root, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
first_index = int(sys.argv[3]) if len(sys.argv) > 3 else 0
LIB_BIN = root / 'src/Kronikol/bin/Debug/net10.0'
TEST_BIN = root / 'tests/Kronikol.Tests/bin/Debug/net10.0'
PASS = 'src/Kronikol/Reports/WarmUpCalls.cs'
RG = 'src/Kronikol/Reports/ReportGenerator.cs'
MUTATIONS = [
    ('Two baseline calls instead of one (T3)', PASS,
     'if (later.Count == 0)', 'if (later.Count < 2)',
     'FullyQualifiedName~One_later_call_is_enough_to_compare_with'),
    ('The baseline taken from all later calls, overlapping ones included (T8)', PASS,
     'var baseline = calls.Skip(1).Where(c => c.Start >= firstEnd);', 'var baseline = calls.Skip(1);',
     'FullyQualifiedName~The_baseline_is_the_calls_that_started_after_the_first_ended'),
    ('No released-together test (T8)', PASS,
     'if (byRatio || released)', 'if (byRatio)',
     'FullyQualifiedName~A_call_that_overlapped_the_first_and_was_released_with_it_waited_for_it'),
    ('The 5xx guard removed (T18)', PASS,
     'if (first.Failed)\n            baseline = baseline.Where(c => c.Failed);', '',
     'FullyQualifiedName~A_failing_first_call_is_not_compared_with_calls_that_succeeded'),
    ('Ratio 5 (T13)', PASS,
     'internal const double Ratio = 10;', 'internal const double Ratio = 5;',
     'FullyQualifiedName~The_ratio_and_the_floor_are_both_inclusive'),
    ('Floor 25 ms (T13)', PASS,
     'internal const double FloorMs = 50;', 'internal const double FloorMs = 25;',
     'FullyQualifiedName~The_ratio_and_the_floor_are_both_inclusive'),
    ('File order instead of timestamps (T17)', PASS,
     'calls.Sort((a, b) => a.Start != b.Start', 'if (false) calls.Sort((a, b) => a.Start != b.Start',
     'FullyQualifiedName~The_order_is_the_runs_not_the_data_files'),
    ("The app's calls judged too (T4, T14)", PASS,
     'if (request.MetaType == RequestResponseMetaType.Event || services[request.TestId].Contains(request.CallerName))',
     'if (request.MetaType == RequestResponseMetaType.Event)',
     'FullyQualifiedName~The_apps_own_calls_are_not_judged_and_the_warm_up_is_the_test_calls_time|FullyQualifiedName~A_caller_that_is_also_called_in_its_scenario_is_the_app_and_is_not_judged'),
    ('A merge recomputes the marks (T22)', 'src/Kronikol/Reports/Merge/MergeableReportRenderer.cs',
     'warmUp: report.WarmUp);', 'warmUp: null);',
     'FullyQualifiedName~Shards_that_carry_no_marks_get_none_though_their_merged_calls_would'),
    ('warmUp: null written on every record (T20)', RG,
     '[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]\n', '',
     'FullyQualifiedName~A_marked_call_carries_its_mark_on_its_request_record_and_nowhere_else|FullyQualifiedName~A_run_with_no_warm_up_writes_no_warm_up_keys'),
    ('The cap at the scenario duration removed (T12)', PASS,
     'if (Math.Min(covered, wall) is > 0 and var ms)', 'if (covered is > 0 and var ms)',
     'FullyQualifiedName~A_warm_up_longer_than_its_scenario_is_capped_at_the_scenarios_duration'),
    ('Overlapping warm-up counted twice (T12)', PASS,
     'var covered = Covered(scenario.Select(c => (c.Start, c.End!.Value)));', 'var covered = scenario.Sum(c => c.Duration);',
     'FullyQualifiedName~Overlapping_warm_up_calls_in_one_scenario_are_counted_once'),
    ('Slowest by wall time again (T30)', 'src/Kronikol/Query/QueryCommand.Overview.cs',
     'var slowest = scenarios.OrderByDescending(s => s.TimeLeftSeconds).Take(3).ToArray();', 'var slowest = scenarios.OrderByDescending(s => s.DurationSeconds).Take(3).ToArray();',
     'FullyQualifiedName~Summary_ranks_slowest_by_the_time_left_and_says_where_the_warm_up_went'),
    ('--slower-than by wall time again', 'src/Kronikol/Query/QueryCommand.Overview.cs',
     'scenario.TimeLeftSeconds < slower)', 'scenario.DurationSeconds < slower)',
     'FullyQualifiedName~Slower_than_reads_the_time_left'),
    ("diff's Slower by wall time again", 'src/Kronikol/Query/QueryCommand.Search.cs',
     'double Seconds(ScenarioEntry scenario) => leftOut ? scenario.TimeLeftSeconds : scenario.DurationSeconds;', 'double Seconds(ScenarioEntry scenario) => scenario.DurationSeconds;',
     'FullyQualifiedName~Diff_reads_the_time_left_when_both_runs_carry_marks'),
    ('Starts read to the tick, not as the data file writes them (S7)', PASS,
     'ShapeOf(request, rules), AsWritten(request.Timestamp),', 'ShapeOf(request, rules), request.Timestamp,',
     'FullyQualifiedName~Calls_that_start_in_one_millisecond_are_ordered_as_the_data_file_records_them'),
]


def run(args):
    return subprocess.run(args, cwd=root, capture_output=True, text=True, encoding='utf-8', errors='replace')


lines = []
for name, rel, old, new, filt in MUTATIONS[first_index:]:
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
        for leaf in ('Kronikol.dll', 'Kronikol.pdb'):
            shutil.copy2(LIB_BIN / leaf, TEST_BIN / leaf)
        t = run(['dotnet', 'test', 'tests/Kronikol.Tests/Kronikol.Tests.csproj', '--no-build', '-nologo', '-v', 'q', '--filter', filt])
        m = re.search(r'Failed:\s*(\d+), Passed:\s*(\d+)', t.stdout)
        verdict = f'failed {m.group(1)}, passed {m.group(2)}' if m else ('no counts: ' + t.stdout[-300:])
        caught = 'CAUGHT' if m and int(m.group(1)) > 0 else 'NOT CAUGHT'
        lines.append(f'{caught}  {name}  [{filt}]  {verdict}')
    finally:
        path.write_text(text, encoding='utf-8', newline='')
    out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
print('\n'.join(lines))
