# Mutations of #101's R1 (plans/OUTLINE_ROW_NAMES_PLAN.md 4.5): each undoes one part of the fix and must turn the fact it
# names red.   python -I mutate-101.py <worktree> <log> [name ...]
# A mutation is one or more exact replacements. The test project is built for net10.0 with its references, the facts run,
# and every file is put back. KILLED means a named fact went red; KILLED-OTHER that only facts it does not name did.
import os
import shutil
import subprocess
import sys
import time

root, log = sys.argv[1], sys.argv[2]
only = set(sys.argv[3:])

EBR = 'src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs'
HK = 'src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs'
CV = 'src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs'
SID = 'src/Kronikol/Reports/ScenarioStableId.cs'
ST = 'src/Kronikol/Reports/StepText.cs'
MR = 'src/Kronikol/Reports/Merge/MergeableReportReader.cs'
MM = 'src/Kronikol/Reports/Merge/MergeableReportMerger.cs'
RG = 'src/Kronikol/Reports/ReportGenerator.cs'
SMD = 'src/Kronikol/Reports/SpecificationsMarkdownGenerator.cs'
HRB = 'src/Kronikol/History/HistoryRunBuilder.cs'
CTRF = 'src/Kronikol/Reports/CtrfReportGenerator.cs'
FDG = 'src/Kronikol/Reports/FailuresDigestGenerator.cs'
LABS = 'src/Kronikol/Reports/LabsReportGenerator.cs'
RSCW = 'src/Kronikol/Reports/RunSummaryConsoleWriter.cs'

FILTER = ('FullyQualifiedName~Kronikol.Tests.ReqNRoll.OutlineRow|FullyQualifiedName~Kronikol.Tests.Reports.OutlineRow'
          '|FullyQualifiedName~Kronikol.Tests.Reports.Merge.OutlineRowMergeTests|FullyQualifiedName~QueryOutlineRowNameTests')

ONE_PASS = ('        var name = title;\n'
            '        foreach (DictionaryEntry entry in arguments)\n'
            '            name = name.Replace("<" + entry.Key + ">", entry.Value?.ToString() ?? "", StringComparison.Ordinal);\n')
ONE_PASS_MUTANT = ('        var name = System.Text.RegularExpressions.Regex.Replace(title, "<([^<>]*)>", m =>\n'
                   '            arguments.Contains(m.Groups[1].Value) ? arguments[m.Groups[1].Value]?.ToString() ?? "" : m.Value);\n')


def site(path, new, old_name):
    """A site put back on Compute over the display name."""
    return (path, new, old_name)


