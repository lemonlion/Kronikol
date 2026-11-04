"""Whether two shapes-table.py outputs mark the same steps: python -I compare-lanes.py <a.txt> <b.txt>. Compares the written and marked columns per scenario."""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")


def rows(path):
    found = {}
    for line in open(path, encoding="utf-8"):
        m = re.match(r"^(\d\d .{1,26}?)\s+(pass|FAIL|skip\w*)\s+(\d+|None)\s+(\d+)\s+(\d+)\s+(\d+)", line)
        if m:
            found[m.group(1).strip()] = (m.group(2), m.group(4), m.group(5))
    return found


a, b = rows(sys.argv[1]), rows(sys.argv[2])
same = [name for name in a if name in b and a[name][1] == b[name][1]]
print(f"{len(a)} scenarios against {len(b)}: {len(same)} mark the same number of steps")
for name in sorted(set(a) | set(b)):
    left, right = a.get(name), b.get(name)
    if left is None or right is None or left[1] != right[1]:
        print(f"  differs: {name}: {left} | {right}  (result, marked, steps)")
