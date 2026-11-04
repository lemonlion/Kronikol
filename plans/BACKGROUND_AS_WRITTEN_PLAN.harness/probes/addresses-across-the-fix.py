#!/usr/bin/env python
"""Which scenarios' step addresses differ between two reports of one suite: addresses-across-the-fix.py <kronikol> <reports-a> <reports-b>

Every scenario, matched on its stable id, through `kronikol query` only. Rows sharing a stable id are told apart by their order. A full compare-across-the-fix.py reads a real
suite as noise, as it differs from itself between two runs, and takes over an hour; a step's address does not depend on either.
"""
import json
import re
import subprocess
import sys

kronikol, left, right = sys.argv[1:4]
TOP_LEVEL = re.compile(r"^[✗·~\s]*\s(b?\d+)\s{2,}\S")


def query(*args):
    done = subprocess.run([kronikol, "query", *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    return done.stdout + done.stderr


def scenarios(report):
    found, offset = {}, 0
    while True:
        page = json.loads(query("scenarios", report, "--json", "--limit", "200", "--max-bytes", "2000000", "--offset", str(offset)))
        for item in page["items"]:
            key = item["stableId"]
            n = 1
            while (key, n) in found:
                n += 1
            found[(key, n)] = item
        following = page.get("next")
        if not following or "--offset" not in following:
            return found
        offset = int(following[following.index("--offset") + 1])


def addresses(report, address):
    seen = []
    for line in query("steps", report, address).splitlines():
        match = TOP_LEVEL.match(" " + line)
        if match and "." not in match.group(1):
            seen.append(match.group(1))
    return seen


a, b = scenarios(left), scenarios(right)
print(f"scenarios: {len(a)} and {len(b)}; by stable id only in one: {sorted(k[0] for k in set(a) ^ set(b))}")
marked_a = marked_b = moved = 0
for key in sorted(set(a) & set(b), key=lambda k: a[k]["address"]):
    x, y = addresses(left, a[key]["address"]), addresses(right, b[key]["address"])
    marked_a += any(s.startswith("b") for s in x)
    marked_b += any(s.startswith("b") for s in y)
    if x != y:
        moved += 1
        print(f"moved: {a[key]['feature']} > {a[key]['scenario'][:70]}: {' '.join(x)} -> {' '.join(y)}")
print(f"with a b address: {marked_a} and {marked_b}")
print(f"scenarios whose step addresses moved: {moved}")
