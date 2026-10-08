# R1's mutations (plan section 5): each is applied alone to a scratch worktree at the release commit, the named facts
# are run, and the mutation must turn at least one of them red. The worktree is restored with `git checkout` after each
# one, so never point this at a worktree holding uncommitted work.
# Usage: PYTHONUTF8=1 python mutate.py <scratch worktree> <out file> [part of a mutation's name, to run that one alone]
import subprocess, sys, os

wt, out = sys.argv[1], sys.argv[2]
only = sys.argv[3] if len(sys.argv) > 3 else None
os.chdir(wt)

GRPC = 'src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs'
S3 = 'src/Kronikol.Extensions.S3/S3TrackingMessageHandler.cs'
GT = ('tests/Kronikol.Tests.Grpc', 'FullyQualifiedName~IdentityPropagation|FullyQualifiedName~MultiHostPropagationTests')
PROPAGATES = '_options.PropagateTestIdentity && identity.Value.IsAttributed'

MUTATIONS = [
    ('propagate only when the phase is tracked (T28)', GRPC, PROPAGATES,
     PROPAGATES + ' && PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)', *GT),
    ('add the identity only when the inbound request lacks it, the HTTP rule (T21, T32)', GRPC, PROPAGATES,
     PROPAGATES + ' && _httpContextAccessor?.HttpContext?.Request.Headers.ContainsKey(TestTrackingHttpHeaders.CurrentTestIdHeader) != true', *GT),
    ('send the background identity (T24)', GRPC, PROPAGATES, '_options.PropagateTestIdentity', *GT),
    ('ignore PropagateTestIdentity (T22, T34)', GRPC, PROPAGATES, 'identity.Value.IsAttributed', *GT),
    ('a new GUID for the trace id (T26, T35)', GRPC, 'InboundTraceId() ?? Guid.NewGuid()', 'Guid.NewGuid()', *GT),
    ('overwrite the name the caller set (T25)', GRPC,
     'AddIfAbsent(headers, TestTrackingHttpHeaders.CurrentTestNameHeader, TrackingHeaderValue.Encode(plan.Identity.Name));',
     'headers.Add(TestTrackingHttpHeaders.CurrentTestNameHeader, TrackingHeaderValue.Encode(plan.Identity.Name));', *GT),
    ('overwrite the id the caller set (T25)', GRPC,
     'AddIfAbsent(headers, TestTrackingHttpHeaders.CurrentTestIdHeader, TrackingHeaderValue.Encode(plan.Identity.Id));',
     'headers.Add(TestTrackingHttpHeaders.CurrentTestIdHeader, TrackingHeaderValue.Encode(plan.Identity.Id));', *GT),
    ('overwrite the trace id the caller set (T25)', GRPC,
     'AddIfAbsent(headers, TestTrackingHttpHeaders.TraceIdHeader, plan.TraceId.ToString());',
     'headers.Add(TestTrackingHttpHeaders.TraceIdHeader, plan.TraceId.ToString());', *GT),
    ('overwrite the caller name the caller set (T25)', GRPC,
     'AddIfAbsent(headers, TestTrackingHttpHeaders.CallerNameHeader, TrackingHeaderValue.Encode(_options.CallerName));',
     'headers.Add(TestTrackingHttpHeaders.CallerNameHeader, TrackingHeaderValue.Encode(_options.CallerName));', *GT),
    ('send the name raw (T33)', GRPC, 'TrackingHeaderValue.Encode(plan.Identity.Name)', 'plan.Identity.Name', *GT),
    ('the interceptor reports no accessor (T29)', GRPC,
     'public bool HasHttpContextAccessor => _httpContextAccessor is not null;', 'public bool HasHttpContextAccessor => false;', *GT),
    ('S3 reports no accessor (T29)', S3,
     'public bool HasHttpContextAccessor => _httpContextAccessor is not null;', 'public bool HasHttpContextAccessor => false;',
     'tests/Kronikol.Tests.S3', 'FullyQualifiedName~HasHttpContextAccessorTests'),
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
