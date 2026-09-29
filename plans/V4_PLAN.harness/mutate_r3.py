"""R3 mutation checks (#87, SPAN_ATTRIBUTION_PLAN.md): break one guard at a time, run the facts that should catch it,
restore.

python mutate_r3.py <repo>   (prints one line per mutation: CAUGHT or SURVIVED)"""
import os
import subprocess
import sys

repo = sys.argv[1]
FILTER = ('FullyQualifiedName~SpanAttributionTests|FullyQualifiedName~SpanAttributionThroughTheHandlerTests|'
          'FullyQualifiedName~InternalFlowSegmentBuilderTests|'
          'FullyQualifiedName~InternalFlowHtmlGeneratorTests|FullyQualifiedName~InternalFlowSegmentMapReportTests')
BUILDER = 'src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs'
HTML = 'src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs'

MUTATIONS = [
    ('an untraced call of a traceless test takes the whole run', BUILDER,
     ': testCalls.Length > 0 ? testSpans : unclaimed;', ': testCalls.Length > 0 ? testSpans : spans;'),
    ('a trace one test records is narrowed by the tree', BUILDER,
     'if (attribution.TestsByTrace[trace].Count > 1 && spanId is not null)',
     'if (attribution.TestsByTrace[trace].Count > 0 && spanId is not null)'),
    ('a shared trace is never narrowed', BUILDER,
     '                if (subtree.Count > 0)\n                    return subtree;', ''),
    ('an untraced call takes its test\'s whole traces', BUILDER,
     '.SelectMany(call => ByTraceAndTree(call.Trace, call.Span))',
     '.SelectMany(call => attribution.SpansByTrace.TryGetValue(call.Trace, out var all) ? all : [])'),
    ('a contested span stays', BUILDER,
     'var kept = selection.Spans.Where(s => testsBySpan[s].Count == 1).ToArray();',
     'var kept = selection.Spans.ToArray();'),
    ('calls of one test contest each other', BUILDER,
     'tests.Add(selection.Log.TestId);', 'tests.Add(selection.Key);'),
    ('nothing counted as left out', BUILDER,
     'SpansLeftOut = selection.Spans.Count - kept.Length', 'SpansLeftOut = 0'),
    ('the whole-test flow takes shared traces whole', BUILDER,
     'if (attribution.TestsByTrace[trace].Count == 1 && attribution.SpansByTrace',
     'if (attribution.TestsByTrace[trace].Count >= 1 && attribution.SpansByTrace'),
    ('a call that lost everything is hidden under HideLink', HTML,
     'else if (segment.SpansLeftOut > 0)', 'else if (false)'),
    ('the title does not count what was left out', HTML,
     '+ (segment.SpansLeftOut > 0 ? $", {segment.SpansLeftOut} left out)" : ")");', '+ ")";'),
    ('the popup does not say why', HTML,
     'content = LeftOutNote(segment) + content;', 'content = "" + content;'),
    ('the flame chart layouts drop the note', HTML,
     'content = LeftOutNote(segment) + content;', 'if (!showFlameChart) content = LeftOutNote(segment) + content;'),
    ('a configured source goes into the markup raw', HTML,
     '{System.Net.WebUtility.HtmlEncode(string.Join(", ", configuredActivitySources))}',
     '{string.Join(", ", configuredActivitySources)}'),
]

for name, rel, old, new in MUTATIONS:
    path = os.path.join(repo, rel)
    original = open(path, encoding='utf-8', newline='').read()
    if original.count(old) != 1:
        print(f'{name}: MUTATION NOT APPLIED ({original.count(old)} matches)')
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
