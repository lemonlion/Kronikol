"""Runs the built ProxyTap facts under a chosen pressure and keeps every failure's message, so a rare failure can be read rather than guessed.

args: <built output folder> <label> <instances> <rounds> <out dir> [--cpu N] [--churn N] [--filter TEXT]
  instances  copies of the test program run at once, each from its own copy of the output folder
  --cpu N    N busy-looping helper processes for the length of each round
  --churn N  N helper processes that bind port 0, listen and let go as fast as they can, as another suite's probes do
"""
import argparse
import collections
import json
import os
import re
import shutil
import subprocess
import sys
import time

ap = argparse.ArgumentParser()
ap.add_argument('built')
ap.add_argument('label')
ap.add_argument('instances', type=int)
ap.add_argument('rounds', type=int)
ap.add_argument('out')
ap.add_argument('--cpu', type=int, default=0)
ap.add_argument('--churn', type=int, default=0)
ap.add_argument('--filter', default='')
args = ap.parse_args()

HOG = "while True:\n    pass\n"
CHURN = (
    "import socket\n"
    "while True:\n"
    "    s = socket.socket()\n"
    "    s.bind(('127.0.0.1', 0))\n"
    "    s.listen(1)\n"
    "    s.close()\n"
)

out = os.path.join(args.out, args.label)
os.makedirs(out, exist_ok=True)
copies = []
for i in range(args.instances):
    copy = os.path.join(args.out, 'copies', f'i{i}')
    if not os.path.exists(copy):
        shutil.copytree(args.built, copy, ignore=shutil.ignore_patterns('Reports', 'TestResults'))
    copies.append(copy)

dll = 'Kronikol.Tests.ProxyTap.dll'
env = dict(os.environ, KRONIKOL_HISTORY='off', KRONIKOL_KEEP_RUNS='off')
env.pop('PLAYWRIGHT_BROWSERS_PATH', None)

summary = {'label': args.label, 'instances': args.instances, 'rounds': args.rounds, 'cpu': args.cpu, 'churn': args.churn, 'runs': 0, 'failed runs': 0, 'failures': 0}
by_fact = collections.Counter()
by_message = collections.Counter()
detail = open(os.path.join(out, 'failures.txt'), 'w', encoding='utf8', newline='\n')
progress = open(os.path.join(out, 'progress.txt'), 'w', encoding='utf8', newline='\n')
FAILED = re.compile(r'^\s+Failed (\S.*?) \[[^\]]*\]\s*$')

for round_number in range(1, args.rounds + 1):
    helpers = [subprocess.Popen([sys.executable, '-c', HOG]) for _ in range(args.cpu)]
    helpers += [subprocess.Popen([sys.executable, '-c', CHURN]) for _ in range(args.churn)]
    started = time.time()
    runs = []
    for copy in copies:
        command = ['dotnet', 'test', os.path.join(copy, dll), '--nologo', '--logger', 'console;verbosity=normal']
        if args.filter:
            command += ['--filter', args.filter]
        runs.append(subprocess.Popen(command, cwd=copy, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf8', errors='replace'))
    outputs = [run.communicate() for run in runs]
    for helper in helpers:
        helper.kill()
    for helper in helpers:
        helper.wait()
    took = time.time() - started

    round_failures = 0
    for index, (run, (text, _)) in enumerate(zip(runs, outputs)):
        summary['runs'] += 1
        if run.returncode != 0:
            summary['failed runs'] += 1
        lines = text.splitlines()
        i = 0
        while i < len(lines):
            match = FAILED.match(lines[i])
            if not match:
                i += 1
                continue
            name = match.group(1)
            block = [lines[i]]
            i += 1
            while i < len(lines) and not FAILED.match(lines[i]) and not lines[i].startswith(('Test Run', 'Total tests', 'Results File')):
                block.append(lines[i])
                i += 1
            message = next((b.strip() for j, b in enumerate(block) if j > 0 and b.strip() and 'Error Message' not in b), '?')
            summary['failures'] += 1
            round_failures += 1
            by_fact[name] += 1
            # The message without the numbers that differ from run to run.
            by_message[re.sub(r'\d+', 'N', message)[:160]] += 1
            detail.write(f'=== round {round_number} instance {index} {name}\n' + '\n'.join(block[:40]) + '\n\n')
            detail.flush()
    progress.write(f'round {round_number}: {took:.0f} s, failures {round_failures}, codes {[r.returncode for r in runs]}\n')
    progress.flush()

detail.close()
progress.close()
summary['by fact'] = by_fact.most_common()
summary['by message'] = by_message.most_common()
with open(os.path.join(out, 'summary.json'), 'w', encoding='utf8', newline='\n') as f:
    json.dump(summary, f, indent=1)
print(json.dumps({k: summary[k] for k in ('label', 'instances', 'rounds', 'cpu', 'churn', 'runs', 'failed runs', 'failures')}))
for name, count in by_fact.most_common(12):
    print(count, name[:150])
for message, count in by_message.most_common(12):
    print(count, message[:170])
