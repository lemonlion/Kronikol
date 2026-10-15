"""Prints every attachment a Kronikol report lists, where it is listed, and the attachments/ folder's files.

Usage: python -I extract_attachments.py <reports dir>
Reads TestRunReport.json without printing anything but attachment entries (the report itself is never opened).
"""
import json
import os
import sys

reports = sys.argv[1]
with open(os.path.join(reports, "TestRunReport.json"), encoding="utf-8-sig") as f:
    report = json.load(f)


def key(d, *names):
    for n in names:
        if n in d:
            return d[n]
        for k in d:
            if k.lower() == n.lower():
                return d[k]
    return None


def walk_steps(steps, depth, out):
    for step in steps or []:
        for a in key(step, "attachments") or []:
            out.append((f"{'  ' * depth}step {key(step, 'text')!r}", a))
        walk_steps(key(step, "subSteps"), depth + 1, out)


total = 0
for feature in key(report, "features") or []:
    for scenario in key(feature, "scenarios") or []:
        rows = [("scenario", a) for a in key(scenario, "attachments") or []]
        walk_steps(key(scenario, "steps"), 1, rows)
        walk_steps(key(scenario, "backgroundSteps"), 1, rows)
        print(f"-- {key(scenario, 'displayName')!r}: {len(rows)} attachment(s)")
        for where, a in rows:
            total += 1
            print(f"   {where:52} {key(a, 'name')!r:26} {os.path.basename(str(key(a, 'path'))):34} {key(a, 'mediaType')}")
print(f"== {total} listed")

folder = os.path.join(reports, "attachments")
if os.path.isdir(folder):
    files = sorted(os.listdir(folder))
    size = sum(os.path.getsize(os.path.join(folder, n)) for n in files)
    print(f"== attachments/: {len(files)} file(s), {size} B")
    for n in files:
        print(f"   {n:40} {os.path.getsize(os.path.join(folder, n)):>9} B")
