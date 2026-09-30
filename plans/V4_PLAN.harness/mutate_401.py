"""4.0.1 mutation checks (each distinct diagram and flame chart stored once, in a table at the end of the segment map):
break one guard at a time, run the facts that should catch it, restore.

python mutate_401.py <repo>   (prints one line per mutation: CAUGHT or SURVIVED)"""
import os
import subprocess
import sys

repo = sys.argv[1]
UNIT = ('tests/Kronikol.Tests/Kronikol.Tests.csproj', ['-f', 'net10.0'],
        'FullyQualifiedName~FlowStoredOnce|FullyQualifiedName~InternalFlowSegmentMapReportTests|'
        'FullyQualifiedName~InternalFlowHtmlGeneratorTests')
E2E = ('tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj', [], 'FullyQualifiedName~SharedFlowPopupTests')
HTML = 'src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs'
SCRIPT = 'src/Kronikol/Reports/internal-flow-popup-script.js'

MUTATIONS = [
    ('a diagram seen before gets a new place', HTML,
     'if (places.TryGetValue(identity, out var at))\n            return at;', '', UNIT),
    ('a flame chart takes the diagram\'s place', HTML,
     'holder["flameAt"] = PlaceOf(flameText, flameAt, flames, flame);',
     'holder["flameAt"] = PlaceOf(text, contentAt, contents, text);', UNIT),
    ('the table is named for the last segment, which a merge can meet in another shard\'s place', HTML,
     'table ??= "~" + key;', 'table = "~" + key;', UNIT),
    ('the table is not written', HTML,
     'if (contents.Count > 0)\n            stored[table!] = new { contents, flames };', '', UNIT),
    ('a holder loses its title', HTML,
     'if (title is not null)\n                holder["title"] = title;', '', UNIT),
    ('the popup does not look in the table', SCRIPT,
     'if (!segment || !(segment.sameAs || segment.table)) return segment;',
     'if (!segment || !segment.sameAs) return segment;', E2E),
    ('the popup reads the table off the segment, not its flow', SCRIPT,
     'var table = map[flow.table];', 'var table = map[segment.table];', E2E),
    ('the popup shows the diagram\'s place among the flame charts', SCRIPT,
     'flameData: flow.flameAt === undefined ? undefined : table.flames[flow.flameAt]',
     'flameData: flow.flameAt === undefined ? undefined : table.flames[flow.contentAt]', E2E),
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
