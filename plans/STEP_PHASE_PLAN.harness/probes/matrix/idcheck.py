#!/usr/bin/env python
"""Does each suite really have test ids? From a run's flow traces: labelled points by whether Track's resolver answered an id, a scope did, or nothing did, and the labels with neither.
Usage: python -I idcheck.py <results dir> <label> <suite> ..."""
import pathlib
import re
import sys
from collections import Counter

root, label = sys.argv[1:3]
for suite in sys.argv[3:]:
    path = pathlib.Path(root) / label / f'{suite}.trace.txt'
    if not path.exists():
        print(f'{suite}: no trace')
        continue
    total = Counter()
    none = Counter()
    for line in path.read_text(encoding='utf-8').splitlines():
        m = re.search(r'^(.*?)\s+ambient=\S+\s+resolver=(\S+|threw \S+)\s+scope=(\S+)', line)
        if not m:
            continue
        name, resolver, scope = m.groups()
        if resolver != 'none':
            total['resolver'] += 1
        elif scope != 'none':
            total['scope'] += 1
        else:
            total['none'] += 1
            none[re.sub(r'^[a-z]\d+-|^t\d+-', '', name.strip())] += 1
    blocks = sum(1 for l in (pathlib.Path(root) / label / f'{suite}.txt').read_text(encoding='utf-8').splitlines() if l.startswith('## '))
    print(f"{suite}: {blocks} tests in the dump; points with the framework's id {total['resolver']}, by a scope {total['scope']}, with none {total['none']}" + (f" ({dict(none)})" if none else ''))
