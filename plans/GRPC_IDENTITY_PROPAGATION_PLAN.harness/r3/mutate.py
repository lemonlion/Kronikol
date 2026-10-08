# R3's mutations: each is applied alone to a scratch worktree at the release commit, the named facts are run, and the
# mutation must turn at least one of them red. The worktree is restored with `git checkout` after each one, so never
# point this at a worktree holding uncommitted work.
# Usage: PYTHONUTF8=1 python mutate.py <scratch worktree> <out file> [part of a mutation's name, to run that one alone]
import subprocess, sys, os

wt, out = sys.argv[1], sys.argv[2]
only = sys.argv[3] if len(sys.argv) > 3 else None
os.chdir(wt)

TTH = 'src/Kronikol/Tracking/TestTrackingMessageHandler.cs'
KT = ('tests/Kronikol.Tests', 'FullyQualifiedName~HttpHopTests|FullyQualifiedName~TestTrackingMessageHandlerTests')
ID = 'AddIfAbsent(request, TestTrackingHttpHeaders.CurrentTestIdHeader, TrackingHeaderValue.Encode(currentTestInfo.Id));'
TRANSPORT = 'return handler is SocketsHttpHandler or HttpClientHandler;'

MUTATIONS = [
    ('stamp the id only when the request being served lacks it, the old rule', TTH, ID, 'if (!hasCurrentTestIdHeader) ' + ID, *KT),
    ('add a header the request already carries', TTH,
     'if (value is not null && !request.Headers.Contains(name))', 'if (value is not null)', *KT),
    ('a new trace id on every hop', TTH,
     'hasTraceIdHeader && Guid.TryParse(traceIdHeaders.First(), out var inboundTraceId) ? inboundTraceId : Guid.NewGuid()',
     'Guid.NewGuid()', *KT),
    ('send no caller name on a hop', TTH, 'TrackingHeaderValue.Encode(_callerName)', 'null', *KT),
    ('no traceparent while a span is current, the old rule', TTH, TRANSPORT, 'return true;', *KT),
    ('pre-empt the framework on SocketsHttpHandler', TTH, TRANSPORT, 'return false;', *KT),
    ('the options\' accessor wins over the one passed in', TTH,
     '_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;',
     '_httpContextAccessor = options.HttpContextAccessor ?? httpContextAccessor;',
     'tests/Kronikol.Tests', 'FullyQualifiedName~HttpContextAccessorOptionsTests'),
    # Found while releasing R2: the listener's start, and the ledger's read budget now scaled by the machine's load.
    ('a caller that loses the race returns at once, the #70 rule', 'src/Kronikol/InternalFlow/InternalFlowActivityListener.cs',
     'SpinWait.SpinUntil(() => _autoStarted != null, RegistrationWait);', '',
     'tests/Kronikol.Tests', 'FullyQualifiedName~InternalFlowActivityListenerStartTests'),
    ('a caller waits no time for the registration', 'src/Kronikol/InternalFlow/InternalFlowActivityListener.cs',
     'RegistrationWait = TimeSpan.FromSeconds(1);', 'RegistrationWait = TimeSpan.Zero;',
     'tests/Kronikol.Tests', 'FullyQualifiedName~InternalFlowActivityListenerStartTests'),
    ('a ledger read eight seconds slower, more than the budget stretched to its cap', 'src/Kronikol/History/HistoryLedger.cs',
     '        ArgumentException.ThrowIfNullOrWhiteSpace(path);\n        budget ??= HistoryLockBudget.Default;',
     '        ArgumentException.ThrowIfNullOrWhiteSpace(path);\n        Thread.Sleep(8000);\n        budget ??= HistoryLockBudget.Default;',
     'tests/Kronikol.Tests', 'FullyQualifiedName~Ledger_for_5000_scenarios_over_50_runs_stays_under_the_budget'),
    ('relationship stats eleven seconds slower, more than the budget stretched to its cap', 'src/Kronikol/ComponentDiagram/ComponentFlowSegmentBuilder.cs',
     '        var result = new Dictionary<string, RelationshipStats>();\n        if (relationships.Length == 0 || logs.Length == 0)',
     '        var result = new Dictionary<string, RelationshipStats>();\n        if (logs.Length >= 3000) Thread.Sleep(11000);\n        if (relationships.Length == 0 || logs.Length == 0)',
     'tests/Kronikol.Tests', 'FullyQualifiedName~ComputeRelationshipStats_scales_to_1500_tests'),
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
