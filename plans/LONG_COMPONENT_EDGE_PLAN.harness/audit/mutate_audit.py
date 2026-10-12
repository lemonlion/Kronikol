# Mutations for the audit of LONG_COMPONENT_EDGE_PLAN (plan §11, 2026-10-12): each puts one fix back as it was, or
# breaks the target of one new fact, rebuilds, runs the facts that should catch it, and reverts the file from git.
# Run it from a clean, committed tree:   python mutate_audit.py core|e2e [ids...]
import json, subprocess, sys, pathlib

WT = pathlib.Path(__file__).resolve().parents[3]
OUT = pathlib.Path(__file__).with_name('mutations.jsonl')

CORE = [
    ('A1', 'TruncateLabel cuts between the halves of a pair again', 'src/Kronikol/PlantUml/PlantUmlStatementLimits.cs',
     "        if (cut > 0 && char.IsHighSurrogate(label[cut - 1]))\n            cut--;\n", "",
     'A_cut_never_splits_a_character|A_name_or_action_written_with_emoji'),
    ('A2', "DiagramWidth's hard break splits a pair again", 'src/Kronikol/PlantUml/DiagramWidth.cs',
     "                if (length > 1 && at + length < atom.Length && char.IsHighSurrogate(atom[at + length - 1]))\n                    length--;\n", "",
     'A_hard_break_never_splits|A_name_or_action_written_with_emoji'),
    ('A3', 'ChunksUpTo splits a pair again', 'src/Kronikol/Extensions/StringExtensions.cs',
     "                if (length > 1 && at + length < value.Length && char.IsHighSurrogate(value[at + length - 1]))\n                    length--;\n", "",
     'A_value_chunked_for_a_note|A_payload_written_with_emoji'),
    ('A4', "WrapUnbreakableRuns' cut splits a pair again", 'src/Kronikol/PlantUml/PlantUmlCreator.cs',
     "        if (cut > pos + 1 && char.IsHighSurrogate(run[cut - 1]))\n            cut--;\n", "",
     'A_value_chunked_for_a_note|A_payload_written_with_emoji'),
    ('A5', "ChunksUpTo's argument checks deferred to enumeration", 'src/Kronikol/Extensions/StringExtensions.cs',
     "        ArgumentNullException.ThrowIfNull(value);\n        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkLength);\n        return Chunks(value, chunkLength);\n",
     "        return Chunks(value, chunkLength);\n",
     'A_value_chunked_for_a_note'),
    ('A6', 'a name in another script keeps the bare alias', 'src/Kronikol/PlantUml/PlantUmlStatementLimits.cs',
     "name.Length <= MaxParticipantNameChars && System.Text.Ascii.IsValid(name)", "name.Length <= MaxParticipantNameChars",
     'Two_names_written_in_another_script|A_name_written_in_ascii_keeps_its_alias'),
    ('A7', 'every name takes the hash', 'src/Kronikol/PlantUml/PlantUmlStatementLimits.cs',
     "name.Length <= MaxParticipantNameChars && System.Text.Ascii.IsValid(name)", "false",
     'A_name_written_in_ascii_keeps_its_alias|A_name_that_fits_keeps_its_alias_as_it_was'),
    ('A8', "a merged report ignores the report's colours", 'src/Kronikol/Reports/Merge/MergeableReportRenderer.cs',
     "            componentOptions.DependencyColors ??= options.DependencyColors;\n", "",
     'A_merged_reports_component_edges_take_the_reports_dependency_colours'),
    ('A9', "the Node message reads the line after the one the engine named", 'src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs',
     "line >= 1 && line <= lines.Length\n            ? $\" on line {line}, a statement of {lines[line - 1].Trim().Length} characters\"",
     "line >= 1 && line < lines.Length\n            ? $\" on line {line}, a statement of {lines[line].Trim().Length} characters\"",
     'A_stack_failure_names_the_line|A_stack_failure_with_no_line'),
    ('A10', 'the Node message counts blank lines before the source', 'src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs',
     'var lines = plantUml.Replace("\\r\\n", "\\n").Trim().Split(\'\\n\');', 'var lines = plantUml.Replace("\\r\\n", "\\n").Split(\'\\n\');',
     'A_stack_failure_names_the_line'),
    ('A11', "an edge's methods sorted by the machine's culture again", 'src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs',
     'rel.Methods.OrderBy(m => m, StringComparer.Ordinal)', 'rel.Methods.OrderBy(m => m)',
     'An_edges_entries_are_listed_in_one_order|A_cut_list_keeps_the_same_entries'),
    ('A12', "the mergeable file's methods sorted by the machine's culture again", 'src/Kronikol/Reports/ReportGenerator.cs',
     'r.Methods.OrderBy(m => m, StringComparer.Ordinal)', 'r.Methods.OrderBy(m => m)',
     'A_relationships_methods_are_written_in_one_order'),
]

