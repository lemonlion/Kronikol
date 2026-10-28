"""Compares two runs' service-card behaviour (card-behaviour.cs output) member by member, classifies each difference,
and measures Q6: how much of the scenario index's churn background calls cause.

Usage: python -I -X utf8 behaviour_diff.py <prefixA> <prefixB> [label]
Each prefix names <prefix>.behaviour.json, <prefix>.detail.json and <prefix>.hash.txt.
Prints labels and counts; op labels are templated targets cut at 140 characters, never bodies.

Classes:
  background  every call of the op (in the scenario, for the index) is cause=background: the subject's or a broker's
              call outside any call its caller received (nesting first)
  delivery    the calls are a broker's delivery or ran inside one (consumer work), cause request
  real        anything else that differs in presence: a call made inside a request or by the test
  ordering    a set member that flips with the order records were captured in (cause, effective phase of a nested call)
  rare        a status or other set value carried by one or two calls in the run that has it
"""
import json
import sys
from collections import Counter, defaultdict


def load(prefix):
    b = json.load(open(prefix + ".behaviour.json", encoding="utf-8"))
    d = json.load(open(prefix + ".detail.json", encoding="utf-8"))
    h = open(prefix + ".hash.txt", encoding="utf-8").read().split()
    return b, d, h


def ops_of(b):
    out = {}
    for e in b["edges"]:
        for o in e["operations"]:
            out[o["op"]] = dict(o, edge=(e["from"], e["to"], e["protocol"]))
    return out


def scen_ops(d, exclude=None):
    """sid -> set of ops, built from the detail; `exclude(opdetail)` -> number of the op's calls in the scenario to drop."""
    out = defaultdict(set)
    for key, s in d["scenarios"].items():
        for op, x in s["ops"].items():
            keep = x["calls"] - (exclude(x) if exclude else 0)
            if keep > 0:
                out[s["sid"]].add(op)
    return out


def classify_presence(x):
    """x: an op's detail (run level or in one scenario)."""
    calls = x["calls"]
    causes = x.get("causes", {})
    if causes.get("background", 0) == calls:
        return "background"
    if x.get("viaDelivery", 0) == calls:
        return "delivery"
    return "real"


