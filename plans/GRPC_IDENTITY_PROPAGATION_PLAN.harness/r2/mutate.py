# R2's mutations (plan section 5): each is applied alone to a scratch worktree at the release commit, the named facts
# are run, and the mutation must turn at least one of them red. The worktree is restored with `git checkout` after each
# one, so never point this at a worktree holding uncommitted work.
# Usage: PYTHONUTF8=1 python mutate.py <scratch worktree> <out file> [part of a mutation's name, to run that one alone]
import subprocess, sys, os

wt, out = sys.argv[1], sys.argv[2]
only = sys.argv[3] if len(sys.argv) > 3 else None
os.chdir(wt)

GRPC = 'src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs'
GT = ('tests/Kronikol.Tests.Grpc', 'FullyQualifiedName~StreamingOutcomeTests')
SERVER_STREAM = ('activityTraceId, activitySpanId, opInfo, activity, TestPhaseContext.Current);\n'
                 '            return new AsyncServerStreamingCall<TResponse>(')

MUTATIONS = [
    ('log a stream at start (T40)', GRPC, SERVER_STREAM,
     SERVER_STREAM.replace('\n', '\n            outcome.Completed();\n', 1), *GT),
    ('record an outcome twice (T43)', GRPC,
     'if (Interlocked.Exchange(ref _recorded, 1) != 0)\n                return;\n', '', *GT),
    ('an early dispose reads OK (T43)', GRPC,
     'Record($"{StatusCode.Cancelled}: the call was disposed before its stream ended", MapGrpcStatusToHttp(StatusCode.Cancelled));',
     'Completed();', *GT),
    ('a failed stream reads OK (T41, T42)', GRPC,
     'Record(text, MapGrpcStatusToHttp(code));', 'Record(text, HttpStatusCode.OK);', *GT),
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
