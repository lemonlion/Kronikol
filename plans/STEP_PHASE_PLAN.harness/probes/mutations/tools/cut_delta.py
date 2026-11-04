"""What one unmutated cut changed against another, on tests stable over both baselines. Builds nothing. Usage: cut_delta.py <old root> <new root>"""
import collections
import glob
import os
import sys

import compare

old_root, new_root = sys.argv[1], sys.argv[2]


def rounds(root, suite, name):
    seen = collections.defaultdict(set)
    for path in glob.glob(os.path.join(compare.RESULTS, root, "*", suite, name)):
        for test, lines in compare.read(path).items():
            seen[test].add(tuple(lines))
    return seen


first = os.path.join(compare.RESULTS, new_root, "1")
for suite in sorted(os.listdir(first)):
    if not glob.glob(os.path.join(compare.RESULTS, old_root, "*", suite)):
        continue
    for path in sorted(glob.glob(os.path.join(first, suite, "*.txt"))):
        name = os.path.basename(path)
        if name == "outcome.txt" or name.endswith(compare.SKIP_FILES):
            continue
        old, new = rounds(old_root, suite, name), rounds(new_root, suite, name)
        for test in new:
            if test not in old:
                print(f"[{suite}] {name}: new test {test[:90]}")
                continue
            if len(old[test]) != 1 or len(new[test]) != 1:
                continue
            before, after = next(iter(old[test])), next(iter(new[test]))
            if before != after:
                kind = "ORDER" if sorted(before) == sorted(after) else "CONTENT"
                print(f"[{suite}] {name}: {kind} {test.rsplit('.', 1)[-1][:80]}")
                for row in compare.changed_rows(list(before), list(after))[:10]:
                    print("      " + row[:120])
