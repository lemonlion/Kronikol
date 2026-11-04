#!/usr/bin/env python
"""Side by side: every test of one suite on the control and on the variant, call by call.

Usage: python -I compare.py <results dir> <control label> <variant label> <suite> [test name prefix ...] [--all]

A row is: label, control http/message, variant http/message, and a mark where they differ. A marker's row says which side has it.
Without --all, tests whose phases and markers are the same on both sides print one line.
"""
import pathlib
import re
import sys


def read(path):
    """[(test name, [(kind, label, http, message, ignored)], [step lines])], in file order. A retried test can have several blocks of one name."""
    blocks = []
    name = None
    rows = None
    steps = None
    in_steps = False
    for line in pathlib.Path(path).read_text(encoding='utf-8').splitlines():
        if line.startswith('## '):
            name = line[3:]
            rows, steps = [], []
            in_steps = False
            blocks.append((name, rows, steps))
        elif name is None or line.startswith('call  '):
            continue
        elif line == 'steps:':
            in_steps = True
        elif in_steps:
            if line.strip():
                steps.append(line)
        elif line.startswith('--- ACTION START MARKER'):
            rows.append(('marker', 'BOUNDARY', '', '', ''))
        elif line.startswith('[bar]'):
            rows.append(('bar', re.sub(r'<[^>]+>', '', line[6:]).strip(), '', '', ''))
        elif line.strip():
            parts = line.split()
            label = parts[0]
            http = parts[1] if len(parts) > 1 else '-'
            message = parts[2] if len(parts) > 2 else '-'
            ignored = parts[3] if len(parts) > 3 else ''
            rows.append(('call', label, http, message, ignored))
    return blocks


def key(name):
    return re.sub(r'^PhaseProbe\.Suite\.[A-Za-z0-9_]+\.', '', name)


def simple(rows, with_bars=False):
    return [r for r in rows if r[0] != 'bar' or with_bars]


def show(control_rows, variant_rows):
    """Aligns two rows lists: labels in the control's order, a label only one side has on its own row."""
    out = []
    labels = {}
    for r in variant_rows:
        labels.setdefault(r[1], []).append(r)
    used = set()
    def fmt(r):
        return f"{r[2]}/{r[3]}" + (' IGNORED' if r[4] else '') if r else '-'
    def markers(rows):
        return [i for i, r in enumerate(rows) if r[0] == 'marker']
    # The order on each side matters for markers, so print each side's own order where they differ.
    c = [r for r in control_rows if r[0] != 'bar']
    v = [r for r in variant_rows if r[0] != 'bar']
    i = j = 0
    while i < len(c) or j < len(v):
        a = c[i] if i < len(c) else None
        b = v[j] if j < len(v) else None
        if a and b and a[1] == b[1]:
            mark = '' if a[2:] == b[2:] else '  <<<'
            if a[0] == 'marker':
                out.append(f"    {'--- boundary ---':<46} both")
            else:
                out.append(f"    {a[1]:<46} {fmt(a):<26} {fmt(b):<26}{mark}")
            i += 1
            j += 1
        elif a and a[0] == 'marker':
            out.append(f"    {'--- boundary (control only) ---':<46}")
            i += 1
        elif b and b[0] == 'marker':
            out.append(f"    {'--- boundary (variant only) ---':<46}")
            j += 1
        elif b and a and any(x[1] == a[1] for x in v[j:]):
            out.append(f"    {b[1]:<46} {'':<26} {fmt(b):<26}  <<< order or variant only")
            j += 1
        elif a:
            out.append(f"    {a[1]:<46} {fmt(a):<26} {'':<26}  <<< order or control only")
            i += 1
        else:
            out.append(f"    {b[1]:<46} {'':<26} {fmt(b):<26}  <<< variant only")
            j += 1
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    show_all = '--all' in sys.argv
    results, control, variant, suite = args[:4]
    prefixes = args[4:]
    a = read(pathlib.Path(results) / control / f'{suite}.txt')
    b = read(pathlib.Path(results) / variant / f'{suite}.txt')
    by_name_b = {}
    for block in b:
        by_name_b.setdefault(key(block[0]), []).append(block)
    seen = {}
    names = []
    for block in a + b:
        if key(block[0]) not in names:
            names.append(key(block[0]))
    by_name_a = {}
    for block in a:
        by_name_a.setdefault(key(block[0]), []).append(block)
    for name in names:
        if prefixes and not any(name.startswith(p) for p in prefixes):
            continue
        blocks_a = by_name_a.get(name, [])
        blocks_b = by_name_b.get(name, [])
        for n in range(max(len(blocks_a), len(blocks_b))):
            ra = blocks_a[n][1] if n < len(blocks_a) else []
            rb = blocks_b[n][1] if n < len(blocks_b) else []
            sa = blocks_a[n][2] if n < len(blocks_a) else []
            sb = blocks_b[n][2] if n < len(blocks_b) else []
            suffix = f' [block {n + 1}]' if max(len(blocks_a), len(blocks_b)) > 1 else ''
            same = simple(ra) == simple(rb) and sa == sb
            markers = sum(1 for r in rb if r[0] == 'marker')
            if same and not show_all:
                print(f"SAME  {name}{suffix}  ({markers} marker{'s' if markers != 1 else ''})")
                continue
            print(f"{'SAME' if same else 'DIFF'}  {name}{suffix}")
            print(f"    {'':<46} {'control http/msg':<26} {'variant http/msg':<26}")
            for line in show(ra, rb):
                print(line)
            if sa != sb:
                print('    steps, control:')
                for s in sa:
                    print('    ' + s)
                print('    steps, variant:')
                for s in sb:
                    print('    ' + s)


if __name__ == '__main__':
    main()
