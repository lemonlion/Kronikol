"""For the run pairs audit-A-ops_diff.py counts (same suite, commit, roster and shape version, neither partial,
consecutive), the templated targets of the Cosmos DB Replace operations that differ between the two runs, and which
run of each pair holds them. Prints counts and templated targets only (the ledger's call lines hold no bodies).
Usage: python -I -X utf8 verify-replace_targets.py <history.jsonl>
"""
import collections
import json
import sys

runs, tables = [], {}
with open(sys.argv[1], encoding="utf-8") as f:
    for line in f:
        o = json.loads(line)
        if o.get("t") == "run":
            runs.append(o)
        elif o.get("t") == "shapes":
            tables[o["hash"]] = o["calls"]
groups = collections.defaultdict(list)
for r in runs:
    groups[(r.get("suite"), r.get("commit"), r.get("roster"), r.get("shapeVersion"))].append(r)


def ops(run):
    calls = tables.get(run.get("shapes"))
    return None if calls is None else {c.rsplit(" ", 1)[0] for c in calls}


targets = collections.Counter()
pairs = 0
for rs in groups.values():
    rs = sorted(rs, key=lambda r: r["at"])
    for a, b in zip(rs, rs[1:]):
        if a.get("partial") or b.get("partial"):
            continue
        oa, ob = ops(a), ops(b)
        if oa is None or ob is None or oa == ob:
            continue
        diff = [x for x in (oa ^ ob) if ">CosmosDB Replace " in x]
        if diff:
            pairs += 1
            for x in diff:
                targets[(x.split(" Replace ", 1)[1], "first run" if x in oa else "second run")] += 1
print(f"pairs whose distinct operations differ by a Cosmos DB Replace: {pairs}")
for (target, where), n in targets.most_common():
    print(f"  {n:3}  Replace {target}  (held by the {where} only)")
