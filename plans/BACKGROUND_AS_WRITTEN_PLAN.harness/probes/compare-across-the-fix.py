#!/usr/bin/env python
"""What a reader gets from two reports of one suite, per scenario: compare-across-the-fix.py <kronikol> <reports-a> <reports-b>

Through `kronikol query` only. Times are taken out, as two runs never agree on them. For each scenario: the PlantUML source of each diagram, the
calls it made (`interactions`), and the calls under their steps (`flow`) with the step headers set aside, as those are what the fix moves. Then the
written outputs beside the report: which files there are, and how Specifications.yml and Failures.md differ. Prints only what differs, and a count.
"""
import difflib
import os
import re
import subprocess
import sys
import tempfile

kronikol, left, right = sys.argv[1:4]
TIME = re.compile(r"\b\d+(\.\d+)?\s?(ms|s)\b")
# What a run makes up afresh: an internal-flow link's id, a generated trace. Told apart only by the run that made them.
RUN_IDS = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}|\b[0-9a-f]{32}\b|\b[0-9a-f]{16}\b")
# A step's header in `flow`: "── b0  Given ...", or "── 0  Given ...". Its address is what moves, so it is compared apart.
HEADER = re.compile(r"^(\s*)── (\S+)\s+(.*)$")


def query(*args):
    done = subprocess.run([kronikol, "query", *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    return done.stdout + done.stderr


def scenarios(report):
    """Every scenario by name, across the query's pages. Two with one name are told apart by their order."""
    found, seen, offset = {}, {}, 0
    while True:
        said = query("scenarios", report, "--limit", "200", "--offset", str(offset), "--max-bytes", "2000000")
        for line in said.splitlines():
            match = re.match(r"^(s\d+)\s+\S+\s+\S+\s+(?:\d+ calls?\s+)?(.*)$", line)
            if match:
                # The run's first scenario can carry its warm-up time after its name.
                name = re.sub(r"\s+\d+(\.\d+)?s warm-up$", "", match.group(2).strip())
                seen[name] = seen.get(name, 0) + 1
                found[f"{name} #{seen[name]}" if seen[name] > 1 else name] = match.group(1)
        more = re.search(r"next:.*--offset (\d+)", said)
        if not more:
            return found
        offset = int(more.group(1))


def diagrams(report, address):
    sources = []
    with tempfile.TemporaryDirectory() as folder:
        for index in range(0, 8):
            path = os.path.join(folder, f"d{index}.puml")
            said = query("diagram", report, f"{address}/d{index}", "--out", path)
            if "wrote" not in said or not os.path.exists(path):
                break
            sources.append(RUN_IDS.sub("ID", TIME.sub("T", open(path, encoding="utf-8").read())))
    return sources


def scrub(text, address):
    # A scenario's own address is the same text in both reports unless the order moved, so it is taken out too.
    # A column is as wide as its longest time, so spaces are told apart by nothing.
    return re.sub(r" {2,}", " ", RUN_IDS.sub("ID", TIME.sub("T", text)).replace(address + "/", "@/").replace(address + " ", "@ "))


def flow_parts(text):
    headers, rest = [], []
    for line in text.splitlines():
        match = HEADER.match(line)
        if match:
            headers.append((match.group(2), match.group(3)))
            rest.append("── <step> " + match.group(3))
        else:
            rest.append(line)
    return headers, rest


a, b = scenarios(left), scenarios(right)
print(f"scenarios: {len(a)} and {len(b)}; only in one: {sorted(set(a) ^ set(b))}")
same_diagrams = different_diagrams = same_calls = different_calls = same_flow = different_flow = moved = 0
diagram_count = 0
for name in sorted(set(a) & set(b)):
    x, y = a[name], b[name]
    dx, dy = diagrams(left, x), diagrams(right, y)
    diagram_count += len(dx)
    if dx == dy:
        same_diagrams += 1
    else:
        different_diagrams += 1
        print(f"--- diagrams differ: {name} ({len(dx)} and {len(dy)})")
        for s, t in zip(dx, dy):
            for line in list(difflib.unified_diff(s.splitlines(), t.splitlines(), lineterm="", n=0))[2:14]:
                print("    " + line[:150])

    cx, cy = scrub(query("interactions", left, x), x), scrub(query("interactions", right, y), y)
    if cx == cy:
        same_calls += 1
    else:
        different_calls += 1
        print(f"--- calls differ: {name}")
        for line in list(difflib.unified_diff(cx.splitlines(), cy.splitlines(), lineterm="", n=0))[2:10]:
            print("    " + line[:150])

    hx, fx = flow_parts(scrub(query("flow", left, x), x))
    hy, fy = flow_parts(scrub(query("flow", right, y), y))
    if fx == fy:
        same_flow += 1
    else:
        different_flow += 1
        print(f"--- flow differs beyond its step addresses: {name}")
        for line in list(difflib.unified_diff(fx, fy, lineterm="", n=0))[2:12]:
            print("    " + line[:150])
    if [h[0] for h in hx] != [h[0] for h in hy]:
        moved += 1
        print(f"--- step addresses moved: {name}: {' '.join(h[0] for h in hx)} -> {' '.join(h[0] for h in hy)}"[:220])

print()
print(f"diagrams: {diagram_count} read; scenarios alike {same_diagrams}, differing {different_diagrams}")
print(f"calls: alike {same_calls}, differing {different_calls}")
print(f"flow, step addresses aside: alike {same_flow}, differing {different_flow}")
print(f"scenarios whose step addresses moved: {moved}")

print()
files = lambda folder: sorted(n for n in os.listdir(folder) if os.path.isfile(os.path.join(folder, n)))
print("files only in one:", sorted(set(files(left)) ^ set(files(right))))
for name in ("Specifications.yml", "Failures.md"):
    p, q = os.path.join(left, name), os.path.join(right, name)
    if not (os.path.exists(p) and os.path.exists(q)):
        continue
    s = RUN_IDS.sub("ID", TIME.sub("T", open(p, encoding="utf-8").read())).splitlines()
    t = RUN_IDS.sub("ID", TIME.sub("T", open(q, encoding="utf-8").read())).splitlines()
    delta = [line for line in difflib.unified_diff(s, t, lineterm="", n=0) if not line.startswith(("---", "+++", "@@"))]
    print(f"{name}: {len(s)} and {len(t)} lines, {len(delta)} differing")
