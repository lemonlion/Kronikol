"""Mutations for plans/PROXY_TAP_TRACE_CONTEXT_PLAN.md (section 5): each puts one behaviour back the way it was, or breaks
it, and at least one fact must turn red. Run in a scratch worktree at the release commit (never the tree itself):

    git worktree add --detach <dir> <release commit>
    python3 mutate.py <dir> <out.jsonl> [M1 M2 ...]

Each mutation edits one file in place, builds and runs the whole ProxyTap test project (57 facts, about 10 s), and
restores the file from memory, so the existing facts get the same chance as the new ones to catch it.
"""
import json, os, re, subprocess, sys

ROOT, OUT = sys.argv[1], sys.argv[2]
PICK = set(sys.argv[3:])
TAP = 'src/Kronikol.Extensions.ProxyTap/ProxyTap.cs'
OPTIONS = 'src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs'
DI = 'src/Kronikol.Extensions.ProxyTap/ProxyTapServiceCollectionExtensions.cs'
PROJECT = 'tests/Kronikol.Tests.ProxyTap'

ASK = '(resolverAttributed && identity is { } attributed ? HostTraceparentFor(attributed) : null)'
NAMED_BRANCH = '        else if (parent is not null)\n        {\n            // The context the host named'

MUTATIONS = [
    ('M1', 'the host is never asked', TAP, '?? ' + ASK + ';', '?? null;'),
    ('M2', 'the server span ignores the context the host named', TAP,
     'server = parent is not null && ActivityContext.TryParse(parent.ToString(), null, isRemote: true, out var parentContext)',
     'server = inboundTraceparent is not null && ActivityContext.TryParse(inboundTraceparent.ToString(), null, isRemote: true, out var parentContext)'),
    ('M3', 'without a listener the named context is not forwarded', TAP, NAMED_BRANCH,
     NAMED_BRANCH.replace('(parent is not null)', '(parent is { TraceId: "never" })')),
    ('M4', 'the named context wins over an inbound traceparent', TAP,
     'var parent = inboundTraceparent\n                     ?? ' + ASK + ';',
     'var parent = ' + ASK + '\n                     ?? inboundTraceparent;'),
    ('M5', 'an identity read from the headers asks the host too', TAP, ASK,
     '(identity is { } attributed ? HostTraceparentFor(attributed) : null)'),
    ('M6', 'a traceparent the tap cannot read counts as one', TAP, ASK,
     '(resolverAttributed && request.Headers["traceparent"] is null && identity is { } attributed ? HostTraceparentFor(attributed) : null)'),
    ('M7', 'no validation', TAP,
     '    private static string? TraceContextProblem(TraceParent value)\n    {\n',
     '    private static string? TraceContextProblem(TraceParent value)\n    {\n        if (value is not null) return null;\n'),
    ('M8', 'upper-case hex accepted', TAP,
     "value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'))", 'value.All(Uri.IsHexDigit)'),
    ('M9', 'an all-zero trace id accepted', TAP,
     '        if (value.TraceId.All(c => c == \'0\'))\n            return "the trace id must not be all zeros";\n', ''),
    ('M10', 'an all-zero parent span id accepted', TAP,
     '        if (value.ParentSpanId.All(c => c == \'0\'))\n            return "the parent span id must not be all zeros";\n', ''),
    ('M11', 'a null trace id throws', TAP, 'value is not null && value.Length == length', 'value!.Length == length'),
    ('M12', 'a rejection is not counted', TAP,
     '        Interlocked.Increment(ref _resolvedIdentityTraceparentFailures);\n', ''),
    ('M13', 'a rejection is not in Diagnostics', TAP, 'if (traceparentFailures > 0)', 'if (traceparentFailures < 0)'),
    ('M14', 'a rejection is not logged', TAP,
     '        _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap ResolvedIdentityTraceparent {what} for test id {identity.Id}; the request is forwarded in a trace of its own.");\n',
     ''),
    ('M15', 'a host callback that throws is not caught', TAP,
     '            named = traceparentFor(identity);\n        }\n        catch (Exception ex)\n',
     '            named = traceparentFor(identity);\n        }\n        catch (Exception ex) when (ex is OutOfMemoryException)\n'),
    ('M16', 'a null answer counted as a rejection', TAP,
     '        if (named is null)\n            return null;\n',
     '        if (named is null)\n        {\n            RejectHostTraceparent(identity, "returned null");\n            return null;\n        }\n'),
    ('M17', 'SynthesizeTraceparent off withholds the named context', TAP, NAMED_BRANCH,
     NAMED_BRANCH.replace('(parent is not null)', '(parent is not null && _options.SynthesizeTraceparent)')),
    ('M18', 'the named flags are not kept', TAP, 'forwardedTraceparent = parent.ToString();',
     'forwardedTraceparent = TraceParent.Format(parent.TraceId, parent.ParentSpanId);'),
    ('M19', 'the captured entries keep a trace of their own', TAP,
     '            activityTraceId = parent.TraceId;\n            activitySpanId = parent.ParentSpanId;\n',
     '            activityTraceId = ActivityTraceId.CreateRandom().ToString();\n            activitySpanId = ActivitySpanId.CreateRandom().ToString();\n'),
    ('M20', 'the option is accepted without an IdentityResolver', OPTIONS,
     'if (ResolvedIdentityTraceparent is not null && IdentityResolver is null)',
     'if (ResolvedIdentityTraceparent is not null && IdentityResolver is null && ListenPort < 0)'),
    ('M21', 'an IdentityResolver that throws fails the request again', TAP,
     '                custom = resolver(headers, traceparent);\n            }\n            catch (Exception ex)\n',
     '                custom = resolver(headers, traceparent);\n            }\n            catch (Exception ex) when (ex is OutOfMemoryException)\n'),
    ('M22', 'a resolver failure is not counted', TAP,
     '                Interlocked.Increment(ref _identityResolverFailures);\n', ''),
    ('M23', 'a resolver failure is not in Diagnostics', TAP, 'if (resolverFailures > 0)', 'if (resolverFailures < 0)'),
    ('M24', 'a resolver failure is not logged', TAP,
     '                _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap IdentityResolver threw ({ex.GetType().Name}: {ex.Message}); the built-in rules attribute the request instead.");\n',
     '                _ = ex;\n'),
    ('M25', 'the list registration drops ResolvedIdentityTraceparent', DI,
     '        to.ResolvedIdentityTraceparent = from.ResolvedIdentityTraceparent;\n', ''),
    ('M26', 'the list registration drops InFlightRegistry', DI, '        to.InFlightRegistry = from.InFlightRegistry;\n', ''),
]


