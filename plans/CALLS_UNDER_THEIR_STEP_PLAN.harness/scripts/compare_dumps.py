"""Two dumps of the third review's probes (dump.py's output), scenario by scenario: which scenarios read differently, ignoring what a run generates.

Usage: compare_dumps.py <a.txt> <b.txt> [--show]
With --show, prints each differing scenario's differing lines, under a cap.
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
a_path, b_path, *rest = sys.argv[1:]
show = "--show" in rest
GENERATED = re.compile(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\b\d+(\.\d+)?m?s\b|127\.0\.0\.1:\d+|localhost:\d+")
NOISE = ("open: TestRun",)


def read(path):
    found = {}
    text = open(path, encoding="utf-8").read()
    for block in text.split("=== ")[1:]:
        lines = block.splitlines()
        # s12 Name [Result]: the address can move between builds, so the name is the key.
        name = re.sub(r"^s\d+\s+", "", lines[0]).strip()
        body = []
        for line in lines[1:]:
            if any(n in line for n in NOISE) or line.strip().startswith("at "):
                continue
            line = re.sub(r"\bs\d+/i\d+", "s/i", GENERATED.sub("#", line))
            line = re.sub(r"\bs\d+/", "s/", line)
            line = re.sub(r"\[i\d+(-i\d+)?\]", "[]", line).rstrip()
            line = re.sub(r"-> (\S+)$", r"-> \1", line)
            body.append(line)
        # Calls logged at once swap their order between runs, so a scenario's lines are compared as a set.
        found[name] = sorted(body)
    return found


a, b = read(a_path), read(b_path)
same = differ = 0
for name in sorted(set(a) | set(b)):
    if name not in a or name not in b:
        print(f"only in {'second' if name in b else 'first'}: {name[:90]}")
        differ += 1
        continue
    if a[name] == b[name]:
        same += 1
        continue
    differ += 1
    print(f"DIFF {name[:90]}")
    if show:
        only_a = [l for l in a[name] if l not in b[name]]
        only_b = [l for l in b[name] if l not in a[name]]
        for l in only_a[:8]:
            print(f"   - {l[:100]}")
        for l in only_b[:8]:
            print(f"   + {l[:100]}")
print(f"{same} scenarios read the same, {differ} differ")
