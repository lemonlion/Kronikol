#!/usr/bin/env python3
"""Writes History.run.json fragments for one synthetic CI run, built from a real ledger's lines.

Usage: make_fragments.py <ledger> <out-dir> <run-number> [--suites N] [--attempt K] [--fail-first]

Each of the first N suites of the ledger (in first-seen order) becomes one reports directory
<out-dir>/<suite>/Reports/History.run.json holding that suite's last roster, its shapes line and its last
run line, with the run id set to gh:<run-number>:<attempt> and the time moved forward, so a fold sees N
shards of one workflow run. The bytes are the consumer's own, so the ledger grows as the real one does.
"""
import json, os, sys, datetime

args = sys.argv[1:]
ledger, out_dir, run_number = args[0], args[1], int(args[2])
suites_wanted = int(args[args.index('--suites') + 1]) if '--suites' in args else 3
attempt = int(args[args.index('--attempt') + 1]) if '--attempt' in args else 1
fail_first = '--fail-first' in args

rosters, shapes, last_run, order = {}, {}, {}, []
for line in open(ledger, encoding='utf-8'):
    o = json.loads(line)
    t = o.get('t')
    if t == 'roster':
        rosters[o['hash']] = o
    elif t == 'shapes':
        shapes[o['hash']] = o
    elif t == 'run':
        s = o.get('suite')
        if s not in last_run:
            order.append(s)
        last_run[s] = o

at = datetime.datetime(2026, 10, 1, tzinfo=datetime.timezone.utc) + datetime.timedelta(minutes=run_number)
for suite in order[:suites_wanted]:
    run = dict(last_run[suite])
    run['id'] = f'gh:{run_number}:{attempt}'
    run['at'] = at.strftime('%Y-%m-%dT%H:%M:%SZ')
    run['branch'] = 'main'
    run['partial'] = None
    if fail_first and run.get('results'):
        run['results'] = 'F' + run['results'][1:]
    fragment = {'historyFormatVersion': 1, 'generator': '3.31.9', 'roster': rosters[run['roster']], 'run': run}
    if run.get('shapes') in shapes:
        fragment['shapes'] = shapes[run['shapes']]
    safe = ''.join(c if c.isalnum() or c in '._-' else '_' for c in (suite or 'nosuite'))
    directory = os.path.join(out_dir, safe, 'Reports')
    os.makedirs(directory, exist_ok=True)
    with open(os.path.join(directory, 'History.run.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(fragment, f, ensure_ascii=True)
        f.write('\n')
print(f'{min(suites_wanted, len(order))} fragment(s) for gh:{run_number}:{attempt} under {out_dir}')
