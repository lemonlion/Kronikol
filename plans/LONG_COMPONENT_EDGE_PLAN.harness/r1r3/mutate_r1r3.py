"""Mutations for plans/LONG_COMPONENT_EDGE_PLAN.md R1 and R3: each puts one behaviour back the way it was, or breaks it,
and the facts named must turn red. Run in a snapshot worktree of the release's working tree (never the tree itself):

    git worktree add --detach <dir> $(git stash create)
    python mutate_r1r3.py <dir> [M1 M2 ...]

Each mutation edits one file in place, runs its filter with dotnet test, and restores the file from memory.
"""
import os, re, subprocess, sys, json

ROOT = sys.argv[1]
PICK = set(sys.argv[2:])
CORE = ('FullyQualifiedName~ComponentDiagramGeneratorTests|FullyQualifiedName~PlantUmlStatementLengthTests'
        '|FullyQualifiedName~NodeJsPlantUmlRendererTests|FullyQualifiedName~ComponentDiagramReportTests'
        '|FullyQualifiedName~MergeableReportTests|FullyQualifiedName~ComponentDiagramNameWrappingTests'
        '|FullyQualifiedName~ComponentDiagramDifferTests')
E2E = 'FullyQualifiedName~LongStatementRenderingTests'
LIMITS = 'src/Kronikol/PlantUml/PlantUmlStatementLimits.cs'
GEN = 'src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs'
FLOW = 'src/Kronikol/InternalFlow/InternalFlowRenderer.cs'
SEQ = 'src/Kronikol/PlantUml/PlantUmlCreator.cs'
FETCH = 'src/Kronikol/DefaultDiagramsFetcher.cs'
SCRIPT = 'src/Kronikol/Reports/plantuml-browser-render-script.js'
MENU = 'src/Kronikol/Reports/DiagramContextMenu.cs'
DIFFER = 'src/Kronikol/ComponentDiagram/ComponentDiagramDiffer.cs'

