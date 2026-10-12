"""Mutations of the warm-up audit patch (Patch A), each run once against the fact named to catch it.

Run from the audit worktree, whose fixes are committed (a local checkpoint): each mutation is written, the core test
project rebuilt, the named facts run, and the file restored with git checkout. A mutation whose build fails is reported
as such, never as caught.
"""
import subprocess
import sys

M = [
    ('M1 a merge renames the totals and not each mark', 'src/Kronikol/Reports/Merge/MergeableReportMerger.cs',
     'report.WarmUp.Calls.ToDictionary(e => e.Key, e => e.Value with { ScenarioId = Id(e.Value.ScenarioId) })',
     'report.WarmUp.Calls.ToDictionary(e => e.Key, e => e.Value)',
     'A_scenario_the_merge_renames_keeps_its_own_calls_in_its_tooltips'),
    ('M2 ties go to capture order again', 'src/Kronikol/Reports/WarmUpCalls.cs',
     '            : a.Listed != b.Listed ? a.Listed.CompareTo(b.Listed)\n', '',
     'Calls_that_tie_are_ordered_as_the_data_file_lists_them'),
    ('M3 the file order takes features as handed over, not by name', 'src/Kronikol/Reports/WarmUpCalls.cs',
     'features.OrderBy(f => f.DisplayName).SelectMany(f => f.Scenarios ?? [])', 'features.SelectMany(f => f.Scenarios ?? [])',
     'Calls_that_tie_are_ordered_as_the_data_file_lists_them'),
    ('M4 the tooltip rounds from a minute again', 'src/Kronikol/Reports/ReportGenerator.cs',
     "return badge.Contains('m') ? badge : ", 'return false ? badge : ',
     'The_tooltip_reads_each_time_as_the_badge_writes_it'),
    ('M5 the badge writes 60.0s again', 'src/Kronikol/Reports/ReportGenerator.cs',
     'return seconds == "60.0" ? "1m 0s" : seconds + "s";', 'return seconds + "s";',
     'The_tooltip_reads_each_time_as_the_badge_writes_it'),
    ('M6 percentiles over unrounded own times', 'src/Kronikol/Reports/ReportGenerator.cs',
     '.Select(s => WholeMs(s.Duration!.Value.TotalMilliseconds) - WholeMs(WarmUpMsOf(warmUp, s.Id)))',
     '.Select(s => s.Duration!.Value.TotalMilliseconds - WarmUpMsOf(warmUp, s.Id))',
     'A_percentile_is_an_own_time_as_the_filter_reads_it'),
    ('M7 diff reads the time left only when both runs carry a mark', 'src/Kronikol/Query/QueryCommand.Search.cs',
     'var leftOut = ReportScanner.MarksWarmUp(left) && ReportScanner.MarksWarmUp(right);',
     'var leftOut = left.Scenarios.Any(s => s.WarmUpSeconds is not null) && right.Scenarios.Any(s => s.WarmUpSeconds is not null);',
     'WarmUpQueryTests.Diff'),
    ('M8 every report reads as marking the warm-up, whatever wrote it', 'src/Kronikol/Query/ReportScanner.cs',
     'AtLeast(index.KronikolVersion, WarmUpSince) || index.Scenarios.Any(s => s.WarmUpSeconds is not null);', 'true;',
     'Diff_reads_a_run_written_before_the_marks_by_wall_time_on_both_sides'),
    ('M9 interactions --json names the first call by its id', 'src/Kronikol/Query/QueryCommand.Payloads.cs',
     'first = FirstCallAddress(index, warmUp)', 'first = warmUp.First',
     'Interactions_mark_a_warm_up_call_after_its_duration'),
    ('M10 a first call carries "first": null again', 'src/Kronikol/Query/QueryCommand.Payloads.cs',
     'QueryWriter.WithMember(item, "warmUp", warmUp.Waited', 'QueryWriter.WithMember(item, "warmUp", true',
     'Interactions_mark_a_warm_up_call_after_its_duration'),
    ('M11 a scenarios row says nothing of its warm-up', 'src/Kronikol/Query/QueryCommand.Overview.cs',
     '+ repeats + attempt + warmUp);', '+ repeats + attempt);',
     'Scenarios_say_how_much_of_each_was_the_warm_up'),
    ('M12 a scenarios item has no warmUpSeconds', 'src/Kronikol/Query/QueryCommand.Overview.cs',
     'scenario.WarmUpSeconds is { } seconds ? QueryWriter.WithMember(item, "warmUpSeconds", seconds) : item;', 'item;',
     'Scenarios_say_how_much_of_each_was_the_warm_up'),
    ('M13 --slower-than says nothing of what it left out', 'src/Kronikol/Query/QueryCommand.Overview.cs',
     'if (warmedOut > 0)', 'if (false)',
     'Slower_than_reads_the_time_left'),
    ('M14 an example column replaces a scenario field again', 'src/Kronikol/Query/ReportScanner.cs',
     '            if (!At("scenarios", "#"))\n                return;\n', '',
     'ExampleColumnsTests'),
    ('M15 Slowest ties in file order again', 'src/Kronikol/Query/QueryCommand.Overview.cs',
     '.ThenByDescending(s => s.DurationSeconds)', '',
     'Slowest_is_listed_when_every_scenario_left_no_time_of_its_own'),
    ('M16 the warm-up member counts every scenario\'s calls', 'src/Kronikol/Query/QueryCommand.Overview.cs',
     'calls = warmed.Sum(', 'calls = scenarios.Sum(',
     'The_warm_up_line_counts_the_calls_of_the_scenarios_it_counts'),
    ('M17 the warm-up line writes 3 ms as 0s', 'src/Kronikol/Query/QueryCommand.Overview.cs',
     'static string Time(double seconds) => seconds < 0.01 ? $"{seconds * 1000:0.#}ms" : $"{seconds:0.##}s";',
     'static string Time(double seconds) => $"{seconds:0.##}s";',
     'A_warm_up_under_ten_milliseconds_reads_in_milliseconds'),
    ('M18 a mark of an unknown kind is kept', 'src/Kronikol/Query/ReportScanner.cs',
     'if (_interaction.WarmUp is { Kind: not ("first" or "waited") })', 'if (false)',
     'A_mark_this_build_cannot_read_is_no_mark'),
    ('M19 a numeric kind is read as a string', 'src/Kronikol/Query/ReportScanner.cs',
     'case "kind": mark.Kind = reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : ""; break;',
     'case "kind": mark.Kind = reader.GetString() ?? ""; break;',
     'A_mark_this_build_cannot_read_is_no_mark'),
    ('M20 the XSD takes any kind', 'src/Kronikol/Reports/ReportGenerator.cs',
     'new XElement(xs + "simpleType", new XElement(xs + "restriction", new XAttribute("base", "xs:string"),\n'
     '                                    new XElement(xs + "enumeration", new XAttribute("value", WarmUpMark.FirstKind)),\n'
     '                                    new XElement(xs + "enumeration", new XAttribute("value", WarmUpMark.WaitedKind))))),',
     'new XAttribute("type", "xs:string")),',
     'The_xsd_refuses_a_mark_the_json_schema_refuses'),
    ('M21 the XSD takes no baseline calls', 'src/Kronikol/Reports/ReportGenerator.cs',
     'new XAttribute("name", "BaselineCalls"), new XAttribute("type", "xs:positiveInteger")',
     'new XAttribute("name", "BaselineCalls"), new XAttribute("type", "xs:int")',
     'The_xsd_refuses_a_mark_the_json_schema_refuses'),
    ('M22 the XSD takes a negative time', 'src/Kronikol/Reports/ReportGenerator.cs',
     ',\n            new XElement(xs + "minInclusive", new XAttribute("value", "0"))));', '));',
     'The_xsd_refuses_a_mark_the_json_schema_refuses'),
    ('M23 the timeline never names its stripes', 'src/Kronikol/Reports/ReportGenerator.cs',
     '(warmUp.ScenarioMs.Count > 0 ? " A striped start', '(false ? " A striped start',
     'The_timeline_says_what_its_stripes_are_when_it_draws_them'),
    ('M24 the timeline names stripes it does not draw', 'src/Kronikol/Reports/ReportGenerator.cs',
     '(warmUp.ScenarioMs.Count > 0 ? " A striped start', '(true ? " A striped start',
     'The_timeline_says_what_its_stripes_are_when_it_draws_them'),
    ('M25 no page carries the warm-up rules', 'src/Kronikol/Reports/ReportGenerator.cs',
     '+ (warmUp.ScenarioMs.Count > 0 ? Stylesheets.WarmUpStyleSheet : "")', '+ ""',
     'WarmUpPagesTests'),
]

