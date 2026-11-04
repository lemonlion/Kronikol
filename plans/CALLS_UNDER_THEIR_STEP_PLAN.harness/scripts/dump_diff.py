"""The lines that differ, per named scenario, between two dumps of dump.py's format, ignoring order and durations.

Usage: dump_diff.py <a> <b> <scenario code>...
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")


def blocks(path):
    out = {}
    for block in open(path, encoding="utf-8").read().split("=== ")[1:]:
        lines = block.splitlines()
        code = lines[0].split()[1] if len(lines[0].split()) > 1 else lines[0]
        out[code] = lines
    return out


def norm(line):
    line = re.sub(r"\b\d+(\.\d+)?\s?(ms|s)\b", "", line)
    return re.sub(r"\s+", " ", line).strip()


a, b = blocks(sys.argv[1]), blocks(sys.argv[2])
for code in sys.argv[3:]:
    la, lb = [norm(x) for x in a.get(code, [])], [norm(x) for x in b.get(code, [])]
    only_a = [x for x in la if x not in lb]
    only_b = [x for x in lb if x not in la]
    print(f"--- {code}: {len(la)} lines, {len(lb)} lines; only before {len(only_a)}, only after {len(only_b)}; same lines in another order: {sorted(la) == sorted(lb)}")
    for x in only_a[:7]:
        print(f"    - {x[:120]}")
    for x in only_b[:7]:
        print(f"    + {x[:120]}")