def main(pa, pb, label):
    ba, da, ha = load(pa)
    bb, db, hb = load(pb)
    print(f"#### {label}")
    print(f"behaviourHash equal: {ha[0] == hb[0]}   without scenarios: {ha[1] == hb[1]}")
    diffs = Counter()

    for k in ("rules", "subject", "unresolved", "capture"):
        if ba[k] != bb[k]:
            print(f"  {k} differs: A {json.dumps(ba[k])[:200]} | B {json.dumps(bb[k])[:200]}")
            diffs[k] += 1
    pa_ = {p["name"]: p for p in ba["parties"]}
    pb_ = {p["name"]: p for p in bb["parties"]}
    for n in sorted(set(pa_) | set(pb_)):
        if pa_.get(n) != pb_.get(n):
            print(f"  party {n}: A {pa_.get(n)} | B {pb_.get(n)}")
            diffs["parties"] += 1

    ea = {(e["from"], e["to"], e["protocol"]) for e in ba["edges"]}
    eb = {(e["from"], e["to"], e["protocol"]) for e in bb["edges"]}
    for e in sorted(ea - eb):
        print(f"  edge only in A: {e}"); diffs["edges"] += 1
    for e in sorted(eb - ea):
        print(f"  edge only in B: {e}"); diffs["edges"] += 1

    oa, ob = ops_of(ba), ops_of(bb)
    presence = Counter()
    for side, only, d in (("A", sorted(set(oa) - set(ob)), da), ("B", sorted(set(ob) - set(oa)), db)):
        for op in only:
            x = d["ops"][op]
            cls = classify_presence(x)
            presence[cls] += 1
            diffs["ops"] += 1
            print(f"  op only in {side} [{cls}]: {x['label']}  calls {x['calls']} scen {x['scenarios']} causes {x['causes']} attr {x['attributions']} statuses {x['statuses']}")
    setdiff = Counter()
    for op in sorted(set(oa) & set(ob)):
        for member, dkey in (("statuses", "statuses"), ("origins", "origins"), ("causes", "causes"), ("effectivePhases", "phases")):
            va, vb = set(oa[op][member]), set(ob[op][member])
            if va == vb:
                continue
            diffs["opsets"] += 1
            for side, extra, d in (("A", va - vb, da), ("B", vb - va, db)):
                for v in sorted(extra):
                    x = d["ops"][op]
                    n = x[dkey].get(v, 0)
                    if member == "statuses":
                        cls = "rare" if n <= 2 else "real"
                    elif member == "effectivePhases":
                        cls = "background" if x["causes"].get("background", 0) > 0 and x["causes"].get("request", 0) == 0 else "ordering"
                    elif member == "causes":
                        cls = "ordering"
                    else:
                        cls = "real"
                    setdiff[(member, cls)] += 1
                    print(f"  {member} +{v!r} only in {side} [{cls}] x{n}: {x['label']}  (calls {x['calls']}, causes {x['causes']}, phases {x['phases']})")

    for k in ("exposes",):
        sa, sb = set(ba[k]["ops"]), set(bb[k]["ops"])
        if sa != sb:
            print(f"  exposes: only A {sorted(sa - sb)} only B {sorted(sb - sa)}"); diffs["exposes"] += 1
    ma = {json.dumps(m, sort_keys=True) for m in ba["messages"]}
    mb = {json.dumps(m, sort_keys=True) for m in bb["messages"]}
    if ma != mb:
        print(f"  messages: only A {sorted(ma - mb)} only B {sorted(mb - ma)}"); diffs["messages"] += 1

    # The scenario index.
    sa = {s["sid"]: s for s in ba["scenarios"]}
    sb = {s["sid"]: s for s in bb["scenarios"]}
    for sid in sorted(set(sa) ^ set(sb)):
        print(f"  scenario only in {'A' if sid in sa else 'B'}: {sid}"); diffs["scenarioRows"] += 1
    meta_diff = 0
    for sid in sorted(set(sa) & set(sb)):
        ma_ = {k: v for k, v in sa[sid].items() if k != "ops"}
        mb_ = {k: v for k, v in sb[sid].items() if k != "ops"}
        if ma_ != mb_:
            meta_diff += 1
            print(f"  scenario metadata differs {sid}: A {json.dumps(ma_)[:200]} | B {json.dumps(mb_)[:200]}")
    churn = [sid for sid in sorted(set(sa) & set(sb)) if set(sa[sid]["ops"]) != set(sb[sid]["ops"])]
    res_a, res_b = da["scenarioResults"], db["scenarioResults"]
    failed_either = {sid for sid in churn if res_a.get(sid) != "Passed" or res_b.get(sid) != "Passed"}
    # Per differing op in a churned scenario, its class in the run that has it.
    det_a = {s["sid"]: s for s in da["scenarios"].values()}
    det_b = {s["sid"]: s for s in db["scenarios"].values()}
    occ = Counter()
    scen_class = Counter()
    examples = defaultdict(list)
    for sid in churn:
        classes = set()
        for side, extra, det, d in (("A", set(sa[sid]["ops"]) - set(sb[sid]["ops"]), det_a, da), ("B", set(sb[sid]["ops"]) - set(sa[sid]["ops"]), det_b, db)):
            for op in extra:
                x = det[sid]["ops"][op]
                cls = classify_presence(x)
                classes.add(cls)
                occ[cls] += 1
                if len(examples[cls]) < 400:
                    examples[cls].append(d["ops"][op]["label"][:110])
        scen_class["+".join(sorted(classes))] += 1

    bg = lambda x: x.get("causes", {}).get("background", 0)
    bg_or_delivery = lambda x: max(x.get("causes", {}).get("background", 0), x.get("viaDelivery", 0)) if x.get("causes", {}).get("background", 0) == 0 or x.get("viaDelivery", 0) == 0 else x["calls"]
    def churn_with(exclude_a, exclude_b):
        A, B = scen_ops(da, exclude_a), scen_ops(db, exclude_b)
        return [sid for sid in sorted(set(sa) & set(sb)) if A.get(sid, set()) != B.get(sid, set())]
    c_nobg = churn_with(bg, bg)
    test_only = lambda x: x["calls"] - x.get("causes", {}).get("test", 0)
    c_test = churn_with(test_only, test_only)
    nondeliv_nobg = lambda x: min(x["calls"], bg(x) + x.get("viaDelivery", 0))
    c_nobg_nodeliv = churn_with(nondeliv_nobg, nondeliv_nobg)

    print(f"  scenario index: {len(sa)} rows; metadata differs {meta_diff}; ops differ in {len(churn)} "
          f"({len(failed_either)} of them failed in a run)")
    print(f"    churned scenarios by the classes of their differing ops: {dict(scen_class)}")
    print(f"    differing op occurrences by class: {dict(occ)}")
    for cls, ex in examples.items():
        top = Counter(ex).most_common(8)
        print(f"      {cls}: " + "; ".join(f"{n}x {l}" for l, n in top))
    print(f"    churn with background calls out of scenario ops: {len(c_nobg)}; with background and delivery work out: {len(c_nobg_nodeliv)}; "
          f"with only the test's own calls: {len(c_test)}")
    if c_nobg:
        print(f"    still churned without background: {c_nobg[:12]}")
    print(f"  op presence differences by class: {dict(presence)}; op set-member differences by (member, class): {dict(setdiff)}")
    print(f"  members that differ: {dict(diffs)}")
    summary = {
        "label": label, "hashEqual": ha[0] == hb[0], "hashEqualNoScenarios": ha[1] == hb[1],
        "edgesDiff": diffs["edges"], "opsPresenceDiff": dict(presence), "opSetDiff": {f"{m}/{c}": n for (m, c), n in setdiff.items()},
        "exposesDiff": diffs["exposes"], "messagesDiff": diffs["messages"], "partiesDiff": diffs["parties"],
        "scenarios": len(sa), "churn": len(churn), "churnFailed": len(failed_either), "churnNoBackground": len(c_nobg),
        "churnNoBackgroundNoDelivery": len(c_nobg_nodeliv), "churnTestOnly": len(c_test), "churnClasses": dict(scen_class),
        "occurrences": dict(occ),
    }
    print("SUMMARY " + json.dumps(summary))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else sys.argv[2])
