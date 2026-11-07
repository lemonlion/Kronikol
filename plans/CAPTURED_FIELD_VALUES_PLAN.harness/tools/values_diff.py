"""Per case, the VALUES lines of two values-pass files (the probe's run-values.sh output), and the cases where they differ.

Usage: python -I values_diff.py <values file A> <values file B>
"""
import sys
from pathlib import Path


def read(path):
    cases, current = {}, None
    for line in Path(path).read_bytes().decode("utf-8", "replace").split("\n"):
        if not line or line.startswith("#"):
            continue
        if not line.startswith(" "):
            current = line.split(" :: ", 1)[0]
            cases[current] = []
        elif line.strip().startswith("VALUES"):
            cases[current].append(line.strip()[len("VALUES"):].strip())
    return cases


a, b = read(sys.argv[1]), read(sys.argv[2])
print(f"cases: {len(a)} vs {len(b)}")
for case in sorted(set(a) | set(b)):
    if a.get(case) != b.get(case):
        print(f"== {case}")
        for v in a.get(case) or ["(none)"]:
            print(f"   A: {v[:220]}")
        for v in b.get(case) or ["(none)"]:
            print(f"   B: {v[:220]}")
