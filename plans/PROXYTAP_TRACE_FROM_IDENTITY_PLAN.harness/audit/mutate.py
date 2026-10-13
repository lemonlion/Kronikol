"""The #170 audit's mutations: each is applied to one file, the facts that should catch it are run, and the file is
put back from a copy held in memory (never git checkout, which would also revert uncommitted work). Run from the
repository root: python plans/PROXYTAP_TRACE_FROM_IDENTITY_PLAN.harness/audit/mutate.py > .../audit/mutations.txt"""
import pathlib
import re
import subprocess
import sys

TAP = 'src/Kronikol.Extensions.ProxyTap/ProxyTap.cs'
BUILDER = 'src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs'
TAP_TESTS = ['tests/Kronikol.Tests.ProxyTap', 'FullyQualifiedName~ProxyTapTraceContextTests']
CORE_TESTS = ['tests/Kronikol.Tests', 'FullyQualifiedName~InternalFlow|FullyQualifiedName~IngestSpan']

MUTATIONS = [
    ('D3: the tap client runs the process HttpClient instrumentation again', TAP,
     '            ActivityHeadersPropagator = null,\n', '', TAP_TESTS),
    ('D4: the accept loop starts under the caller activity again', TAP,
     '        var current = Activity.Current;\n        Activity.Current = null;\n',
     '        var current = Activity.Current;\n', TAP_TESTS),
    ('D4: StartAsync does not give the caller activity back', TAP,
     '            Activity.Current = current;\n', '', TAP_TESTS),
    ('D4: no clear in HandleAsync for a parent with no span id', TAP,
     '            if (parent != default && parent.SpanId == default)\n                Activity.Current = null;\n', '', TAP_TESTS),
    ('D5: the diagnostic leaves out "not all zero"', TAP,
     'did not apply to {notApplied:N0} request(s), because their test id is not a W3C trace id (32 lower-case hex digits, not all zero)',
     'did not apply to {notApplied:N0} request(s), because their test id is not a W3C trace id (32 lower-case hex digits)', TAP_TESTS),
    ('D5: the log line leaves out "not all zero"', TAP,
     'because the test id is not a W3C trace id (32 lower-case hex digits, not all zero); the request',
     'because the test id is not a W3C trace id (32 lower-case hex digits); the request', TAP_TESTS),
    ('D6: IsTraceId reads the length of a null id', TAP,
     '        if (id is not { Length: 32 })\n', '        if (id!.Length != 32)\n', TAP_TESTS),
    ('D12: no span tree in the nesting', BUILDER,
     'k >= 0 && outer is null && above is { Count: > 0 }; k--)', 'k >= 0 && outer is null && above is { Count: < 0 }; k--)', CORE_TESTS),
    ('D12: the ids above a call include its own span id', BUILDER,
     '            ids.Remove(spanId);\n', '            ids.Add(spanId);\n', CORE_TESTS),
    ('D12: tree evidence from a candidate in another trace', BUILDER,
     '                        && string.Equals(candidate.ActivityTraceId, entry.ActivityTraceId, StringComparison.Ordinal)\n                        && above.Contains(candidateSpan))',
     '                        && above.Contains(candidateSpan))', CORE_TESTS),
    ('D1: a call owns only the calls directly inside it', BUILDER,
     'at is not null && visited.Add(at); at = outerOf.GetValueOrDefault(at))', 'at is not null && visited.Add(at); at = null)', CORE_TESTS),
    ('D1: a call owns its inner calls of every trace', BUILDER,
     '                if (inner.ActivitySpanId is { } innerSpan && string.Equals(inner.ActivityTraceId, trace, StringComparison.Ordinal))',
     '                if (inner.ActivitySpanId is { } innerSpan)', CORE_TESTS),
    ('D1: a call does not own its own span id', BUILDER,
     'var own = new HashSet<string>(StringComparer.Ordinal) { call.ActivitySpanId };',
     'var own = new HashSet<string>(StringComparer.Ordinal);', CORE_TESTS),
]


def run(project, test_filter):
    done = subprocess.run(['dotnet', 'test', project, '--filter', test_filter], capture_output=True, text=True,
                          encoding='utf-8', errors='replace', timeout=1800)
    out = done.stdout + done.stderr
    if 'Build FAILED' in out or re.search(r': error CS\d+', out):
        return 'BUILD FAILED', []
    summary = re.findall(r'(Passed!|Failed!)\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)', out)
    failed = sorted(set(re.findall(r'^\s+Failed (\S+?)(?:\(| \[)', out, re.M)))
    return (summary[-1] if summary else 'no summary'), failed


# Arguments, when given, pick the mutations whose name holds one of them.
for name, path, old, new, (project, test_filter) in MUTATIONS:
    if sys.argv[1:] and not any(arg in name for arg in sys.argv[1:]):
        continue
    file = pathlib.Path(path)
    original = file.read_bytes()
    text = original.decode('utf-8')
    assert text.count(old) == 1, f'{name}: the text to mutate is not there exactly once'
    file.write_bytes(text.replace(old, new).encode('utf-8'))
    try:
        summary, failed = run(project, test_filter)
    finally:
        file.write_bytes(original)
    caught = isinstance(summary, tuple) and summary[0] == 'Failed!'
    print(f'{"CAUGHT  " if caught else "SURVIVED"} {name}')
    print(f'         {summary}')
    for test in failed:
        print(f'         - {test.rsplit(".", 1)[-1]}')
