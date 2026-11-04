"""Two dumps of the fluent BDDfy probe side by side, each call as url=step. Usage: bddfy_fluent_read.py <dump> <dump> <label> <label>"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def read(path):
    out = {}
    current = None
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.rstrip()
        if line.startswith("=== "):
            current = re.sub(r"^=== s\d+ ", "", line)
            out[current] = []
        elif " GET " in line and current:
            m = re.search(r"GET (\S+)\s+-> (\S+)", line)
            out[current].append(f"{m.group(1)}={m.group(2)}")
    return out


a, b = read(sys.argv[1]), read(sys.argv[2])
for name in a:
    print(name)
    print(f"  {sys.argv[3]}: " + " ".join(a[name]))
    print(f"  {sys.argv[4]}: " + " ".join(b.get(name, [])))
