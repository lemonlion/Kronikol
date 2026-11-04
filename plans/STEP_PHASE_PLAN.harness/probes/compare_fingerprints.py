"""Compares two runs' interaction fingerprints, scenario by scenario.

    python -I compare_fingerprints.py <before History.run.json> <after History.run.json>

History.run.json is the fragment a run writes for the cross-run ledger: `roster` names the scenarios and `run` holds one
entry per scenario, in roster order, in shapeSet and shapeOrdered. A scenario is matched on its stable id, not its position,
as a run's order can differ. The rule's version is printed too: a change of rule is what makes a comparison quiet.
"""
import json
import sys


def load(path):
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    roster = doc["roster"]
    entries = roster.get("scenarios") or roster.get("ids") or roster if isinstance(roster, dict) else roster
    if isinstance(entries, dict):
        entries = entries.get("s") or entries.get("names") or next(v for v in entries.values() if isinstance(v, list))
    names = [e if isinstance(e, str) else (e.get("id") or e.get("stableId") or e.get("title") or json.dumps(e)) for e in entries]
    run = doc["run"]
    return doc, dict(zip(names, zip(run["shapeSet"], run["shapeOrdered"], run["calls"])))


def main():
    before_doc, before = load(sys.argv[1])
    after_doc, after = load(sys.argv[2])
    print(f"shapeVersion {before_doc['run']['shapeVersion']} -> {after_doc['run']['shapeVersion']}, generators "
          f"{before_doc['run']['generator'].split('+')[0]} -> {after_doc['run']['generator'].split('+')[0]}")
    same = 0
    for name in sorted(before):
        if name not in after:
            print(f"  only before: {name}")
        elif before[name] == after[name]:
            same += 1
        else:
            print(f"  MOVED {name[:70]}: calls {before[name][2]} -> {after[name][2]}; set {before[name][0]} -> {after[name][0]}")
    print(f"{same} of {len(before)} scenarios keep their fingerprints")


main()
