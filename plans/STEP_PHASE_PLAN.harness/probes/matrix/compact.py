#!/usr/bin/env python
"""Each test of a suite on one or two lines: its calls in order with their phases, S, A, U, and | where a call's HTTP and message phases differ, and [B] where a boundary marker is logged.
Usage: python -I compact.py <results dir> <control label> <variant label> <suite> [test name prefix ...]"""
import pathlib
import sys

P = {'Setup': 'S', 'Action': 'A', 'Unknown': 'U', '-': '-'}


def read(path):
    blocks = []
    in_steps = False
    rows = None
    for line in pathlib.Path(path).read_text(encoding='utf-8').splitlines():
        if line.startswith('## '):
            rows = []
            steps = []
            blocks.append((line[3:].rsplit('.', 1)[-1], rows, steps))
            in_steps = False
        elif rows is None or line.startswith('call  '):
            continue
        elif line == 'steps:':
            in_steps = True
        elif in_steps:
            if line.strip():
                steps.append(line.rstrip())
        elif line.startswith('--- ACTION START MARKER'):
            rows.append('[B]')
        elif line.startswith('[bar]'):
            continue
        elif line.strip():
            parts = line.split()
            label = parts[0]
            http = P.get(parts[1], parts[1]) if len(parts) > 1 else '-'
            msg = P.get(parts[2], parts[2]) if len(parts) > 2 else '-'
            ignored = '!' if len(parts) > 3 else ''
            phase = http if http == msg or msg == '-' else (msg if http == '-' else f'{http}|{msg}')
            rows.append(f'{label} {phase}{ignored}')
    return blocks


def main():
    results, control, variant, suite = sys.argv[1:5]
    prefixes = sys.argv[5:]
    a = read(pathlib.Path(results) / control / f'{suite}.txt')
    b = read(pathlib.Path(results) / variant / f'{suite}.txt')
    names = []
    for block in a + b:
        if block[0] not in names:
            names.append(block[0])
    def of(blocks, name):
        return [x for x in blocks if x[0] == name]
    for name in names:
        if prefixes and not any(name.startswith(p) for p in prefixes):
            continue
        ca, cb = of(a, name), of(b, name)
        for i in range(max(len(ca), len(cb))):
            ra = ca[i][1] if i < len(ca) else None
            rb = cb[i][1] if i < len(cb) else None
            tag = f' #{i + 1}' if max(len(ca), len(cb)) > 1 else ''
            if ra == rb:
                print(f'{name}{tag}\n  same: {", ".join(ra)}')
            else:
                print(f'{name}{tag}\n  ctrl: {", ".join(ra) if ra is not None else "(absent)"}\n  var:  {", ".join(rb) if rb is not None else "(absent)"}')
            sa = ca[i][2] if i < len(ca) else None
            sb = cb[i][2] if i < len(cb) else None
            if sa != sb:
                print('  steps ctrl: ' + ' / '.join(s.strip() for s in (sa or [])))
                print('  steps var:  ' + ' / '.join(s.strip() for s in (sb or [])))


if __name__ == '__main__':
    main()
