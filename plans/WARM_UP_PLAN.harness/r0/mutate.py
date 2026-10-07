"""R0's mutations: each undoes one fix, and the fact named must turn red.

  python mutate.py <worktree> <out.txt> [first index]

Run in a worktree of its own, after one full build of tests/Kronikol.Tests. Each mutation replaces one exact string
(it must occur once), builds src/Kronikol for net10.0 alone and copies Kronikol.dll and .pdb into the test project's
bin (a test-project build recompiles every extension, minutes per mutation), runs the filter with --no-build, records
the counts, and restores the file. Every mutation here is in src/Kronikol.
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
RG = 'src/Kronikol/Reports/ReportGenerator.cs'
MUTATIONS = [
    ('F1 timeline width formatted with the current culture again (T51)', RG,
     'body.Append(CultureInfo.InvariantCulture, $"<div class=\\"timeline-track\\">',
     'body.Append($"<div class=\\"timeline-track\\">',
     'FullyQualifiedName~The_timeline_writes_each_bar_width_with_a_point'),
    ('Equal instants read as a measured 0 again', RG,
     'if (elapsed > 0)', 'if (elapsed >= 0)',
     'FullyQualifiedName~LogPair_calls_carry_no_duration_into_the_data_file'),
    ('The merge reads an unknown duration as 0 again', 'src/Kronikol/Reports/Merge/MergeableReportReader.cs',
     '&& d.ValueKind == JsonValueKind.Number && d.GetDouble() > 0', '&& d.ValueKind == JsonValueKind.Number',
     'FullyQualifiedName~MergeReadsDurationsTests'),
    ('Rename leaves expiredFrom alone again', 'src/Kronikol/Reports/Merge/MergeableReportMerger.cs',
     'ExpiredFromTestId = i.ExpiredFromTestId is { } from ? Id(from) : null', 'ExpiredFromTestId = i.ExpiredFromTestId',
     'FullyQualifiedName~An_expired_call_follows_its_scenario_when_the_merge_renames_it'),
    ('The merged report draws no background section again', 'src/Kronikol/Reports/Merge/MergeableReportRenderer.cs',
     'background: BackgroundAttribution.Summarise(report.Interactions, report.Features),', '',
     'FullyQualifiedName~A_merged_report_draws_the_background_calls_section'),
    ('Failures.md times calls from timestamps only again', 'src/Kronikol/Reports/FailuresDigestGenerator.cs',
     'request.DurationMs ?? response?.DurationMs', '(double?)null',
     'FullyQualifiedName~A_call_shows_the_time_its_capturer_measured|FullyQualifiedName~A_call_sent_as_one_record_shows_its_measured_time'),
    ('TrackingProxy stamps both records when the call returns again', 'src/Kronikol/Tracking/TrackingProxy.cs',
     'requestAt: startedAt,', 'requestAt: DateTimeOffset.UtcNow,',
     'FullyQualifiedName~is_timed_from_its_start_to_its_end'),
    ('services reads the response record alone again', 'src/Kronikol/Query/QueryCommand.Overview.cs',
     '? _durationByCall.TryAdd(call, ms)', '? interaction.Type.Equals("Response", StringComparison.OrdinalIgnoreCase)',
     'FullyQualifiedName~ServicesDurationTests'),
    ('The query engine runs under the machine culture again (T52)', 'src/Kronikol/Query/QueryCommand.cs',
     'using var culture = InvariantCultureScope.Begin();', '',
     'FullyQualifiedName~QueryCultureTests'),
    ('The flame colour comes from GetHashCode again', 'src/Kronikol/InternalFlow/InternalFlowRenderer.cs',
     'SourceHash(rawSource)', 'rawSource.GetHashCode()',
     'FullyQualifiedName~A_flame_bar_has_the_colour_the_browser_gives_its_source_in_every_process'),
]


def run(args):
    return subprocess.run(args, cwd=root, capture_output=True, text=True, encoding='utf-8', errors='replace')


lines = []
for name, rel, old, new, filt in MUTATIONS[first_index:]:
    path = root / rel
    text = path.read_text(encoding='utf-8')
    if text.count(old) != 1:
        lines.append(f'SETUP ERROR {name}: {text.count(old)} matches')
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
