"""Compares two runs of one suite: each scenario's calls, the background block and the orphan warning on the console.

    python compare_reports.py <before reports dir> <after reports dir> [<before console> <after console>]

A call is (type, method, path with ids folded, service, caller, status, phase, attribution source); a scenario's calls
are compared as a multiset. Prints what differs, or "identical".
"""
import collections
import json
import pathlib
import re
import sys

ID = re.compile(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{12,}|\d+")


def calls(interactions):
    out = collections.Counter()
    for c in interactions:
        uri = re.sub(r"^\w+://[^/]+", "", c.get("uri") or "")
        out[(c.get("type"), c.get("method"), ID.sub("{v}", uri), c.get("serviceName"), c.get("callerName"),
             c.get("statusCode"), c.get("phase"), c.get("attributionSource"))] += 1
    return out


def read(directory):
    report = json.loads(next(pathlib.Path(directory).glob("TestRunReport.json")).read_text(encoding="utf-8"))
    scenarios = {}
    for f in report.get("features") or []:
        for s in f.get("scenarios") or []:
            scenarios[f"{f['name']} / {s['name']}"] = (s.get("result"), calls(s.get("httpInteractions") or []))
    bg = report.get("background") or {}
    return scenarios, bg.get("calls", 0), calls(bg.get("interactions") or []), len(report.get("diagnostics") or [])


def orphans(console):
    lines = pathlib.Path(console).read_text(encoding="utf-8", errors="replace").splitlines()
    return [l.strip() for l in lines if "orphaned test ID" in l]


before, after = read(sys.argv[1]), read(sys.argv[2])
diffs = []
for name in sorted(set(before[0]) | set(after[0])):
    if name not in before[0] or name not in after[0]:
        diffs.append(f"scenario only {'after' if name in after[0] else 'before'}: {name}")
        continue
    (rb, cb), (ra, ca) = before[0][name], after[0][name]
    if rb != ra:
        diffs.append(f"{name}: result {rb} -> {ra}")
    if cb != ca:
        diffs.append(f"{name}: calls {sum(cb.values())} -> {sum(ca.values())}; gone {dict(cb - ca)}; new {dict(ca - cb)}")
if before[1:] != after[1:]:
    diffs.append(f"background calls {before[1]} -> {after[1]}, diagnostics {before[3]} -> {after[3]}; "
                 f"background gone {dict(before[2] - after[2])}; new {dict(after[2] - before[2])}")
print(f"{len(before[0])} scenarios before, {len(after[0])} after; "
      f"{sum(sum(c.values()) for _, c in before[0].values())} calls before, "
      f"{sum(sum(c.values()) for _, c in after[0].values())} after")
if len(sys.argv) > 4:
    print(f"orphan warning before: {orphans(sys.argv[3]) or 'none'}; after: {orphans(sys.argv[4]) or 'none'}")
print("\n".join(diffs) if diffs else "identical")
