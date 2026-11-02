# Drives merge-round-trip-fixture.cs: for each case, four runs in CI's order and environment (one GitHub run id per run, its
# shards sharing it), the first three folded into a ledger by the published `kronikol history record`, the fourth not
# yet recorded; then `kronikol merge <run 4> --history <ledger>` on the published tool and on the prototype, and the
# run's own reading beside each. args: <work dir> <published tool dll> <prototype tool dll> [case ...]
# The fixture is built once against the published package (bin-513); bin-proto is that build with the prototype's
# Kronikol.dll in place of the package's.
import os, shutil, subprocess, sys

work, published, prototype, *names = sys.argv[1:]
bins = {'5.27.13': (os.path.join(work, 'bin-513'), published), 'prototype': (os.path.join(work, 'bin-proto'), prototype)}
# MRT_CANDIDATE: a work folder whose bin-513 and bin-proto both hold a candidate's Kronikol.dll, the same tool passed twice, so one reading.
if os.environ.get('MRT_CANDIDATE'):
    bins = {'candidate': (os.path.join(work, 'bin-proto'), prototype)}
fixture = 'merge-round-trip-fixture.dll'

CASES = {
    # name: shards
    'uri': [''], 'uri-relative': [''], 'method': [''], 'status': [''], 'content': [''], 'content-json': [''], 'ignored': [''],
    'retry-one-id': [''], 'shared-id-two-tests': [''], 'repeat': [''], 'defaulted': [''], 'examples': [''], 'examples-json': [''],
    'partial': [''], 'partial-false': [''], 'reorder': [''], 'count': [''], 'window': [''], 'rules': [''],
    'rules-two-shards-same': ['a', 'b'], 'rules-two-shards-differ': ['a', 'b'],
    'error-keys': [''], 'expired': [''], 'features-one-name': [''], 'two-shards': ['a', 'b'], 'two-suites': ['a', 'b'],
    'overlap': ['a', 'b'], 'overlap-reversed': ['a', 'b'], 'run-id': [''], 'real-change': [''],
    'two-shards-change': ['a', 'b'], 'two-shards-change-no-ledger': ['a', 'b'], 'flaky-min-runs': [''],
    'retry-one-id-7': [''], 'count-7': [''], 'reorder-7': [''],
    'gh-record-first': [''], 'run-id-record-first': [''],
    'rules-edit': [''], 'rules-last-shard-diverges': ['a', 'b'], 'pr-branch': [''], 'history-branch': [''], 'history-compare-branch': [''],
    'two-shards-same-ids': ['a', 'b'], 'error-text': [''], 'names': [''], 'method-real-handler': [''], 'attempts': [''], 'not-a-test': [''],
    'retry-process': [''], 'retry-process-failed-only': [''], 'two-shards-late-change': ['a', 'b'], 'two-shards-late-change-pruned': ['a', 'b'],
    'rerun-failed-job': ['a', 'b'], 'rerun-failed-job-record-first': ['a', 'b'], 'two-shards-one-empty': ['a', 'b'],
    'pr-branch-no-base': [''], 'flaky-min-runs-4': [''], 'history-shapes-off': [''], 'history-durations-off': [''], 'slower': [''], 'ado-record-first': [''],
}

# A case run differently from the one it is named after: the fixture's case, the runs, the environment, and how the last run is read.
VARIANTS = {
    'overlap-reversed': dict(fixture='overlap', merge_reversed=True),
    'two-shards-change': dict(runs=7),
    'two-shards-change-no-ledger': dict(fixture='two-shards-change', runs=7, env={'MRT_NO_LEDGER': '1'}),
    'retry-one-id-7': dict(fixture='retry-one-id', runs=7),
    'count-7': dict(fixture='count', runs=7, env={'MRT_OWN_COUNT_RUNS': '1'}),
    'reorder-7': dict(fixture='reorder', runs=7, env={'MRT_OWN_REORDERED': '1'}),
    'flaky-min-runs': dict(runs=7, env={'MRT_OWN_MIN_RUNS': '2'}),
    'gh-record-first': dict(fixture='run-id-plain', record_first=True),
    'run-id-record-first': dict(fixture='run-id', record_first=True),
    'pr-branch': dict(last_env={'GITHUB_BASE_REF': 'main', 'GITHUB_HEAD_REF': 'topic', 'GITHUB_REF_NAME': '7/merge', 'GITHUB_EVENT_NAME': 'pull_request', 'MRT_OWN_BRANCH': 'main'}),
    # The shards ran on the pull request, the merge in a job that is not one (workflow_run, or a re-merge by hand).
    'pr-branch-no-base': dict(fixture='pr-branch', last_env={'GITHUB_BASE_REF': 'main', 'GITHUB_HEAD_REF': 'topic', 'GITHUB_REF_NAME': '7/merge', 'GITHUB_EVENT_NAME': 'pull_request', 'MRT_OWN_BRANCH': 'main'}, merge_env_drop=['GITHUB_BASE_REF', 'GITHUB_HEAD_REF']),
    'rerun-failed-job': dict(shard_env={'b': {'GITHUB_RUN_ATTEMPT': '2'}}),
    'rerun-failed-job-record-first': dict(fixture='rerun-failed-job', shard_env={'b': {'GITHUB_RUN_ATTEMPT': '2'}}, record_first=True),
    'history-branch': dict(last_env={'GITHUB_REF_NAME': 'topic', 'MRT_OWN_BRANCH': 'main'}),
    'history-compare-branch': dict(last_env={'GITHUB_REF_NAME': 'topic'}),
    'retry-process': dict(attempts=2),
    'retry-process-failed-only': dict(attempts=2),
    'two-shards-late-change': dict(runs=7),
    'two-shards-late-change-pruned': dict(fixture='two-shards-late-change', runs=7, prune=3),
    'flaky-min-runs-4': dict(fixture='flaky-min-runs', runs=4, env={'MRT_OWN_MIN_RUNS': '2'}),
    'slower': dict(env={'MRT_OWN_MIN_RUNS': '2', 'MRT_OWN_SLOWER_BY': '1.1', 'MRT_OWN_SLOWER_MIN_MS': '10'}),
    'ado-record-first': dict(fixture='run-id-plain', record_first=True, ado=True),
}

