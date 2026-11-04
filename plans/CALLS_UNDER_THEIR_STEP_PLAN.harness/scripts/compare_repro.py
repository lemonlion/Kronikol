"""Two outputs of read_calls.py, scenario by scenario, ignoring durations and interaction addresses.

Usage: compare_repro.py <a.txt> <b.txt> [--show]
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
a_path, b_path, *rest = sys.argv[1:]
show = "--show" in rest


def read(path):
    found, name = {}, None
    for line in open(path, encoding="utf-8"):
        line = line.rstrip()
        if line.startswith("--- "):
            name = line
            found[name] = []
        elif name:
            line = re.sub(r"\s+\d+(\.\d+)?m?s\b", "", line)
            line = re.sub(r"\s*\[i\d+(-i\d+)?\]", "", line)
            line = re.sub(r"customer-\d+", "customer-N", line)
            if line.strip().startswith("calls:"):
                found[name].extend(sorted(line.strip()[6:].split()))
            elif line.strip():
                found[name].append(line)
    return found


a, b = read(a_path), read(b_path)
same = differ = 0
for name in sorted(set(a) | set(b)):
    if name not in a or name not in b:
        print(f"only in {'second' if name in b else 'first'}: {name[:90]}")
        differ += 1
    elif a[name] == b[name]:
        same += 1
    else:
        differ += 1
        print(f"DIFF {name[:90]}")
        if show:
            for line in [l for l in a[name] if l not in b[name]][:8]:
                print(f"   - {line[:96]}")
            for line in [l for l in b[name] if l not in a[name]][:8]:
                print(f"   + {line[:96]}")
print(f"{same} scenarios read the same, {differ} differ")
