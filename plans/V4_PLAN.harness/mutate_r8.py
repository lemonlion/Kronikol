"""R8 mutation checks (4.0.0 defaults): put one 3.x default back at a time, run the facts that should catch it,
restore. The last mutation needs the Playwright project; the rest run the unit tests only.

python mutate_r8.py <repo>   (prints one line per mutation: CAUGHT or SURVIVED)"""
import os
import subprocess
import sys

repo = sys.argv[1]
OPTIONS = 'src/Kronikol/ReportConfigurationOptions.cs'
RESOLVER = 'src/Kronikol/Reports/ReportToggleDefaultsResolver.cs'
INGEST = 'src/Kronikol.Tool/IngestCommand.cs'
UNIT = ('tests/Kronikol.Tests/Kronikol.Tests.csproj', ['-f', 'net10.0'])
E2E = ('tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj', [])

MUTATIONS = [
    ('payloads written as text by default', OPTIONS, UNIT,
     'public bool CompressTestRunReportPayloads { get; set; } = true;',
     'public bool CompressTestRunReportPayloads { get; set; }',
     'FullyQualifiedName~PayloadCompressionTests|FullyQualifiedName~CompressedReportQueryTests|FullyQualifiedName~IngestCommandTests'),
    ('notes start as JSON by default', OPTIONS, UNIT,
     'public NotePayloadFormat NotePayloadFormat { get; set; } = NotePayloadFormat.Yaml;',
     'public NotePayloadFormat NotePayloadFormat { get; set; } = NotePayloadFormat.Json;',
     'FullyQualifiedName~ReportConfigurationOptionsDefaultsTests|FullyQualifiedName~IngestCommandTests'),
    ('the built-in note format is JSON', RESOLVER, UNIT,
     'public NotePayloadFormat NotePayloadFormat { get; init; } = NotePayloadFormat.Yaml;',
     'public NotePayloadFormat NotePayloadFormat { get; init; } = NotePayloadFormat.Json;',
     'FullyQualifiedName~ReportToggleDefaultsResolverTests|FullyQualifiedName~ToggleDefaultsMarkupTests|FullyQualifiedName~NoteFormatToggleScriptTests'),
    ('headers start shown', RESOLVER, UNIT,
     'public bool HeadersShown { get; init; }\n',
     'public bool HeadersShown { get; init; } = true;\n',
     'FullyQualifiedName~ReportToggleDefaultsResolverTests|FullyQualifiedName~ToggleDefaultsMarkupTests|FullyQualifiedName~DiagramContextMenuTests|FullyQualifiedName~IngestCommandTests'),
    ('ingest pins JSON notes', INGEST, UNIT,
     'Kronikol.Reports.NotePayloadFormat? notePayloadFormat = null;',
     'Kronikol.Reports.NotePayloadFormat? notePayloadFormat = Kronikol.Reports.NotePayloadFormat.Json;',
     'FullyQualifiedName~IngestCommandTests'),
    ('a page opens with headers shown (Playwright)', RESOLVER, E2E,
     'public bool HeadersShown { get; init; }\n',
     'public bool HeadersShown { get; init; } = true;\n',
     'FullyQualifiedName~ToggleDefaultsTests.With_nothing_configured|FullyQualifiedName~ReportToolbarTests.Headers_hidden_is_the_start_state'),
]

for name, rel, (project, extra), old, new, test_filter in MUTATIONS:
    path = os.path.join(repo, rel)
    original = open(path, encoding='utf-8', newline='').read()
    text = original.replace('\r\n', '\n')
    crlf = '\r\n' in original
    if text.count(old) != 1:
        print(f'{name}: MUTATION NOT APPLIED ({text.count(old)} matches)', flush=True)
        continue
    mutated = text.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(mutated.replace('\n', '\r\n') if crlf else mutated)
    try:
        run = subprocess.run(['dotnet', 'test', project, *extra, '--filter', test_filter], cwd=repo,
                             capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=2400)
        out = run.stdout + run.stderr
        failed = [l.strip() for l in out.splitlines() if l.strip().startswith('Failed Kronikol')]
        verdict = 'CAUGHT' if run.returncode != 0 and failed else ('BUILD FAILED' if 'error CS' in out else 'SURVIVED')
        print(f'{name}: {verdict} ({len(failed)} failed)' + (f'  e.g. {failed[0][:120]}' if failed else ''), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(original)
