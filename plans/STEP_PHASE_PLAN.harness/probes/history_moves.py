"""Recomputes which scenario's fingerprints move at each stage from results/history/<stage>/<suite>.json."""
import json
import pathlib

H = pathlib.Path(r"C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/31780689-26c4-4084-b1b5-c4efb9a6fd42/scratchpad/rr/results/history")
SUITES = ["reqnroll-xunit2", "xunit3-plain", "lightbdd-xunit2", "bddfy-xunit3"]


def load(path):
    doc = json.loads(path.read_text(encoding="utf-8"))
    roster = doc["roster"]
    run = doc["run"]
    names = roster["names"]
    return doc, {n: (s, o, c) for n, s, o, c in zip(names, run["shapeSet"], run["shapeOrdered"], run["calls"])}


for stage in ["5.28.3-r1", "5.28.3-r2", "5.28.3-r3"]:
    print("==", stage)
    for suite in SUITES:
        d0, base = load(H / "5.28.2" / f"{suite}.json")
        d1, now = load(H / stage / f"{suite}.json")
        same = 0
        for name in base:
            if name not in now:
                print("  only base", suite, name)
            elif base[name] == now[name]:
                same += 1
            else:
                kind = "SET" if base[name][0] != now[name][0] else "order"
                print(f"  {kind:5} {suite:16} {name[:60]:60} calls {base[name][2]} -> {now[name][2]}")
        print(f"  {suite}: {same} of {len(base)} keep; version {d0['run']['shapeVersion']} -> {d1['run']['shapeVersion']}; generator {d1['run']['generator'][:40]}")
