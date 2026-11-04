#!/usr/bin/env python
"""For each shape run on several suites: do they record the same thing? A row of each dump is compared whole, phases and markers in order,
with the step tree. The reference is the first suite. Usage: python -I cross.py <results dir> <label> <reference suite> <suite> ... [--prefix X,T] [--bars]"""
import pathlib
import re
import sys


def read(path):
    blocks = {}
    name = None
    rows = steps = None
    in_steps = False
    for line in pathlib.Path(path).read_text(encoding='utf-8').splitlines():
        if line.startswith('## '):
            name = line[3:].rsplit('.', 1)[-1]
            rows, steps = [], []
            in_steps = False
            blocks.setdefault(name, []).append((rows, steps))
        elif name is None or line.startswith('call  '):
            continue
        elif line == 'steps:':
            in_steps = True
        elif in_steps:
            if line.strip():
                steps.append(line.rstrip())
        elif line.startswith('--- ACTION START MARKER'):
            rows.append('BOUNDARY')
        elif line.startswith('[bar]'):
            continue
        elif line.strip():
            parts = line.split()
            rows.append(' '.join(parts))
    return blocks


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    prefixes = ['']
    for a in sys.argv[1:]:
        if a.startswith('--prefix='):
            prefixes = a.split('=', 1)[1].split(',')
    results, label = args[:2]
    suites = args[2:]
    data = {s: read(pathlib.Path(results) / label / f'{s}.txt') for s in suites}
    ref = suites[0]
    names = sorted({n for d in data.values() for n in d if any(n.startswith(p) for p in prefixes)})
    agree = disagree = 0
    for name in names:
        base = data[ref].get(name)
        differing = []
        for s in suites[1:]:
            other = data[s].get(name)
            if other is None:
                differing.append((s, 'absent'))
            elif base is None:
                differing.append((s, 'reference absent'))
            elif other != base:
                detail = []
                for i, (a, b) in enumerate(zip(base, other)):
                    if a[0] != b[0]:
                        only_ref = [r for r in a[0] if r not in b[0]]
                        only_other = [r for r in b[0] if r not in a[0]]
                        detail.append(f"rows ref {only_ref} other {only_other}" if (only_ref or only_other) else 'order differs')
                    if a[1] != b[1]:
                        detail.append(f"steps ref {a[1]} other {b[1]}")
                if len(base) != len(other):
                    detail.append(f"{len(base)} blocks in ref, {len(other)} here")
                differing.append((s, '; '.join(detail)))
        if differing:
            disagree += 1
            print(f"DIFFERS {name}")
            for s, d in differing:
                print(f"    {s}: {d}")
        else:
            agree += 1
    print(f"{agree} shapes agree across {', '.join(suites)}; {disagree} differ")


if __name__ == '__main__':
    main()
