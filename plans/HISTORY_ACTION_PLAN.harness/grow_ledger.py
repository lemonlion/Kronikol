#!/usr/bin/env python3
"""Grows a ledger to a target size the way time grows it: more run lines.

Usage: grow_ledger.py <ledger> <out> <target-bytes>

Copies the ledger, then appends copies of its run lines, taken in order and cycled, each with a new id
(gh:<9000000000 + n>:1) and a time one minute after the last, until the file reaches the target. Roster
and shapes lines are not repeated: a real ledger gains one only when a suite's scenarios change.
"""
import json, sys, datetime

src, out, target = sys.argv[1], sys.argv[2], int(sys.argv[3])
runs, last_at = [], None
with open(src, encoding='utf-8') as f, open(out, 'w', encoding='utf-8', newline='\n') as o:
    for line in f:
        o.write(line)
        obj = json.loads(line)
        if obj.get('t') == 'run':
            runs.append(obj)
            last_at = obj['at']
    size = o.tell()
    at = datetime.datetime.strptime(last_at, '%Y-%m-%dT%H:%M:%SZ').replace(tzinfo=datetime.timezone.utc)
    n = 0
    while size < target:
        run = dict(runs[n % len(runs)])
        n += 1
        at += datetime.timedelta(minutes=1)
        run['id'] = f'gh:{9000000000 + n}:1'
        run['at'] = at.strftime('%Y-%m-%dT%H:%M:%SZ')
        text = json.dumps(run, separators=(',', ':'), ensure_ascii=False) + '\n'
        o.write(text)
        size += len(text.encode('utf-8'))
print(f'{out}: {size} bytes, {n} run lines added')