M = [
    # the name (N1-N7)
    ('pickle-no-row-check', ['A_pickle_index_that_points_at_another_row_never_gives_that_row_s_name'], [(EBR,
        '        && ArgumentValues(scenarioInfo) is { } argumentValues\n        && entry.CellValues.SequenceEqual(argumentValues, StringComparer.Ordinal)\n',
        '')]),
    ('pickle-no-title-check', ['A_pickle_of_another_outline_with_the_same_values_never_gives_its_name'], [(EBR,
        '        && string.Equals(entry.ScenarioName, scenarioInfo.Title, StringComparison.Ordinal)\n', '')]),
    ('one-pass', ['The_rule_fills_one_column_at_a_time_in_written_order', 'The_rule_names_every_row_as_Gherkin_s_own_compiler_does'],
        [(EBR, ONE_PASS, ONE_PASS_MUTANT)]),
    ('case-insensitive', ['The_rule_fills_one_column_at_a_time_in_written_order'], [(EBR,
        'entry.Value?.ToString() ?? "", StringComparison.Ordinal);', 'entry.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);')]),
    ('no-fallback', ['Without_messages_the_row_is_named_by_Gherkin_s_rule'], [(EBR,
        '        return FillPlaceholders(title, arguments);\n    }\n', '        return title;\n    }\n')]),
    ('empty-pickle-name-taken', ['An_empty_pickle_name_falls_back_to_the_rule'], [(EBR,
        'PickleName(LookupCache.GetValue(featureInfo, BuildLookup), scenarioInfo) is { Length: > 0 } pickleName)',
        'PickleName(LookupCache.GetValue(featureInfo, BuildLookup), scenarioInfo) is { } pickleName)')]),
    ('identity-raw', ['The_identity_and_the_collected_row_carry_the_row_s_name'], [(HK,
        'ReqNRollTestContext.CurrentTestInfo = (rowName, scenarioId);',
        'ReqNRollTestContext.CurrentTestInfo = (_scenarioContext.ScenarioInfo.Title, scenarioId);')]),
    ('collected-row-unnamed', ['The_identity_and_the_collected_row_carry_the_row_s_name', 'A_plain_scenario_keeps_its_title_in_the_identity_and_the_row'], [(HK,
        '            RowName = _scenarioContext.TryGetValue(ReqNRollConstants.RowNameKey, out string? rowName)\n'
        '                ? rowName\n'
        '                : ExamplesBlockResolver.ResolveRowName(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo)\n',
        '            RowName = null\n')]),
    ('source-by-filled-name', ['A_placeholder_row_still_finds_its_source_line_and_its_block'], [(EBR,
        'scenarioInfo.Title is { Length: > 0 } title && lookup.LinesByScenarioName.TryGetValue(title, out var line)',
        'ResolveRowName(featureInfo, scenarioInfo) is { Length: > 0 } title && lookup.LinesByScenarioName.TryGetValue(title, out var line)')]),
    ('value-match-by-filled-name', ['A_placeholder_row_still_finds_its_source_line_and_its_block'], [(EBR,
        '.Where(e => string.Equals(e.ScenarioName, scenarioInfo.Title, StringComparison.Ordinal)',
        '.Where(e => string.Equals(e.ScenarioName, ResolveRowName(featureInfo, scenarioInfo), StringComparison.Ordinal)')]),
    # the mark and the key (K1-K4)
    ('mark-never-set', ['The_converter_names_a_row_by_its_row_name_and_marks_it', 'The_history_roster_keys_the_row_on_the_id_its_title_gave_it'], [(CV,
        'KeyedByOutline = x.OutlineId is not null && !string.Equals(displayName, x.OutlineId, StringComparison.Ordinal),',
        'KeyedByOutline = false,')]),
    ('mark-every-outline-row', ['A_row_named_by_its_title_is_not_marked_and_is_what_it_was'], [(CV,
        'KeyedByOutline = x.OutlineId is not null && !string.Equals(displayName, x.OutlineId, StringComparison.Ordinal),',
        'KeyedByOutline = x.OutlineId is not null,')]),
    ('key-ignores-mark', ['An_unmarked_row_whose_name_differs_from_its_outline_keys_on_its_name'], [(SID,
        'scenario is { KeyedByOutline: true, OutlineId: { } outlineId } ? outlineId : scenario.DisplayName;',
        'scenario.OutlineId ?? scenario.DisplayName;')]),
    ('key-not-recased', ['A_marked_row_keys_on_its_title_as_CapitaliseTitles_re_cased_it'], [(ST,
        '                scenario.OutlineId = Capitalise(scenario.OutlineId);\n', '')]),
    ('reader-drops-mark', ['The_merge_reads_the_mark_back_and_writes_the_shard_s_ids',
                           'A_row_from_a_shard_written_before_its_name_was_filled_and_one_written_after_are_one_scenario'], [(MR,
        'KeyedByOutline = se.TryGetProperty("keyedByOutline", out var keyedByOutline) && keyedByOutline.ValueKind == JsonValueKind.True,',
        'KeyedByOutline = false,')]),
    ('writer-drops-mark', ['The_data_file_marks_a_filled_row_and_no_other', 'The_merge_reads_the_mark_back_and_writes_the_shard_s_ids'], [(RG,
        '                if (s.KeyedByOutline)\n                    scenario = InsertAfter(scenario, "outlineId", "keyedByOutline", true);\n', '')]),
    # order and anchors (K5, K6)
    ('page-sort', ['The_page_keeps_the_rows_order_and_every_anchor'], [(RG,
        'var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(ScenarioStableId.KeyName).ToArray();',
        'var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName).ToArray();')]),
    ('yml-sort', ['Specifications_yml_keeps_the_adapter_s_order'], [(RG,
        'var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(ScenarioStableId.KeyName);',
        'var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName);')]),
    ('json-sort', ['Specifications_json_keeps_the_adapter_s_order'], [(RG,
        'Scenarios = f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(ScenarioStableId.KeyName).Select(s => new',
        'Scenarios = f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(s => s.DisplayName).Select(s => new')]),
    ('xml-sort', ['Specifications_xml_keeps_the_adapter_s_order'], [(RG,
        'f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(ScenarioStableId.KeyName).Select(s =>\n',
        'f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(s => s.DisplayName).Select(s =>\n')]),
    ('md-sort', ['Specifications_md_keeps_the_adapter_s_order'], [(SMD,
        '            .ThenBy(ScenarioStableId.KeyName)\n', '            .ThenBy(s => s.DisplayName)\n')]),
    ('adapter-sorts-by-row-name', ['The_converter_orders_rows_by_their_title_never_by_their_filled_names', 'The_data_file_keeps_the_adapter_s_order'], [(CV,
        '                        .ThenBy(x => x.ScenarioTitle)\n', '                        .ThenBy(x => x.RowName ?? x.ScenarioTitle)\n')]),
    ('slug-from-name', ['The_page_keeps_the_rows_order_and_every_anchor'], [(RG,
        'var baseAnchor = GenerateScenarioAnchorId(ScenarioStableId.KeyName(scenario));',
        'var baseAnchor = GenerateScenarioAnchorId(scenario.DisplayName);')]),
    # the 15 id sites (K1), each put back on Compute over the display name
    ('site-history-roster', ['The_history_roster_keys_the_row_on_the_id_its_title_gave_it'], [(HRB,
        'ScenarioStableId.For(suite, pair.Feature.DisplayName, pair.Scenario)',
        'ScenarioStableId.Compute(suite, pair.Feature.DisplayName, pair.Scenario.DisplayName, pair.Scenario.OutlineId, pair.Scenario.ExampleValues)')]),
    ('site-ctrf', ['The_CTRF_report_names_the_row_and_writes_the_id_its_title_gave_it'], [(CTRF,
        'ScenarioStableId.For(suite, feature.DisplayName, scenario)',
        'ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues)')]),
    ('site-digest', ['Failures_md_heads_the_row_with_its_name_and_keeps_its_example_row_and_its_id',
                     'Failures_jsonl_names_the_row_and_writes_the_id_its_title_gave_it'], [(FDG,
        'ScenarioStableId.For(suite, located.Feature.DisplayName, scenario)',
        'ScenarioStableId.Compute(suite, located.Feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues)')]),
    ('site-labs', ['The_labs_page_links_the_row_by_the_id_its_title_gave_it'], [(LABS,
        'ScenarioStableId.For(suite, feature.DisplayName, scenario)',
        'ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues)')]),
    ('site-merge-identity', ['A_row_from_a_shard_written_before_its_name_was_filled_and_one_written_after_are_one_scenario'], [(MM,
        'ScenarioStableId.For(suite, feature.DisplayName, scenario)',
        'ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName ?? "", scenario.OutlineId, scenario.ExampleValues)')]),
    ('site-console', ['The_console_summary_names_the_row_and_writes_the_id_its_title_gave_it'], [(RSCW,
        'ScenarioStableId.For(suite, feature.DisplayName, scenario)',
        'ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues)')]),
    ('site-timeline', ['The_timeline_names_the_row_and_joins_it_by_the_id_its_title_gave_it'], [(RG,
        'ScenarioStableId.For(suite, featureName, scenario)',
        'ScenarioStableId.Compute(suite, featureName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues)')]),
    ('site-scenario-element', ['A_marked_scenario_shown_on_its_own_carries_the_id_its_title_gave_it'], [(RG,
        'var scenarioStableId = ScenarioStableId.For(suite, feature.DisplayName, scenario);',
        'var scenarioStableId = ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues);')]),
    ('site-component-model', ['The_component_diagram_s_model_lists_the_row_by_the_id_its_title_gave_it'], [(RG,
        'stableIds.Add(ScenarioStableId.For(suite, feature.DisplayName, scenario));',
        'stableIds.Add(ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues));')]),
    ('site-row-history', ['The_outline_s_row_reads_its_history_by_the_id_its_title_gave_it'], [(RG,
        'var rowId = ScenarioStableId.For(suite, featureDisplayName ?? "", row);',
        'var rowId = ScenarioStableId.Compute(suite, featureDisplayName ?? "", row.DisplayName, row.OutlineId, row.ExampleValues);')]),
    ('site-flat-row', ['The_outline_s_flat_row_carries_the_id_its_title_gave_it'], [(RG,
        'var flatRowStableId = ScenarioStableId.For(suite, featureDisplayName ?? "", s);',
        'var flatRowStableId = ScenarioStableId.Compute(suite, featureDisplayName ?? "", s.DisplayName, s.OutlineId, s.ExampleValues);')]),
    ('site-grouped-row', ['The_outline_s_row_carries_the_id_its_title_gave_it'], [(RG,
        'var rowStableId = ScenarioStableId.For(suite, featureDisplayName ?? "", s);',
        'var rowStableId = ScenarioStableId.Compute(suite, featureDisplayName ?? "", s.DisplayName, s.OutlineId, s.ExampleValues);')]),
    ('site-json-data', ['The_JSON_data_file_names_the_row_and_writes_the_id_its_title_gave_it', 'A_filled_row_s_data_file_id_is_the_id_its_title_gave_it'], [(RG,
        '["stableId"] = ScenarioStableId.For(suite, f.DisplayName, s),',
        '["stableId"] = ScenarioStableId.Compute(suite, f.DisplayName, s.DisplayName, s.OutlineId, s.ExampleValues),')]),
    ('site-xml-data', ['The_XML_data_file_names_the_row_and_writes_the_id_its_title_gave_it'], [(RG,
        'new XElement("StableId", ScenarioStableId.For(resolvedSuite, f.DisplayName, s)),',
        'new XElement("StableId", ScenarioStableId.Compute(resolvedSuite, f.DisplayName, s.DisplayName, s.OutlineId, s.ExampleValues)),')]),
    ('site-yaml-data', ['The_YAML_data_file_names_the_row_and_writes_the_id_its_title_gave_it'], [(RG,
        'ScenarioStableId.For(resolvedSuite, feature.DisplayName, scenario)',
        'ScenarioStableId.Compute(resolvedSuite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues)')]),
]


