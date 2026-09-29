"""R6 mutation checks (segments no drawn arrow links are dropped): break one guard at a time, run the facts that should
catch it, restore.

python mutate_r6.py <repo>   (prints one line per mutation: CAUGHT or SURVIVED)"""
import os
import subprocess
import sys

repo = sys.argv[1]
FILTER = 'FullyQualifiedName~InternalFlowSegmentMapReportTests|FullyQualifiedName~FlowStoredOnce'
HTML = 'src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs'
REPORT = 'src/Kronikol/Reports/ReportGenerator.cs'

MUTATIONS = [
    ('the page keeps every segment', REPORT,
     'var linkedSegments = InternalFlowHtmlGenerator.LinkedSegments(perBoundarySegments, linkSources);',
     'var linkedSegments = perBoundarySegments;'),
    ('the data file keeps every segment', REPORT,
     'InternalFlowHtmlGenerator.LinkedSegments(perBoundarySegments, linkSources),', 'perBoundarySegments,'),
    ('the data file reads links from every test\'s diagrams', REPORT,
     'var linkSources = features.SelectMany(f => f.Scenarios)\n                .SelectMany(s => diagramLookup?[s.Id] ?? [])',
     'var linkSources = (diagramLookup ?? Enumerable.Empty<string>().ToLookup(x => x, x => x))\n                .SelectMany(g => g)'),
    ('the filter keeps a segment no link names', HTML,
     'return segments.Where(s => linked.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);',
     'return segments.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);'),
]

for name, rel, old, new in MUTATIONS:
    path = os.path.join(repo, rel)
    original = open(path, encoding='utf-8', newline='').read()
    if original.count(old) != 1:
        print(f'{name}: MUTATION NOT APPLIED ({original.count(old)} matches)', flush=True)
        continue
    open(path, 'w', encoding='utf-8', newline='').write(original.replace(old, new))
    try:
        run = subprocess.run(['dotnet', 'test', 'tests/Kronikol.Tests/Kronikol.Tests.csproj', '-f', 'net10.0',
                              '--filter', FILTER], cwd=repo, capture_output=True, text=True, encoding='utf-8',
                             errors='replace', timeout=1500)
        out = run.stdout + run.stderr
        failed = [l.strip() for l in out.splitlines() if l.strip().startswith('Failed Kronikol')]
        verdict = 'CAUGHT' if run.returncode != 0 and failed else ('BUILD FAILED' if 'error CS' in out else 'SURVIVED')
        print(f'{name}: {verdict} ({len(failed)} failed)' + (f'  e.g. {failed[0][:120]}' if failed else ''), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(original)