def ado_env(run):
    env = {k: v for k, v in os.environ.items() if not k.startswith('GITHUB_')}
    env.update({'TF_BUILD': 'True', 'BUILD_BUILDID': str(1000 + run), 'BUILD_SOURCEBRANCH': 'refs/heads/main', 'BUILD_SOURCEBRANCHNAME': 'main', 'BUILD_SOURCEVERSION': 'abc%d' % run,
                'SYSTEM_TEAMFOUNDATIONCOLLECTIONURI': 'https://ado.example/', 'SYSTEM_TEAMPROJECT': 'p', 'BUILD_BUILDNUMBER': str(run), 'SYSTEM_JOBATTEMPT': '1'})
    env.pop('KRONIKOL_HISTORY', None)
    return env

def env_for(run):
    env = dict(os.environ)
    env.update({'GITHUB_ACTIONS': 'true', 'GITHUB_RUN_ID': str(1000 + run), 'GITHUB_RUN_ATTEMPT': '1', 'GITHUB_REF_NAME': 'main', 'GITHUB_SHA': 'abc%d' % run,
                'GITHUB_REPOSITORY': 'x/y', 'GITHUB_SERVER_URL': 'https://github.example', 'GITHUB_RUN_NUMBER': str(run)})
    env.pop('KRONIKOL_HISTORY', None)
    env.pop('GITHUB_STEP_SUMMARY', None)
    env.pop('GITHUB_OUTPUT', None)
    return env

def run(cmd, env, cwd=None):
    r = subprocess.run(cmd, env=env, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding='utf-8', errors='replace')
    return r.returncode, r.stdout

for name in names or CASES:
    shards = CASES[name]
    variant = VARIANTS.get(name, {})
    fixture_case = variant.get('fixture', name)
    last = variant.get('runs', 4)
    extra = dict(variant.get('env', {}), MRT_LAST=str(last))
    print(f'\n=== {name} ({"shards " + ",".join(shards) if shards[0] else "one shard"}, {last} runs)', flush=True)
    root = os.path.join(work, 'cases', name)
    shutil.rmtree(root, ignore_errors=True)
    ledger = os.path.join(root, 'ledger.jsonl')
    os.makedirs(root)
    make_env = ado_env if variant.get('ado') else env_for
    for number in range(1, last + 1):
        env = dict(make_env(number), **extra)
        if number == last:
            env.update(variant.get('last_env', {}))
        folders = []
        for shard in shards:
            folder = os.path.join(root, f'run{number}', shard or 'shard')
            folders.append(folder)
            for attempt in range(1, (variant.get('attempts', 1) if number == last else 1) + 1):
                code, out = run(['dotnet', os.path.join(work, 'bin-513', fixture), 'run', fixture_case, folder, ledger, str(number)] + ([shard] if shard else []), dict(env, MRT_ATTEMPT=str(attempt), **(variant.get('shard_env', {}).get(shard, {}) if number == last else {})), cwd=root)
                if code != 0:
                    print(f'  fixture failed ({code}):', out[-1500:])
                    sys.exit(1)
            if number == last:
                own = [l.strip() for l in out.splitlines() if 'history:' in l]
                print(f'  run 4{" shard " + shard if shard else ""}, its own console: {own[0] if own else "(no history line)"}')
        if number < last or variant.get('record_first'):
            code, out = run(['dotnet', published, 'history', 'record', *folders, '--history', ledger], env, cwd=root)
            if code != 0:
                print('  record failed:', out[-800:])
                sys.exit(1)
            if 'partial' in out and number in (1, 2, last - 1):
                print(f'  record of run {number}:', [l for l in out.splitlines() if l.startswith('recorded')][0])
    if variant.get('prune'):
        code, out = run(['dotnet', published, 'history', 'prune', '--history', ledger, '--window', str(variant['prune'])], env, cwd=root)
        print(f"  pruned to a window of {variant['prune']}: exit {code}")
    run4 = [os.path.join(root, f'run{last}', s or 'shard') for s in shards]
    if variant.get('merge_reversed'):
        run4.reverse()
    env = dict(make_env(last), **extra)
    env.update(variant.get('last_env', {}))
    merge_env = {k: v for k, v in env.items() if k not in variant.get('merge_env_drop', [])}
    for label, (bin_dir, tool) in bins.items():
        out_dir = os.path.join(root, 'm-' + label)
        os.makedirs(out_dir, exist_ok=True)
        code, out = run(['dotnet', tool, 'merge', *run4, '-o', os.path.join(out_dir, 'merged.html'), '--history', ledger], merge_env, cwd=root)
        history = [l.strip() for l in out.splitlines() if l.strip().startswith('history:')]
        print(f'-- {label}: kronikol merge exit {code}: {history[0] if history else "(no history line)"}')
        if code != 0:
            print(out[-800:])
            continue
        code, out = run(['dotnet', os.path.join(bin_dir, fixture), 'read', ledger, tool, *run4], env, cwd=root)
        sys.stdout.write(out)
        sys.stdout.flush()
