"""R5 mutation checks (#86, each distinct flow stored once): break one guard at a time, run the facts that should catch
it, restore.

python mutate_r5.py <repo>   (prints one line per mutation: CAUGHT or SURVIVED)"""
import os
import subprocess
import sys

repo = sys.argv[1]
UNIT = ('tests/Kronikol.Tests/Kronikol.Tests.csproj', ['-f', 'net10.0'],
        'FullyQualifiedName~FlowStoredOnce|FullyQualifiedName~InternalFlowSegmentMapReportTests|'
        'FullyQualifiedName~InternalFlowHtmlGeneratorTests')
E2E = ('tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj', [], 'FullyQualifiedName~SharedFlowPopupTests')
HTML = 'src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs'
REPORT = 'src/Kronikol/Reports/ReportGenerator.cs'
SCRIPT = 'src/Kronikol/Reports/internal-flow-popup-script.js'

MUTATIONS = [
    ('every segment keeps its own copy', HTML,
     'if (holders.TryAdd(flow, key))', 'if (holders.TryAdd(flow, key) || true)', UNIT),
    ('a later segment names itself', HTML,
     '? new { title = title.GetString(), sameAs = holders[flow] }', '? new { title = title.GetString(), sameAs = key }', UNIT),
    ('a named segment loses its title', HTML,
     '? new { title = title.GetString(), sameAs = holders[flow] }', '? new { sameAs = holders[flow] }', UNIT),
    ('the diagram element is named for the call', HTML,
     'var id = $"iflow-puml-{FlowHash(plantuml)}";', 'var id = $"iflow-puml-{segment.RequestResponseId}";', UNIT),
    ('the page stores every flow inline', REPORT,
     '                    InternalFlowHtmlGenerator.StoreFlowsOnce(InternalFlowHtmlGenerator.BuildSegmentData(\n                        perBoundarySegments,',
     '                    (InternalFlowHtmlGenerator.BuildSegmentData(\n                        perBoundarySegments,', UNIT),
    ('the data file stores every flow inline', REPORT,
     'internalFlowSegmentData = InternalFlowHtmlGenerator.StoreFlowsOnce(InternalFlowHtmlGenerator.BuildSegmentData(',
     'internalFlowSegmentData = (InternalFlowHtmlGenerator.BuildSegmentData(', UNIT),
    ('the popup does not follow the name', SCRIPT,
     'fill(popup, resolve(map, segmentId));', 'fill(popup, map[segmentId]);', E2E),
]

for name, rel, old, new, (project, extra, test_filter) in MUTATIONS:
    path = os.path.join(repo, rel)
    original = open(path, encoding='utf-8', newline='').read()
    if original.count(old) != 1:
        print(f'{name}: MUTATION NOT APPLIED ({original.count(old)} matches)', flush=True)
        continue
    open(path, 'w', encoding='utf-8', newline='').write(original.replace(old, new))
    try:
        run = subprocess.run(['dotnet', 'test', project, *extra, '--filter', test_filter], cwd=repo, capture_output=True,
                             text=True, encoding='utf-8', errors='replace', timeout=2400)
        out = run.stdout + run.stderr
        failed = [l.strip() for l in out.splitlines() if l.strip().startswith('Failed Kronikol')]
        verdict = 'CAUGHT' if run.returncode != 0 and failed else ('BUILD FAILED' if 'error CS' in out else 'SURVIVED')
        print(f'{name}: {verdict} ({len(failed)} failed)' + (f'  e.g. {failed[0][:120]}' if failed else ''), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(original)