def run(cmd, timeout):
    try:
        done = subprocess.run(cmd, cwd=root, shell=True, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=timeout)
        return done.returncode, done.stdout + done.stderr
    except subprocess.TimeoutExpired as e:
        return 124, f'TIMEOUT {e}'


PROJECT = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
with open(log, 'w', encoding='utf-8') as out:
    for name, named, edits in M:
        if only and name not in only:
            continue
        staged = {}
        ok = True
        for rel, old, new in edits:
            full = os.path.join(root, rel)
            if full not in staged:
                staged[full] = open(full, 'rb').read()
            o, n = old.encode('utf-8'), new.encode('utf-8')
            if staged[full].count(o) != 1:
                out.write(f'{name}: PATTERN NOT FOUND ONCE in {rel} ({staged[full].count(o)})\n')
                ok = False
                break
            staged[full] = staged[full].replace(o, n)
        if not ok:
            out.flush()
            continue
        backups = {}
        try:
            for full, text in staged.items():
                backups[full] = full + '.mutation-backup'
                shutil.copyfile(full, backups[full])
                open(full, 'wb').write(text)
            started = time.time()
            code, text = run(f'dotnet build {PROJECT} -c Debug -f net10.0 -nologo -v q', 1800)
            if code != 0:
                out.write(f'{name}: BUILD FAILED\n{text[-1500:]}\n')
                out.flush()
                continue
            code, text = run(f'dotnet test {PROJECT} -f net10.0 --no-build -nologo --filter "{FILTER}"', 1500)
            failed = [line.strip() for line in text.splitlines() if line.strip().startswith('Failed Kronikol.')]
            summary = [line.strip() for line in text.splitlines() if 'Total:' in line]
            hit = [f for f in failed if any(n in f for n in named)]
            verdict = 'KILLED' if hit else ('KILLED-OTHER' if failed else ('SURVIVED' if summary else 'NO RUN'))
            out.write(f'{name}: {verdict} ({len(failed)} red, {len(hit)} named, {time.time() - started:.0f} s) {summary[-1] if summary else text[-300:]}\n')
            for line in failed[:8]:
                out.write(f'    {line}\n')
            out.flush()
        finally:
            for full, backup in backups.items():
                shutil.copyfile(backup, full)
                os.remove(backup)
    out.write('done\n')
