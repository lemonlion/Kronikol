#!/usr/bin/env python
# Compares two read-report.sh or read-more.sh outputs scenario by scenario, by title and row, ignoring times and ids.
# usage: compare-reads.py <left> <right> [chars-per-diff]. Prints the scenarios that differ and a count of the rest.
import re, sys

left, right = sys.argv[1], sys.argv[2]
limit = int(sys.argv[3]) if len(sys.argv) > 3 else 600

def blocks(path):
    text = open(path, encoding="utf-8", errors="replace").read()
    found = {}
    for part in re.split(r"^### ", text, flags=re.M):
        head, _, body = part.partition("\n")
        found[head.strip()] = body
    return found

def strip(line):
    line = re.sub(r"\s+\d+(\.\d+)?\s*m?s\b", "", line.rstrip())
    line = re.sub(r"\[i\d+(-i\d+)?\]( \d+ calls)?", "", line)
    return re.sub(r"\s+\d+(\.\d+)?\s*(B|KB|MB)\s*$", "", line).rstrip()

def scenarios(found):
    out = {}
    n = 0
    while f"s{n} steps" in found:
        steps = [strip(l) for l in found[f"s{n} steps"].splitlines()
                 if l.strip() and not re.match(r"^(!|stableId|at |open:|next:|\d+ calls? ·)", l)]
        heading = re.sub(r"^s\d+\s+", "", steps[0]) if steps else f"s{n}"
        title = re.sub(r"\s+\[[A-Za-z]+\].*$", "", heading)
        verdict = re.search(r"\[([A-Za-z]+)\]", heading)
        example = next((l for l in steps if l.startswith("example:")), "")
        calls_key = next((k for k in found if k.startswith(f"s{n} calls")), None)
        calls = []
        if calls_key:
            for l in found[calls_key].splitlines():
                cols = l.split()
                if len(cols) >= 3 and not l.startswith(("step ", "next:", "!")) and "bucket" not in l:
                    calls.append(" ".join(cols[:1] + [c for c in cols[1:] if not re.match(r"^\d+(\.\d+)?$", c) and c not in ("ms", "s", "B", "KB", "MB")])[:60])
        key = title + (" " + example if example else "")
        rows = [l for l in steps[1:] if not l.startswith("example:")]
        out[key] = ([verdict.group(1) if verdict else "?"] + rows, sorted(re.sub(r"\s+(ms|s|B|KB)\b", "", c) for c in calls))
        n += 1
    return out

a, b = scenarios(blocks(left)), scenarios(blocks(right))
same = 0
for key in sorted(set(a) | set(b)):
    if a.get(key) == b.get(key):
        same += 1
        continue
    print(f"=== {key}")
    x, y = a.get(key, (["(absent)"], [])), b.get(key, (["(absent)"], []))
    if x[0] != y[0]:
        print("  LEFT  " + " | ".join(x[0])[:limit])
        print("  RIGHT " + " | ".join(y[0])[:limit])
    if x[1] != y[1]:
        print("  LEFT calls  " + " / ".join(x[1])[:limit])
        print("  RIGHT calls " + " / ".join(y[1])[:limit])
print(f"{same} scenarios the same, {len(set(a) | set(b)) - same} differ")
