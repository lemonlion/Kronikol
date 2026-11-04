"""From run_cases.py's output with two builds, published with its read forced and R2 with its read forced: each case's flag count, and whether the reading is right as a person judged it elsewhere.

Usage: flags_table.py <run_cases output>...
Prints a line per case holding a flag on any build, and the totals.
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
total = flagged = {}
counts = {}
for path in sys.argv[1:]:
    case = None
    for line in open(path, encoding="utf-8"):
        m = re.match(r"=== (\S+)", line)
        if m:
            case = m.group(1)
            continue
        m = re.match(r"\s+(\S+): flags (\{.*?\}|NO REPORT.*?)(\s+\(all builds the same\))?\s*$", line)
        if not m:
            continue
        label, flags, same = m.group(1), m.group(2), m.group(3)
        labels = [label] if not same else None
        counts.setdefault(case, {})[label] = flags
        if same:
            counts[case]["*"] = flags

builds = sorted({b for row in counts.values() for b in row if b != "*"})
names = [b for b in builds]
print(f"{len(counts)} cases")
shown = 0
for case, row in counts.items():
    if all(v in ("{}",) for v in row.values()):
        continue
    shown += 1
    print(f"  {case}: " + "; ".join(f"{b} {row.get(b, row.get('*'))}" for b in names))
print(f"{shown} cases flagged on some build, {len(counts) - shown} with no flag on any")
