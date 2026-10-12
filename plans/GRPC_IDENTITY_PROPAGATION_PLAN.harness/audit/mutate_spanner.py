# Mutation check of the #134 audit fixes: each mutation must turn at least one named fact red.
# Usage: python mutate.py <snapshot worktree> <output file>
import io, os, re, subprocess, sys, time

wt, out = sys.argv[1], sys.argv[2]
os.chdir(wt)

GRPC = 'tests/Kronikol.Tests.Grpc/Kronikol.Tests.Grpc.csproj'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
SPANNER = 'tests/Kronikol.Tests.Spanner/Kronikol.Tests.Spanner.csproj'
INT = 'src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs'
STORE = 'src/Kronikol/InternalFlow/InternalFlowSpanStore.cs'
HANDLER = 'src/Kronikol/Tracking/TestTrackingMessageHandler.cs'
PROXY = 'src/Kronikol/Tracking/TrackingProxy.cs'
DIAG = 'src/Kronikol/Reports/DiagnosticReportGenerator.cs'
SPAN_FMT = 'src/Kronikol.Extensions.Spanner/SpannerResponseFormatter.cs'

FINALLY_RESTORE = "        finally\n        {\n            Activity.Current = callerActivity;\n        }"
WITH_META = "context = WithCallMetadata(context, plan, activityTraceId, activitySpanId, recorded);"
STREAM_CATCH = ("            if (started is not null)\n                started.Failed(ex);\n            else\n"
                "                InternalFlowSpanStore.Complete(activity);")

# (id, what, file, old, new, occurrence or None for "exactly one", project, filter)
SP_TRACKER = 'src/Kronikol.Extensions.Spanner/SpannerTracker.cs'
SP_INT = 'src/Kronikol.Extensions.Spanner/SpannerTrackingInterceptor.cs'
SP_CMD = 'src/Kronikol.Extensions.Spanner/TrackingSpannerCommand.cs'
M = [
    ('S1', 'a statement\'s Detailed content ignores LogSqlText', SP_TRACKER,
     "            return (options.LogSqlText ? sql : null, sql);", "            return (sql, sql);", None,
     SPANNER, 'FullyQualifiedName~LogSqlText'),
    ('S2', 'the interceptor passes no Raw content for a unary call', SP_INT,
     "        var (reqId, traceId) = _tracker.LogRequest(opInfo, content, rawContent);",
     "        var (reqId, traceId) = _tracker.LogRequest(opInfo, content);", 0,
     SPANNER, 'FullyQualifiedName~LogSqlText'),
    ('S2b', 'the interceptor passes no Raw content for a streaming call', SP_INT,
     "        var (reqId, traceId) = _tracker.LogRequest(opInfo, content, rawContent);",
     "        var (reqId, traceId) = _tracker.LogRequest(opInfo, content);", 1,
     SPANNER, 'FullyQualifiedName~LogSqlText'),
    ('S3', 'the server observer passes no Raw content', SP_TRACKER,
     "            var (reqId, traceId) = tracker.LogRequest(opInfo, content, rawContent);",
     "            var (reqId, traceId) = tracker.LogRequest(opInfo, content);", None,
     SPANNER, 'FullyQualifiedName~LogSqlText'),
    ('S4', 'request content is computed at the base verbosity again', SP_TRACKER,
     "    internal static (string? Content, string? RawContent) RequestContent(object? request, SpannerTrackingOptions options)\n    {\n",
     "    internal static (string? Content, string? RawContent) RequestContent(object? request, SpannerTrackingOptions options)\n    {\n        if (options.Verbosity == SpannerTrackingVerbosity.Summarised) return (null, null);\n", None,
     SPANNER, 'FullyQualifiedName~A_phase_whose_verbosity'),
    ('S5', 'the command picks its content from the base verbosity again', SP_CMD,
     "        var (reqId, traceId) = _connection.Tracker.LogRequest(op, detailedContent, rawContent);",
     "        var (reqId, traceId) = _connection.Tracker.LogRequest(op, _options.Verbosity == SpannerTrackingVerbosity.Raw ? rawContent : detailedContent, rawContent);", None,
     SPANNER, 'FullyQualifiedName~TrackingSpannerCommandContentTests'),
    ('S6', 'the server observer ignores a failed status', SP_TRACKER,
     "            if (statusCode is { } code && code != StatusCode.OK)",
     "            if (statusCode is { } code && code != StatusCode.OK && false)", None,
     SPANNER, 'FullyQualifiedName~OnResponse_with_a_failed_status'),
]
def read(p): return io.open(p, encoding='utf-8', newline='').read()
def write(p, s): io.open(p, 'w', encoding='utf-8', newline='').write(s)

def mutate(s, old, new, occ):
    n = s.count(old)
    if occ is None:
        assert n == 1, ('anchor count', n)
        return s.replace(old, new)
    idx = -1
    for _ in range(occ + 1):
        idx = s.index(old, idx + 1)
    return s[:idx] + new + s[idx + len(old):]

def run(cmd):
    p = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    return p.returncode, p.stdout + p.stderr

with io.open(out, 'w', encoding='utf-8', newline='\n') as log:
    log.write('Mutations of the #134 audit Spanner fixes, run on the snapshot ' + subprocess.run(['git', 'rev-parse', '--short', 'HEAD'], capture_output=True, text=True).stdout.strip() + '\n\n')
    killed = 0
    for mid, what, path, old, new, occ, proj, flt in M:
        original = read(path)
        t0 = time.time()
        try:
            write(path, mutate(original, old, new, occ))
            rc, b = run(['dotnet', 'build', proj, '-c', 'Debug', '-v', 'q', '-nologo'])
            if rc != 0:
                errs = [l for l in b.splitlines() if ' error ' in l][:2]
                verdict, detail = 'BUILD FAILED', ' | '.join(errs)
            else:
                rc, t = run(['dotnet', 'test', proj, '-c', 'Debug', '--no-build', '--filter', flt])
                fails = sorted(set(re.sub(r'^\s*\[xUnit\.net [^\]]*\]\s*', '', l).replace(' [FAIL]', '').strip()
                                   for l in t.splitlines() if '[FAIL]' in l))
                summary = [l.strip() for l in t.splitlines() if re.search(r'(Passed|Failed)!  -', l)]
                if fails:
                    verdict, detail = 'KILLED', f'{len(fails)} red, e.g. ' + fails[0].split('.')[-1][:120]
                    killed += 1
                else:
                    verdict, detail = 'SURVIVED', ' '.join(summary) or t[-300:]
        finally:
            write(path, original)
        log.write(f'{mid} {verdict}: {what} ({time.time() - t0:.0f} s)\n    {detail}\n')
        log.flush()
    log.write(f'\n{killed} of {len(M)} killed\n')
    # Leave the snapshot built as it was.
    for proj in (SPANNER,):
        run(['dotnet', 'build', proj, '-c', 'Debug', '-v', 'q', '-nologo'])
print('done')