E2E = [
    ('B1', 'the popup describes only a stack failure', 'src/Kronikol/Reports/internal-flow-popup-script.js',
     "if (el.querySelector('svg') || (window._isStackOverflow && window._isStackOverflow(el.innerHTML))) {",
     "if (window._isStackOverflow && window._isStackOverflow(el.innerHTML)) {",
     'A_popup_diagram_the_engine_cannot_parse'),
    ('B2', 'every kind read out with "a"', 'src/Kronikol/Reports/plantuml-browser-render-script.js',
     "function withArticle(kind) { return (/^[aeiou]/i.test(kind) ? 'an ' : 'a ') + kind; }",
     "function withArticle(kind) { return 'a ' + kind; }",
     'A_popup_diagram_the_engine_cannot_parse'),
    ('B3', "the probe's verdict counts any SVG as drawn (3.30.4's)", 'tools/render-bench/statement-limits-worker-probe.js',
     "  if (svg) {\n    var texts", "  if (svg) { return { v: 'drawn' };\n    var texts",
     'The_worker_probes_verdict_rejects_every_failure'),
    ('B4', "the probe's verdict without the engine's first line", 'tools/render-bench/statement-limits-worker-probe.js',
     " || first.indexOf('PlantUML ') === 0) return { v: 'ERROR-PICTURE'", ") return { v: 'ERROR-PICTURE'",
     'The_worker_probes_verdict_rejects_every_failure'),
    ('B5', "the probe's verdict without 'An error has occurred'", 'tools/render-bench/statement-limits-worker-probe.js',
     "/Syntax Error\\\\?|An error has occurred/i", "/Syntax Error\\\\?/i",
     'The_worker_probes_verdict_rejects_every_failure'),
    ('B6', "the YAML view's break splits a pair again", 'src/Kronikol/Reports/collapsible-notes-script.js',
     "                if (cut > 1 && unit >= 0xD800 && unit <= 0xDBFF) cut--;\n", "",
     'Yaml_default_breaks_a_long_run_of_emoji'),
    ('B7', "the diff diagram's names uncut", 'src/Kronikol/ComponentDiagram/ComponentDiagramDiffer.cs',
     "var shown = PlantUml.PlantUmlStatementLimits.CapName(participant);", "var shown = participant;",
     'A_statement_past_its_old_limit_draws_in_the_worker'),
    ('B8', "the Node detector reads the whole picture (parity with the page's)", 'src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs',
     "return StackOverflowRegex().IsMatch(texts[^1].Value);", "return StackOverflowRegex().IsMatch(result);",
     'The_stack_detector_tells_the_engines_stack_failure_from_a_drawn_diagram'),
]


def run(cmd):
    return subprocess.run(cmd, cwd=WT, capture_output=True, text=True, encoding='utf-8', errors='replace')


def mutate(kind, mid, what, path, old, new, facts):
    p = WT / path
    t = p.read_bytes().decode('utf-8')
    assert t.count(old) == 1, (mid, path, t.count(old))
    p.write_bytes(t.replace(old, new).encode('utf-8'))
    try:
        proj = 'tests/Kronikol.Tests/Kronikol.Tests.csproj' if kind == 'core' else 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
        b = run(['dotnet', 'build', proj, '-c', 'Debug', '-v', 'q', '-nologo', '-nodeReuse:false', '-p:UseSharedCompilation=false'])
        if b.returncode != 0:
            return {'mutation': mid, 'what': what, 'build': 'FAILED', 'killed': True, 'tail': b.stdout[-600:]}
        flt = '|'.join('FullyQualifiedName~' + f for f in facts.split('|'))
        r = run(['dotnet', 'test', proj, '--no-build', '-nologo', '--filter', flt])
        lines = r.stdout.splitlines()
        failed = sorted({l.strip().split(' ')[1].split('.')[-1].split('(')[0] for l in lines if l.strip().startswith('Failed ') and '[' in l})
        summary = [l.strip() for l in lines if 'Passed!' in l or 'Failed!' in l]
        total = [l for l in lines if 'Total tests' in l or 'No test matches' in l]
        return {'mutation': mid, 'what': what, 'failed': failed, 'summary': summary, 'killed': bool(failed), 'note': total[:1]}
    finally:
        run(['git', 'checkout', '--', path])


if __name__ == '__main__':
    kind = sys.argv[1]
    only = set(sys.argv[2:])
    for m in (CORE if kind == 'core' else E2E):
        if only and m[0] not in only:
            continue
        res = mutate(kind, *m)
        print(json.dumps(res), flush=True)
        with OUT.open('a', encoding='utf-8') as f:
            f.write(json.dumps(res) + '\n')
    # The tree is back as committed: rebuild so later runs do not test a mutant's binaries.
    print('restore build:', run(['dotnet', 'build', 'tests/Kronikol.Tests/Kronikol.Tests.csproj' if kind == 'core' else 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj', '-c', 'Debug', '-v', 'q', '-nologo']).returncode)
