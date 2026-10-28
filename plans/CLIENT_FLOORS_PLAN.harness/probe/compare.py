# Compare each scenario's outcome lines (not the requests, whose job ids and dataset names differ) between two probe runs.
# python -I compare.py <old> <new>
import re, sys

def outcomes(path):
    out, name = {}, None
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.rstrip("\n")
        m = re.match(r"## (Q\d+|S0)", line)
        if m:
            name = m.group(1); out[name] = []; continue
        if line.startswith("## "):
            name = None; continue
        if name and not re.match(r"\s*(POST|GET|PUT|PATCH|DELETE) ", line) and "job_" not in line and "probe_" not in line:
            # dataset names carry the run's tag
            out[name].append(re.sub(r"ds_[0-9a-z_]+", "ds", line))
    return out

a, b = outcomes(sys.argv[1]), outcomes(sys.argv[2])
same = diff = 0
for q in sorted(set(a) | set(b), key=lambda q: (len(q), q)):
    if q not in a or q not in b:
        print(f"{q}: only in {'new' if q in b else 'old'}")
        continue
    if a[q] == b[q]:
        same += 1
    else:
        diff += 1
        print(f"{q}: DIFFERS\n  old: {a[q]}\n  new: {b[q]}")
print(f"{same} the same, {diff} differ")
