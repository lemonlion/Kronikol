"""against_boundary.py over any suites, with counts per suite and test family: every recorded phase that disagrees with where its call is drawn.

    python -I against_boundary_all.py <results folder> <label> <suite> ... [--list]

A call drawn before a test's first Setup/Action marker should not read Action, and one after it should not read Setup or Unknown. Service-side calls
(labels starting svc) read Unknown by design and are left out. Tests with no marker are skipped, so a label with fewer markers can score lower for that reason alone.
"""
import pathlib
import re
import sys
from collections import Counter

ROW = re.compile(r"^(?P<label>\S+)\s+(?P<http>Setup|Action|Unknown|-)\s+(?P<msg>Setup|Action|Unknown|-)\s*(?:IGNORED)?\s*$")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    show = '--list' in sys.argv
    root, label = pathlib.Path(args[0]), args[1]
    for suite in args[2:]:
        tests = with_marker = 0
        wrong = Counter()
        rows = []
        block = None
        blocks = []
        for line in (root / label / f'{suite}.txt').read_text(encoding='utf-8').splitlines():
            if line.startswith('## '):
                block = dict(name=line[3:].rsplit('.', 1)[-1], lines=[], steps=False)
                blocks.append(block)
            elif block is not None:
                if line.startswith('steps:'):
                    block['steps'] = True
                elif not block['steps']:
                    block['lines'].append(line)
        for b in blocks:
            tests += 1
            if not any('ACTION START MARKER' in l for l in b['lines']):
                continue
            with_marker += 1
            seen = False
            for l in b['lines']:
                if 'ACTION START MARKER' in l:
                    seen = True
                    continue
                m = ROW.match(l)
                if not m or m['label'].startswith('svc'):
                    continue
                for column in ('http', 'msg'):
                    v = m[column]
                    if v == '-':
                        continue
                    if (seen and v != 'Action') or (not seen and v == 'Action'):
                        wrong[b['name'][:1]] += 1
                        rows.append(f"{b['name'][:50]:<52} {m['label']:<30} {column:<5} {v:<8} {'after' if seen else 'before'}")
        print(f"{suite:<18} {label:<8} tests {tests:>3}, with a boundary {with_marker:>3}, phases against it {sum(wrong.values()):>3}  {dict(wrong)}")
        if show:
            for r in rows:
                print('   ' + r)


main()
