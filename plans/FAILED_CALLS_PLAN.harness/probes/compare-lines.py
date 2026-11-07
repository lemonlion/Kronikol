"""Compares each probe case's fingerprint line on 5.31.1 (frozen) with the line this build makes under rule 11, and prints
the line rule 12 reads. Usage: python compare-lines.py <frozen.txt> <this-build.txt>"""
import re
import sys


def cases(path):
    found, title = {}, None
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.rstrip("\n")
        if line and not line.startswith(" ") and ": " in line and not line.startswith(("Kronikol", "SDKs")):
            title = line
            found.setdefault(title, {"11": [], "12": []})
        elif title and (m := re.match(r"\s+fingerprint line: (.*)", line)):
            found[title]["11"].append(m.group(1))
        elif title and (m := re.match(r"\s+rule 12 line: (.*)", line)):
            found[title]["12"].append(m.group(1))
    return found


frozen, built = cases(sys.argv[1]), cases(sys.argv[2])
moved = 0
for title, then in frozen.items():
    now = built.get(title)
    if now is None:
        print(f"MISSING on this build: {title}")
        moved += 1
        continue
    same = then["11"] == now["11"]
    moved += not same
    print(("same " if same else "MOVED") + f"  {title}")
    if not same:
        print(f"    5.31.1:        {then['11']}")
        print(f"    this, rule 11: {now['11']}")
    if now["12"] != now["11"]:
        print(f"    rule 12:       {now['12']}")
print(f"\n{len(frozen)} cases, {moved} whose rule-11 line moved")
