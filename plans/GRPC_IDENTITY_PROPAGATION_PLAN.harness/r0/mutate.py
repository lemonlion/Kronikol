# R0's mutations (plan section 5): each is applied alone to a scratch worktree at the release commit, the named facts
# are run, and the mutation must turn at least one of them red. The worktree is restored with `git checkout` after each
# one, so never point this at a worktree holding uncommitted work.
# Usage: PYTHONUTF8=1 python mutate.py <scratch worktree> <out file> [part of a mutation's name, to run that one alone]
import subprocess, sys, os

wt, out = sys.argv[1], sys.argv[2]
only = sys.argv[3] if len(sys.argv) > 3 else None
os.chdir(wt)

GRPC = 'src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs'
THV = 'src/Kronikol/Tracking/TrackingHeaderValue.cs'
DRG = 'src/Kronikol/Reports/DiagnosticReportGenerator.cs'

MUTATIONS = [
    ('write into the caller\'s Metadata again (T1)', GRPC,
     'var headers = new Metadata();\n        if (context.Options.Headers is { } callerHeaders)\n        {\n            foreach (var entry in callerHeaders)\n                headers.Add(entry);\n        }',
     'var headers = context.Options.Headers ?? new Metadata();',
     'tests/Kronikol.Tests.Grpc', 'FullyQualifiedName~CallMetadataTests'),
    ('drop the Activity.Current restore (T4)', GRPC,
     '            Activity.Current = callerActivity;\n', '',
     'tests/Kronikol.Tests.Grpc', 'FullyQualifiedName~CallMetadataTests'),
    ('hard-code the flags 00 (T6)', GRPC,
     '{(recorded ? "01" : "00")}', '00',
     'tests/Kronikol.Tests.Grpc', 'FullyQualifiedName~CallMetadataTests'),
    ('add a second traceparent when the caller set one (T3)', GRPC,
     '&& !HasEntry(headers, TraceParentKey)', '',
     'tests/Kronikol.Tests.Grpc', 'FullyQualifiedName~CallMetadataTests'),
    ('"?" in place of the encoding (T7, T9)', THV,
     'return Prefix + Uri.EscapeDataString(value);',
     'return new string(value.Select(c => c is >= (char)0x20 and <= (char)0x7E ? c : \'?\').ToArray());',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('leave surrounding spaces unencoded (T7, T9)', THV,
     "if (value.Length > 0 && (value[0] == ' ' || value[value.Length - 1] == ' '))\n            return false;", '',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('decode a value that lacks the prefix (T7)', THV,
     'if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))\n            return value;\n\n        return Uri.UnescapeDataString(value.Substring(Prefix.Length));',
     'if (value is null) return value;\n        return Uri.UnescapeDataString(value.StartsWith(Prefix, StringComparison.Ordinal) ? value.Substring(Prefix.Length) : value);',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('skip decoding: middleware (T10)', 'src/Kronikol/Tracking/TestTrackingContextMiddleware.cs',
     'TestIdentityScope.Begin(TrackingHeaderValue.Decode(name[0]!), TrackingHeaderValue.Decode(id[0]!))',
     'TestIdentityScope.Begin(name[0]!, id[0]!)',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('skip decoding: resolver (T10)', 'src/Kronikol/Tracking/TestInfoResolver.cs',
     'result = (TrackingHeaderValue.Decode(testName[0]!), TrackingHeaderValue.Decode(testId[0]!));',
     'result = (testName[0]!, testId[0]!);',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('skip decoding: handler inbound (T10)', 'src/Kronikol/Tracking/TestTrackingMessageHandler.cs',
     'new TestIdentity(TrackingHeaderValue.Decode(currentTestNameHeaders.First()!), TrackingHeaderValue.Decode(currentTestIdHeaders.First()!), AttributionSource.RequestHeader)',
     'new TestIdentity(currentTestNameHeaders.First()!, currentTestIdHeaders.First()!, AttributionSource.RequestHeader)',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('skip decoding: message tracker (T10)', 'src/Kronikol/Tracking/MessageTracker.cs',
     'TrackingHeaderValue.Decode(testNameValues.First()!),\n                    TrackingHeaderValue.Decode(testIdValues.First()!),',
     'testNameValues.First()!,\n                    testIdValues.First()!,',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('skip decoding: server bridge (T10)', 'src/Kronikol/Tracking/TestTrackingServerBridge.cs',
     'var name = TrackingHeaderValue.Decode(nameValues.FirstOrDefault());\n        var id = TrackingHeaderValue.Decode(idValues.FirstOrDefault());',
     'var name = nameValues.FirstOrDefault();\n        var id = idValues.FirstOrDefault();',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('skip decoding: ProxyTap (T10)', 'src/Kronikol.Extensions.ProxyTap/ProxyTap.cs',
     'var name = TrackingHeaderValue.Decode(FirstValue(headers, TestTrackingHttpHeaders.CurrentTestNameHeader));',
     'var name = FirstValue(headers, TestTrackingHttpHeaders.CurrentTestNameHeader);',
     'tests/Kronikol.Tests.ProxyTap', 'FullyQualifiedName~ProxyTapTests'),
    ('skip encoding: handler writes the raw name (T8, T9)', 'src/Kronikol/Tracking/TestTrackingMessageHandler.cs',
     'new[] { TrackingHeaderValue.Encode(currentTestInfo.Name) }', 'new[] { currentTestInfo.Name }',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('Guid.Parse for the inbound trace id again (T12)', 'src/Kronikol/Tracking/TestTrackingMessageHandler.cs',
     'hasTraceIdHeader && Guid.TryParse(traceIdHeaders.First(), out var inboundTraceId) ? inboundTraceId : Guid.NewGuid()',
     'hasTraceIdHeader ? Guid.Parse(traceIdHeaders.First()!) : Guid.NewGuid()',
     'tests/Kronikol.Tests', 'FullyQualifiedName~IdentityHeaderEncodingTests'),
    ('read instances[0] (T13, T14)', DRG,
     'var accessorStatus = AccessorCell(instances);',
     'var accessorStatus = instances[0].HasHttpContextAccessor ? AccessorCell([instances[0]]) : AccessorCell(instances);',
     'tests/Kronikol.Tests', 'FullyQualifiedName~DiagnosticReportGeneratorTests'),
]

def run(cmd):
    return subprocess.run(cmd, capture_output=True, text=True, shell=False)

with open(out, 'w', encoding='utf-8') as log:
    for name, path, old, new, project, flt in [m for m in MUTATIONS if only is None or only in m[0]]:
        src = open(path, encoding='utf-8').read()
        n = src.count(old)
        if n != 1:
            log.write(f'NOT APPLIED ({n} matches): {name}\n'); log.flush()
            continue
        open(path, 'w', encoding='utf-8', newline='\n').write(src.replace(old, new))
        build = run(['dotnet', 'build', project, '-v', 'q', '-nologo'])
        if build.returncode != 0:
            errs = [l for l in build.stdout.splitlines() if ' error ' in l][:2]
            log.write(f'BUILD FAILED: {name}: {errs}\n')
        else:
            test = run(['dotnet', 'test', project, '--no-build', '--filter', flt])
            failed = sorted({l.strip().split(' [')[0].replace('Failed ', '') for l in test.stdout.splitlines() if l.strip().startswith('Failed ')})
            summary = [l.strip() for l in test.stdout.splitlines() if 'Passed!' in l or 'Failed!' in l]
            verdict = 'KILLED' if failed else 'SURVIVED'
            log.write(f'{verdict}: {name}\n  {summary}\n')
            for f in failed[:12]:
                log.write(f'    red: {f}\n')
        log.flush()
        run(['git', 'checkout', '--', path])
    log.write('done\n')