only = set(sys.argv[1:])
for name, path, old, new, test in M:
    if only and name.split()[0] not in only:
        continue
    text = open(path, encoding='utf-8', newline='').read()
    if text.count(old) != 1:
        print(f'SKIPPED {name}: the code to mutate occurs {text.count(old)} times', flush=True)
        continue
    open(path, 'w', encoding='utf-8', newline='').write(text.replace(old, new))
    try:
        build = subprocess.run(['dotnet', 'build', 'tests/Kronikol.Tests/Kronikol.Tests.csproj', '-v', 'q', '-nologo'],
                               capture_output=True, text=True)
        if build.returncode != 0:
            print(f'BUILD FAILED {name}', flush=True)
            continue
        run = subprocess.run(['dotnet', 'test', 'tests/Kronikol.Tests/Kronikol.Tests.csproj', '--no-build', '-v', 'q',
                              '--filter', f'FullyQualifiedName~{test}'], capture_output=True, text=True)
        summary = next((l.strip() for l in run.stdout.splitlines() if 'Failed:' in l and 'Passed:' in l), run.stdout[-300:])
        verdict = 'CAUGHT ' if run.returncode != 0 else 'SURVIVED'
        print(f'{verdict} {name}  [{test}]  {summary}', flush=True)
    finally:
        subprocess.run(['git', 'checkout', '--', path], check=True)
subprocess.run(['dotnet', 'build', 'tests/Kronikol.Tests/Kronikol.Tests.csproj', '-v', 'q', '-nologo'], capture_output=True)
print('done; the tree is restored and rebuilt', flush=True)
