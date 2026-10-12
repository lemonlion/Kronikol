"""Mutations of the warm-up audit's Patch B, each run once against the fact named to catch it, in that fact's project.

Run from the Patch B worktree, whose fixes are committed (a local checkpoint): each mutation is written, the named test
project rebuilt, the named facts run, and the file restored with git checkout. A mutation whose build fails is reported
as such, never as caught; a run that does not end in ten minutes is reported as TIMED OUT.
"""
import subprocess
import sys

CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
SQLITE = 'tests/Kronikol.Tests.Sqlite/Kronikol.Tests.Sqlite.csproj'
REDIS = 'tests/Kronikol.Tests.Redis/Kronikol.Tests.Redis.csproj'
WEAVER = 'tests/Kronikol.Tests.StepTracking/Kronikol.Tests.StepTracking.csproj'

# (name, file, old, new, project, filter, occurrences)
M = [
    ('MB1 a ValueTask call is logged as it starts again', 'src/Kronikol/Tracking/TrackingProxy.cs',
     'if (result is ValueTask valueTask)', 'if (result is ValueTask valueTask && valueTask.IsCompleted && !valueTask.IsCompleted)',
     CORE, 'TrackingProxyTests.A_value_task', 1),
    ('MB2 a ValueTask<T> call is logged as it starts again', 'src/Kronikol/Tracking/TrackingProxy.cs',
     'valueTaskType.GetGenericTypeDefinition() == typeof(ValueTask<>))', 'valueTaskType.GetGenericTypeDefinition() == typeof(Task<>))',
     CORE, 'TrackingProxyTests.A_value_task', 1),
    ('MB3a the serializer writes a ValueTask\'s members again', 'src/Kronikol/Tracking/TrackingSafeSerializer.cs',
     'if (value is ValueTask || value.GetType()', 'if (value.GetType()',
     CORE, 'A_value_task_is_written_without_being_read', 1),
    ('MB3b the serializer reads a pending ValueTask<T> again', 'src/Kronikol/Tracking/TrackingSafeSerializer.cs',
     'if (value is ValueTask || value.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(ValueTask<>))',
     'if (value is ValueTask)',
     CORE, 'A_value_task_is_written_without_being_read', 1),
    ('MB4 a measured duration loses to the timestamps', 'src/Kronikol/Reports/ReportGenerator.cs',
     '        if ((request?.DurationMs ?? response?.DurationMs) is { } measured)\n            return measured;\n', '',
     CORE, 'SequenceCollapserTests|FullyQualifiedName~RelationshipStatsTests', 1),
    ('MB5 the loop label counts an untimed call as 0 ms', 'src/Kronikol/PlantUml/SequenceCollapser.cs',
     'if (Reports.ReportGenerator.InteractionDuration(unit.Entries[0], unit.Entries[1]) is { } ms)\n                durations.Add(ms);',
     'durations.Add(Reports.ReportGenerator.InteractionDuration(unit.Entries[0], unit.Entries[1]) ?? 0);',
     CORE, 'SequenceCollapserTests', 1),
    ('MB6 component stats read the timestamps over a measured duration', 'src/Kronikol/ComponentDiagram/ComponentFlowSegmentBuilder.cs',
     'var duration = Reports.ReportGenerator.InteractionDuration(req, res) ?? (res.Timestamp!.Value - req.Timestamp!.Value).TotalMilliseconds;',
     'var duration = (res.Timestamp!.Value - req.Timestamp!.Value).TotalMilliseconds;',
     CORE, 'RelationshipStatsTests', 1),
    ('MB7 a title takes the machine\'s casing', 'src/Kronikol/ScenarioTitleResolver.cs',
     ': char.ToUpperInvariant(humanized[0]) + humanized[1..].ToLowerInvariant();',
     ': char.ToUpper(humanized[0]) + humanized[1..].ToLowerInvariant();',
     CORE, 'ScenarioTitleResolverTests.A_title_reads_the_same', 1),
    ('MB8 a feature name is title-cased with the machine\'s culture', 'src/Kronikol/StringCasing.cs',
     ': CultureInfo.InvariantCulture.TextInfo.ToTitleCase(humanized);', ': CultureInfo.CurrentCulture.TextInfo.ToTitleCase(humanized);',
     CORE, 'ScenarioTitleResolverTests.A_title_reads_the_same', 1),
    ('MB9 camel case lowers with the machine\'s culture', 'src/Kronikol/StringCasing.cs',
     '? $"{char.ToLowerInvariant(word[0])}{word[1..]}"', '? $"{char.ToLower(word[0])}{word[1..]}"',
     CORE, 'ScenarioTitleResolverTests.A_title_reads_the_same', 1),
    ('MB10 the weaver humanizes with the build machine\'s culture', 'src/Kronikol.StepTracking/StepWeaver.cs',
     'text = char.ToUpperInvariant(text[0]) + text.Substring(1).ToLowerInvariant();',
     'text = char.ToUpper(text[0]) + text.Substring(1).ToLowerInvariant();',
     WEAVER, 'HumanizeMethodName_reads_the_same', 1),
    ('MB11 a SQL value takes the machine\'s culture', 'src/Kronikol/Sql/SqlRowFormatter.cs',
     'IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),',
     'IFormattable formattable => formattable.ToString() ?? "",',
     CORE, 'SqlValueTextTests', 1),
    ('MB12 a SQL date is written in the invariant calendar form, not as a row writes it', 'src/Kronikol/Sql/SqlRowFormatter.cs',
     '        DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan => JsonSerializer.Serialize(value, value.GetType(), JsonOptions).Trim(\'"\'),\n', '',
     CORE, 'SqlValueTextTests', 1),
    ('MB13 the SQLite scalar takes the machine\'s culture', 'src/Kronikol.Extensions.Sqlite/TrackingSqliteCommand.cs',
     'var str = SqlRowFormatter.ValueText(result);', 'var str = result.ToString() ?? "";',
     SQLITE, 'A_scalar_and_a_parameter_read_as_a_row_does_on_every_machine', 1),
    ('MB14 a SQLite parameter takes the machine\'s culture', 'src/Kronikol.Extensions.Sqlite/TrackingSqliteCommand.cs',
     '.Select(p => $"{p.ParameterName}={(p.Value is null or DBNull ? "" : SqlRowFormatter.ValueText(p.Value))}"));',
     '.Select(p => $"{p.ParameterName}={p.Value}"));',
     SQLITE, 'A_scalar_and_a_parameter_read_as_a_row_does_on_every_machine', 1),
    ('MB15 a Redis double takes the machine\'s culture', 'src/Kronikol.Extensions.Redis/RedisTrackingDatabase.cs',
     'double d => d.ToString(CultureInfo.InvariantCulture),', 'double d => d.ToString(),',
     REDIS, 'A_number_the_server_answers_reads_the_same_on_every_machine', 1),
    ('MB16 a Redis long takes the machine\'s minus sign', 'src/Kronikol.Extensions.Redis/RedisTrackingDatabase.cs',
     'long l => l.ToString(CultureInfo.InvariantCulture),', 'long l => l.ToString(),',
     REDIS, 'A_number_the_server_answers_reads_the_same_on_every_machine', 1),
    ('MB17 a Spanner row count takes the machine\'s minus sign', 'src/Kronikol.Extensions.Spanner/TrackingSpannerCommand.cs',
     '$"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected"', '$"{rowsAffected.Value} rows affected"',
     CORE, 'A_value_a_database_capture_writes_as_text_does_not_take_the_machines_culture', 1),
    ('MB18 a copy\'s expired calls are kept', 'src/Kronikol/Reports/Merge/MergeableReportMerger.cs',
     '.Where(i => !duplicateIds.Contains(i.TestId) && !(i.ExpiredFromTestId is { } from && duplicateIds.Contains(from)))',
     '.Where(i => !duplicateIds.Contains(i.TestId))',
     CORE, 'A_copied_scenarios_expired_calls_are_kept_once', 1),
    ('MB19 the parent patterns follow the machine\'s casing', 'src/Kronikol.ReqNRoll.Core/ExampleValueGrouper.cs',
     '            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);', '            RegexOptions.IgnoreCase);',
     CORE, 'A_step_in_capitals_groups_the_same', 2),
    ('MB20 the lint skips a verbatim interpolation', 'tests/Kronikol.Tests/CultureInvariantSourceTests.cs',
     ' || statement.Contains("$@\\"", StringComparison.Ordinal)', '',
     CORE, 'CultureInvariantSourceTests.The_rule_finds_what_it_is_for', 1),
    ('MB21 the lint skips a composite format', 'tests/Kronikol.Tests/CultureInvariantSourceTests.cs',
     ' && !CompositeFormat.IsMatch(statement)', '',
     CORE, 'CultureInvariantSourceTests.The_rule_finds_what_it_is_for', 1),
    ('MB22 the lint reads F and not f in a hole', 'tests/Kronikol.Tests/CultureInvariantSourceTests.cs',
     r'+?:(?<format>[Nn]\d+|[Ff][1-9]\d*', r'+?:(?<format>[Nn]\d+|F[1-9]\d*',
     CORE, 'CultureInvariantSourceTests.The_rule_finds_what_it_is_for', 1),
    ('MB23 the lint reads N and not n in a ToString', 'tests/Kronikol.Tests/CultureInvariantSourceTests.cs',
     r'ToString\(""(?<format>[Nn]\d+', r'ToString\(""(?<format>N\d+',
     CORE, 'CultureInvariantSourceTests.The_rule_finds_what_it_is_for', 1),
    ('MB24 the capture lint misses a diagnostic tracker\'s parameter', 'tests/Kronikol.Tests/CultureInvariantSourceTests.cs',
     r'|\{val\}', '',
     CORE, 'CultureInvariantSourceTests.The_capture_rule_finds_what_it_is_for', 1),
    ('MB25 a numeric parameter is written back as a double', 'src/Kronikol/Query/ReportScanner.cs',
     ': reader.HasValueSequence ? Encoding.UTF8.GetString(reader.ValueSequence) : Encoding.UTF8.GetString(reader.ValueSpan);',
     ': reader.GetDouble().ToString();',
     CORE, 'A_report_scanned_outside_the_query_reads_a_numeric_parameter_as_written', 1),
]

