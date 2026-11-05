#!/usr/bin/env python
"""Side by side, per test: every row of a dump that differs between two versions. Usage: cmp.py <a dump> <b dump> [--all]"""
import sys, re, difflib

def parse(path):
    tests, name, body = {}, None, []
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        if line.startswith('## '):
            if name is not None:
                tests[name] = body
            name, body = line[3:], []
        elif name is not None:
            body.append(line)
    if name is not None:
        tests[name] = body
    return tests

a, b = parse(sys.argv[1]), parse(sys.argv[2])
show_all = '--all' in sys.argv
same = 0
for name in sorted(set(a) | set(b)):
    x, y = a.get(name), b.get(name)
    if x is None or y is None:
        print(f'## {name}: only in {"b" if x is None else "a"}')
        continue
    if x == y:
        same += 1
        continue
    print(f'## {name}')
    for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, x, y, autojunk=False).get_opcodes():
        if op == 'equal':
            continue
        for line in x[i1:i2]:
            print('  - ' + line.strip()[:130])
        for line in y[j1:j2]:
            print('  + ' + line.strip()[:130])
print(f'\n{same} tests identical, {len(set(a)|set(b))} in all')
