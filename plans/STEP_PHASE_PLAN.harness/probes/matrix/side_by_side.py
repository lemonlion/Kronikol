"""Prints every labelled point of every test across the variants, one line each, so a change can be read at a glance.

    python -I side_by_side.py <results folder> <variant> [<variant> ...]

Each cell is the HTTP call's phase, a slash, and the message's: S Setup, A Action, U Unknown, - not recorded. A trailing i
means the handler's TrackDuringSetup or TrackDuringAction dropped the call. A cell is blank where that variant has no such
point. Under each test, `boundary` counts the Setup/Action markers per variant, and `marker before` names the first
labelled point after the first marker, which is where the diagram's Setup partition ends.
"""
import pathlib
import re
import sys

PROBES = ["reqnroll-xunit2", "xunit3-plain", "lightbdd-xunit2", "bddfy-xunit3"]
ROW = re.compile(r"^(?P<label>\S+)\s+(?P<http>Setup|Action|Unknown|-)\s+(?P<msg>Setup|Action|Unknown|-)\s*(?P<ignored>IGNORED)?\s*$")
SHORT = {"Setup": "S", "Action": "A", "Unknown": "U", "-": "-"}


def read(path):
    tests, order, current = {}, [], None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            name = line[3:]
            current = {"rows": {}, "order": [], "markers": 0, "before": ""}
            tests[name] = current
            order.append(name)
        elif line.startswith("steps:"):
            current = None
        elif current is None:
            continue
        elif "ACTION START MARKER" in line:
            current["markers"] += 1
            current["armed"] = current["markers"] == 1
        else:
            m = ROW.match(line)
            if m:
                cell = f"{SHORT[m['http']]}/{SHORT[m['msg']]}" + ("i" if m["ignored"] else "")
                current["rows"][m["label"]] = cell
                current["order"].append(m["label"])
                if current.pop("armed", False):
                    current["before"] = m["label"]
    return tests, order


def main():
    root, variants = pathlib.Path(sys.argv[1]), sys.argv[2:]
    for probe in PROBES:
        print(f"==== {probe}")
        data = {v: read(root / v / f"{probe}.txt") for v in variants}
        names = data[variants[0]][1]
        for name in names:
            print(f"## {name}")
            labels = []
            for v in variants:
                for label in data[v][0].get(name, {"order": []})["order"]:
                    if label not in labels:
                        labels.append(label)
            print(f"  {'':<34}" + "".join(f"{v:>9}" for v in variants))
            for label in labels:
                print(f"  {label:<34}" + "".join(f"{data[v][0].get(name, {'rows': {}})['rows'].get(label, ''):>9}" for v in variants))
            print(f"  {'boundary':<34}" + "".join(f"{data[v][0].get(name, {'markers': 0})['markers']:>9}" for v in variants))
            befores = {v: data[v][0].get(name, {"before": ""})["before"] for v in variants}
            if len(set(befores.values())) > 1:
                print(f"  marker before: " + ", ".join(f"{v}={befores[v] or 'none'}" for v in variants))


main()
