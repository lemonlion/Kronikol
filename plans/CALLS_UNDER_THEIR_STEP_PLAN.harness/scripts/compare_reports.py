"""Two reports of one suite, scenario by scenario, by stable id: the step tree (paths, text, result) and each call's step.

Usage: compare_reports.py <published reports dir> <prototype reports dir> <decode command...>
The decode command takes a .krkl and -o <file>, and the JSON is read here and never printed: only the differences are, under a cap.
A top-level step is compared as a path's first segment, so a call that now reads a sub-step of the same step is counted apart from one that moved.
"""
import collections
import json
import pathlib
import re
import subprocess
import sys
import tempfile

sys.stdout.reconfigure(encoding="utf-8")
left, right, *decode = sys.argv[1:]


def load(folder):
    folder = pathlib.Path(folder)
    source = folder / "TestRunReport.json"
    if not source.exists():
        with tempfile.TemporaryDirectory(dir=folder.parent) as temp:
            out = pathlib.Path(temp) / "r.json"
            subprocess.run([*decode, str(folder / "TestRunReport.krkl"), "-o", str(out)], check=True, capture_output=True)
            return json.loads(out.read_text(encoding="utf-8"))
    return json.loads(source.read_text(encoding="utf-8"))


def walk(steps, prefix, separator):
    for i, step in enumerate(steps or []):
        path = f"{prefix}{separator}{i}"
        yield path, step
        yield from walk(step.get("subSteps"), path, ".")


def scenarios(report):
    found = {}
    for feature in report["features"]:
        for scenario in feature["scenarios"]:
            found[scenario.get("stableId") or scenario["id"]] = scenario
    return found


# What a run generates: ids in a path or an assertion's text differ between any two runs of one version.
GENERATED = re.compile(r"[0-9a-fA-F]{8}[-_][0-9a-fA-F]{4}[-_][0-9a-fA-F]{4}[-_][0-9a-fA-F]{4}[-_][0-9a-fA-F]{12}|[0-9a-fA-F]{32}|\d{6,}")


def norm(text):
    return GENERATED.sub("#", text) if text else text


def tree(scenario):
    rows = list(walk(scenario.get("backgroundSteps"), "b", "")) + list(walk(scenario.get("steps"), "", ""))
    return [(path, step.get("keyword"), norm(step.get("text")), step.get("status")) for path, step in rows]


def calls(scenario):
    return [(i.get("method"), norm((i.get("uri") or "").split("?")[0]), i.get("stepPath")) for i in scenario.get("httpInteractions") or [] if i.get("type") == "Request"]


a, b = scenarios(load(left)), scenarios(load(right))
count = collections.Counter()
samples = collections.defaultdict(list)
for key in sorted(set(a) | set(b)):
    if key not in a or key not in b:
        count["scenario in one report only"] += 1
        samples["scenario in one report only"].append(f"{key} {(a.get(key) or b.get(key)).get('name')}")
        continue
    x, y = a[key], b[key]
    count["scenarios compared"] += 1
    if x.get("result") != y.get("result"):
        count["result differs"] += 1
        samples["result differs"].append(f"{x.get('name')}: {x.get('result')} -> {y.get('result')}")
    tx, ty = tree(x), tree(y)
    if tx != ty:
        count["tree differs"] += 1
        only = [t for t in tx if t not in ty][:3] + [("+",) + t for t in ty if t not in tx][:3]
        samples["tree differs"].append(f"{x.get('name')}: {only}")
    cx, cy = collections.Counter(calls(x)), collections.Counter(calls(y))
    by_call_x = collections.defaultdict(list)
    by_call_y = collections.defaultdict(list)
    for (method, uri, path), n in cx.items():
        by_call_x[(method, uri)] += [path] * n
    for (method, uri, path), n in cy.items():
        by_call_y[(method, uri)] += [path] * n
    for call in set(by_call_x) | set(by_call_y):
        px, py = sorted(by_call_x[call], key=str), sorted(by_call_y[call], key=str)
        count["calls compared"] += min(len(px), len(py))
        if len(px) != len(py):
            count["a call's count differs (the run's own wobble, or a real change)"] += 1
            samples["a call's count differs (the run's own wobble, or a real change)"].append(f"{x.get('name')}: {call} {len(px)} -> {len(py)}")
        for p, q in zip(px, py):
            if p == q:
                count["call reads the same"] += 1
            elif p is None:
                count["published had no step, prototype has one"] += 1
                samples["published had no step, prototype has one"].append(f"{x.get('name')[:50]}: {call[1][-40:]} -> {q}")
            elif q is None:
                count["PUBLISHED HAD A STEP, PROTOTYPE HAS NONE"] += 1
                samples["PUBLISHED HAD A STEP, PROTOTYPE HAS NONE"].append(f"{x.get('name')[:50]}: {call[1][-40:]} {p} -> none")
            elif q.split(".")[0] == p.split(".")[0]:
                count["same top-level step, now a sub-step"] += 1
            else:
                count["MOVED TO ANOTHER TOP-LEVEL STEP"] += 1
                samples["MOVED TO ANOTHER TOP-LEVEL STEP"].append(f"{x.get('name')[:50]}: {call[1][-40:]} {p} -> {q}")

for key, value in count.most_common():
    print(f"{value:7}  {key}")
    for sample in samples[key][:6]:
        print(f"           {sample[:170]}")