MUTATIONS = [
    ('M1', 'the edge cap raised by half', LIMITS,
     'public const int MaxComponentEdgeLabelChars = 375;', 'public const int MaxComponentEdgeLabelChars = 562;', [CORE, E2E]),
    ('M2', 'the cut taken from the label\'s end, not its entries', GEN,
     'if (whole.Length <= cap || methods.Length == 0)', 'if (true)', [CORE]),
    ('M3', 'a formatter\'s label capped before it is wrapped', GEN,
     'label = CapLinkText(WrapLabel(options.RelationshipLabelFormatter(rel)), linkOpen);\n                label = PlantUml.PlantUmlStatementLimits.TruncateLabel(label, PlantUml.PlantUmlStatementLimits.MaxComponentEdgeLabelChars);',
     'label = CapLinkText(WrapLabel(PlantUml.PlantUmlStatementLimits.TruncateLabel(options.RelationshipLabelFormatter(rel), PlantUml.PlantUmlStatementLimits.MaxComponentEdgeLabelChars)), linkOpen);', [CORE]),
    ('M4', 'a formatter\'s label left uncapped', GEN,
     '                label = PlantUml.PlantUmlStatementLimits.TruncateLabel(label, PlantUml.PlantUmlStatementLimits.MaxComponentEdgeLabelChars);\n', '', [CORE]),
    ('M5', 'the stats lines dropped', GEN,
     '$"{linkOpen}{methodsPart}]]\\\\n{statsPart}{errorPart}\\\\n{rel.CallCount} calls across {rel.TestCount} tests"',
     '$"{linkOpen}{methodsPart}]]\\\\n{rel.CallCount} calls across {rel.TestCount} tests"', [CORE]),
    ('M6', 'the block-opener cap back at 1471', LIMITS,
     'public const int MaxBlockLabelChars = 600;', 'public const int MaxBlockLabelChars = 1471;', [CORE, E2E]),
    ('M7', 'the coloured-bar cap back at 1400', LIMITS,
     'public const int MaxColouredNoteBarChars = 600;', 'public const int MaxColouredNoteBarChars = 1400;', [CORE, E2E]),
    ('M8a', 'a component node\'s name uncapped', GEN,
     'Wrap(PlantUml.PlantUmlStatementLimits.CapName(name), MaxNameLineChars)', 'Wrap(name, MaxNameLineChars)', [CORE]),
    ('M8b', 'a sequence participant\'s name uncapped', SEQ,
     'DiagramWidth.Wrap(PlantUmlStatementLimits.CapName(name), DiagramWidth.MaxNameLineChars)', 'DiagramWidth.Wrap(name, DiagramWidth.MaxNameLineChars)', [CORE]),
    ('M9', 'a cut name\'s alias without the hash', LIMITS,
     'string.Concat(name.AsSpan(0, MaxParticipantNameChars - 9), "_", StableHash(name))', 'name[..MaxParticipantNameChars]', [CORE]),
    ('M10', 'the alias taken from the whole name', LIMITS,
     'name.Length <= MaxParticipantNameChars ? name : string.Concat', 'true ? name : string.Concat', [CORE]),
    ('M11', 'an activity action uncapped', FLOW,
     'var label = PlantUml.PlantUmlStatementLimits.TruncateLabel(\n            WrapForWidth(EscapePlantUml(node.Span.Name)), PlantUml.PlantUmlStatementLimits.MaxActivityActionChars);',
     'var label = WrapForWidth(EscapePlantUml(node.Span.Name));', [CORE]),
    ('M12', 'a swimlane uncapped', FLOW,
     'EscapePlantUml(PlantUml.PlantUmlStatementLimits.CapName(source))', 'EscapePlantUml(source)', [CORE]),
    ('M13', 'the placeholder note uncapped', FETCH,
     '+ PlantUml.PlantUmlStatementLimits.TruncateLabel(\n            EscapeNoteText($"\\u26a0 diagram could not be generated: {exception.GetType().Name}: {exception.Message}"),\n            PlantUml.PlantUmlStatementLimits.MaxNoteLineChars)',
     '+ EscapeNoteText($"\\u26a0 diagram could not be generated: {exception.GetType().Name}: {exception.Message}")', [CORE]),
    ('M14', 'the page\'s check keeps its own 1471', SCRIPT,
     'if (t.length > L.blockLabel) return { line: i + 1, kind: \'block label\', length: t.length, limit: L.blockLabel };',
     'if (t.length > 1471) return { line: i + 1, kind: \'block label\', length: t.length, limit: 1471 };', [E2E]),
    ('M15', 'the page\'s check without the edge cap', MENU,
     'componentEdgeLabel = PlantUml.PlantUmlStatementLimits.MaxComponentEdgeLabelChars,', 'componentEdgeLabel = PlantUml.PlantUmlStatementLimits.MaxMessageStatementChars,', [E2E]),
    ('M16', 'the diff diagram\'s names uncapped', DIFFER,
     'var shown = PlantUml.PlantUmlStatementLimits.CapName(participant);', 'var shown = participant;', [CORE]),
    ('M17', 'the diff diagram\'s aliases from the whole name', DIFFER,
     'SanitizeAliasRegex().Replace(PlantUml.PlantUmlStatementLimits.AliasSource(name).Camelize(), "_")',
     'SanitizeAliasRegex().Replace(name.Camelize(), "_")', [CORE]),
]

def run(filter_, project):
    cmd = ['dotnet', 'test', project, '--filter', filter_] + (['-f', 'net10.0'] if 'Kronikol.Tests.csproj' in project else [])
    p = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
    out = p.stdout + p.stderr
    failed = sorted(set(re.findall(r'^\s+Failed (\S+?)(?:\(.*?\))? \[', out, re.M)))
    summary = re.findall(r'(Passed!|Failed!)\s+- Failed:\s+(\d+), Passed:\s+(\d+)', out)
    if 'error CS' in out or not summary:
        return {'error': (re.findall(r'.*error CS.*', out) or [out[-600:]])[:3]}
    return {'summary': summary[-1], 'failed': [f.split('.')[-1] for f in failed]}

results = []
for key, what, rel, old, new, filters in MUTATIONS:
    if PICK and key not in PICK:
        continue
    path = os.path.join(ROOT, rel)
    original = open(path, 'rb').read()
    text = original.decode('utf-8')
    if text.count(old) != 1:
        results.append({'mutation': key, 'what': what, 'error': f'the site is in the file {text.count(old)} times'})
        print(json.dumps(results[-1]), flush=True)
        continue
    open(path, 'wb').write(text.replace(old, new).encode('utf-8'))
    try:
        row = {'mutation': key, 'what': what}
        for f in filters:
            project = 'tests/Kronikol.Tests.EndToEnd' if f == E2E else 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
            row['e2e' if f == E2E else 'core'] = run(f, project)
        row['killed'] = any(r.get('failed') for k, r in row.items() if k in ('core', 'e2e') and isinstance(r, dict))
        results.append(row)
        print(json.dumps(row), flush=True)
    finally:
        open(path, 'wb').write(original)

print('killed {} of {}'.format(sum(1 for r in results if r.get('killed')), len(results)))
