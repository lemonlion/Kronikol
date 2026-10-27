"""How much of a run's call record survives a second run of the same code.

Reads a Kronikol history ledger (history.jsonl from a kronikol-history branch) and compares every pair of
consecutive run lines of one suite on one commit, with the same roster and shape version and neither partial.
For each pair it asks what a per-run summary file would have to change if it carried:

  - deps:      the distinct caller>service pairs of the run (the ledger's `deps`)
  - set:       per scenario, the set of distinct templated calls (`shapeSet`)
  - ordered:   per scenario, the calls in order (`shapeOrdered`)
  - counts:    per scenario, how many calls (`calls`)

Prints counts only, never names or calls. Usage: python -I ledger_stability.py <history.jsonl>
"""
import collections
import json
import sys


def main(path):
    runs = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            o = json.loads(line)
            if o.get("t") == "run":
                runs.append(o)

    groups = collections.defaultdict(list)
    for r in runs:
        key = (r.get("suite"), r.get("commit"), r.get("roster"), r.get("shapeVersion"))
        groups[key].append(r)

    pairs = 0
    pair_changed = collections.Counter()
    slots_compared = 0
    slot_changed = collections.Counter()
    count_deltas = collections.Counter()
    suites = collections.Counter()
    for key, rs in groups.items():
        rs = sorted(rs, key=lambda r: r["at"])
        for a, b in zip(rs, rs[1:]):
            if a.get("partial") or b.get("partial"):
                continue
            if a.get("shapeVersion") is None:
                continue
            pairs += 1
            suites[key[0]] += 1
            if sorted(a.get("deps") or []) != sorted(b.get("deps") or []):
                pair_changed["deps"] += 1
            changed_here = collections.Counter()
            n = min(len(a["calls"]), len(b["calls"]))
            for i in range(n):
                # a slot holds a scenario only when both runs ran it and both passed it
                ra, rb = a["results"][i], b["results"][i]
                if ra != "P" or rb != "P":
                    continue
                if a["calls"][i] is None or b["calls"][i] is None:
                    continue
                slots_compared += 1
                if a["calls"][i] != b["calls"][i]:
                    slot_changed["counts"] += 1
                    changed_here["counts"] += 1
                    count_deltas[abs(a["calls"][i] - b["calls"][i])] += 1
                if a["shapeSet"][i] != b["shapeSet"][i]:
                    slot_changed["set"] += 1
                    changed_here["set"] += 1
                if a["shapeOrdered"][i] != b["shapeOrdered"][i]:
                    slot_changed["ordered"] += 1
                    changed_here["ordered"] += 1
            for k in changed_here:
                pair_changed[k] += 1

    print(f"run lines: {len(runs)}; same-commit consecutive pairs compared: {pairs} over {len(suites)} suites")
    print(f"passed-in-both scenario slots compared: {slots_compared}")
    for k in ("counts", "set", "ordered"):
        print(f"  slots whose {k:8} differ: {slot_changed[k]:6}  ({100.0 * slot_changed[k] / max(slots_compared, 1):.2f}%)")
    for k in ("deps", "counts", "set", "ordered"):
        print(f"  run pairs where any {k:8} differ: {pair_changed[k]:4} of {pairs}  ({100.0 * pair_changed[k] / max(pairs, 1):.1f}%)")
    print("  size of a count change (|delta| -> slots):", dict(sorted(count_deltas.items())[:12]))
    print("  pairs per suite:", dict(sorted(suites.items())))

    run_level(path, groups)


def run_level(path, groups):
    """The run as one summary sees it: the distinct calls of the whole run, not of each scenario.

    A line of the ledger's shapes table is `Caller>Service Method Uri Status`, Uri templated by InteractionShape.
    Compared with the status, without it (the operation alone), and cut to `Caller>Service` (the edge).
    """
    tables = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            o = json.loads(line)
            if o.get("t") == "shapes":
                tables[o["hash"]] = o["calls"]

    def views(run):
        calls = tables.get(run.get("shapes"))
        if calls is None:
            return None
        with_status = set(calls)
        # The status is the last token. The edge cannot be cut from the line, since party names hold spaces
        # (`Breakfast Provider>Cow Service GET /milk 200`): the run line's own `deps` is the edge list.
        operation = {c.rsplit(" ", 1)[0] for c in calls}
        edge = set(run.get("deps") or [])
        return {"calls+status": with_status, "operations": operation, "edges": edge}

    pairs = 0
    changed = collections.Counter()
    sizes = collections.Counter()
    for key, rs in groups.items():
        rs = sorted(rs, key=lambda r: r["at"])
        for a, b in zip(rs, rs[1:]):
            if a.get("partial") or b.get("partial"):
                continue
            va, vb = views(a), views(b)
            if va is None or vb is None:
                continue
            pairs += 1
            for k in va:
                if va[k] != vb[k]:
                    changed[k] += 1
            sizes["operations"] += len(va["operations"])
            sizes["edges"] += len(va["edges"])
    print(f"run level (pairs with a shapes table on both lines): {pairs}")
    for k in ("calls+status", "operations", "edges"):
        print(f"  run pairs whose distinct {k:12} differ: {changed[k]:4} of {pairs}  ({100.0 * changed[k] / max(pairs, 1):.1f}%)")
    if pairs:
        print(f"  mean distinct operations per run: {sizes['operations'] / pairs:.1f}; mean distinct edges: {sizes['edges'] / pairs:.1f}")


if __name__ == "__main__":
    main(sys.argv[1])
