"""Which calls move between two labels of the matrix, leaving the boundary out: per suite, each test with a call whose phases changed, and the call as label: before > after.
Usage: python -I matrix_moves.py <results dir> <before label> <after label> [suite ...]"""
import pathlib
import re
import sys

ROW = re.compile(r'^(\S+)\s+(Setup|Action|Unknown|-)\s+(Setup|Action|Unknown|-)\s*(\S*)')


def read(path):
    tests, name = {}, None
    in_steps = False
    for line in path.read_text(encoding='utf-8').splitlines():
        if line.startswith('## '):
            name = line[3:].rsplit('.', 1)[-1]
            tests[name] = {}
            in_steps = False
        elif line.startswith('steps:'):
            in_steps = True
        elif name and not in_steps:
            m = ROW.match(line)
            if m and m[1] != 'call':
                tests[name][m[1]] = (m[2], m[3], m[4])
    return tests


def main():
    root, before, after = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
    suites = sys.argv[4:] or sorted(p.stem for p in (root / after).glob('*.txt') if not p.stem.endswith('.trace'))
    for suite in suites:
        a, b = read(root / before / f'{suite}.txt'), read(root / after / f'{suite}.txt')
        moved = 0
        lines = []
        for test in sorted(set(a) | set(b)):
            diffs = []
            for label in sorted(set(a.get(test, {})) | set(b.get(test, {}))):
                x, y = a.get(test, {}).get(label), b.get(test, {}).get(label)
                if x != y:
                    moved += 1
                    diffs.append(f"{label}: {x and '/'.join(x[:2])} > {y and '/'.join(y[:2])}")
            if diffs:
                lines.append(f'  {test[:60]}: ' + '; '.join(diffs))
        print(f'== {suite}: {moved} calls moved in {len(lines)} of {len(b)} tests')
        for line in lines:
            print(line)


main()