def run(args):
    p = subprocess.run(args, cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
    return p.returncode, p.stdout + p.stderr


def check():
    code, out = run(['dotnet', 'build', PROJECT, '-c', 'Release', '-nologo', '-v', 'q'])
    if code != 0:
        return {'error': sorted(set(re.findall(r'error CS\d+: [^\[]*', out)))[:3] or [out[-600:]]}
    code, out = run(['dotnet', 'test', PROJECT, '-c', 'Release', '--no-build'])
    summary = re.findall(r'(Passed!|Failed!)\s+- Failed:\s+(\d+), Passed:\s+(\d+)', out)
    failed = sorted(set(re.findall(r'^\s+Failed (Kronikol\..+?) \[[^\]]*\]\s*$', out, re.M)))
    if not summary:
        return {'error': [out[-600:]]}
    return {'summary': list(summary[-1]), 'failed': [f.replace('Kronikol.Tests.ProxyTap.', '') for f in failed]}


results = []
with open(OUT, 'w', encoding='utf-8') as log:
    for key, what, rel, old, new in MUTATIONS:
        if PICK and key not in PICK:
            continue
        path = os.path.join(ROOT, rel)
        original = open(path, 'rb').read()
        text = original.decode('utf-8')
        row = {'mutation': key, 'what': what}
        if text.count(old) != 1:
            row['error'] = f'the site is in {rel} {text.count(old)} times'
        else:
            open(path, 'wb').write(text.replace(old, new).encode('utf-8'))
            try:
                row.update(check())
            finally:
                open(path, 'wb').write(original)
        row['killed'] = bool(row.get('failed'))
        results.append(row)
        log.write(json.dumps(row) + '\n')
        log.flush()
        print(json.dumps(row), flush=True)

    restored = check()
    line = {'restored': restored, 'killed': sum(1 for r in results if r['killed']), 'of': len(results)}
    log.write(json.dumps(line) + '\n')
    print(json.dumps(line), flush=True)