only = set(sys.argv[1:])
built = set()
for name, path, old, new, project, test, occurrences in M:
    if only and name.split()[0] not in only:
        continue
    text = open(path, encoding='utf-8', newline='').read()
    if text.count(old) != occurrences:
        print(f'SKIPPED {name}: the code to mutate occurs {text.count(old)} times, not {occurrences}', flush=True)
        continue
    open(path, 'w', encoding='utf-8', newline='').write(text.replace(old, new))
    try:
        built.add(project)
        build = subprocess.run(['dotnet', 'build', project, '-v', 'q', '-nologo'], capture_output=True, text=True)
        if build.returncode != 0:
            errors = [l.strip() for l in build.stdout.splitlines() if ' error ' in l][:2]
            print(f'BUILD FAILED {name}: {errors}', flush=True)
            continue
        try:
            run = subprocess.run(['dotnet', 'test', project, '--no-build', '-v', 'q', '--filter', f'FullyQualifiedName~{test}'],
                                 capture_output=True, text=True, timeout=600)
        except subprocess.TimeoutExpired:
            print(f'TIMED OUT {name}  [{test}]', flush=True)
            continue
        summary = next((l.strip() for l in run.stdout.splitlines() if 'Failed:' in l and 'Passed:' in l), run.stdout[-300:])
        ran = 'Total:     0' not in summary and 'No test matches' not in run.stdout
        verdict = 'CAUGHT ' if run.returncode != 0 and ran else ('NO TESTS' if not ran else 'SURVIVED')
        print(f'{verdict} {name}  [{test}]  {summary}', flush=True)
    finally:
        subprocess.run(['git', 'checkout', '--', path], check=True)
for project in sorted(built):
    subprocess.run(['dotnet', 'build', project, '-v', 'q', '-nologo'], capture_output=True)
print('done; the tree is restored and rebuilt', flush=True)
