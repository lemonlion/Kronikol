"""Compares two revised cards: behaviour byte-identical (the hashed bytes), identical without its scenario index, and
which behaviour members differ, with a count of scenario rows whose ops differ. Prints counts and member names only.
Usage: python -I -X utf8 revision-pairs_revised.py <a.service.json> <b.service.json> <label>"""
import json, sys
a, b = (json.load(open(p, encoding="utf-8")) for p in sys.argv[1:3])
ba, bb = a["behaviour"], b["behaviour"]
same = a["run"]["behaviourHash"] == b["run"]["behaviourHash"]
na = {k: v for k, v in ba.items() if k != "scenarios"}; nb = {k: v for k, v in bb.items() if k != "scenarios"}
members = sorted(k for k in set(ba) | set(bb) if ba.get(k) != bb.get(k))
ra = {r["sid"]: r for r in ba.get("scenarios", [])}; rb = {r["sid"]: r for r in bb.get("scenarios", [])}
ops_rows = sum(1 for s in set(ra) & set(rb) if ra[s]["ops"] != rb[s]["ops"])
title_rows = sum(1 for s in set(ra) & set(rb) if (ra[s]["feature"], ra[s]["scenario"]) != (rb[s]["feature"], rb[s]["scenario"]))
counts_differ = sum(1 for x, y in zip(sorted(a["run"]["counts"], key=lambda r: r["op"]), sorted(b["run"]["counts"], key=lambda r: r["op"])) if x != y)
print(f"{sys.argv[3]:42} behaviour {'same' if same else 'DIFF'}; without index {'same' if na == nb else 'DIFF'}; members differ {members}; "
      f"scenario rows: ops differ {ops_rows}, titles differ {title_rows}, only in one {len(set(ra) ^ set(rb))}; run.counts rows differ {counts_differ}")
# The operations present in one card only, by label (a templated target, cut at 80 characters), with their call counts.
oa = {o["op"]: (e["from"], e["to"], o["method"], o["target"][:80]) for e in ba["edges"] for o in e["operations"]}
ob = {o["op"]: (e["from"], e["to"], o["method"], o["target"][:80]) for e in bb["edges"] for o in e["operations"]}
ca = {c["op"]: c["calls"] for c in a["run"]["counts"]}; cb = {c["op"]: c["calls"] for c in b["run"]["counts"]}
for op in sorted(set(oa) ^ set(ob)):
    side, label, calls = ("first", oa[op], ca.get(op)) if op in oa else ("second", ob[op], cb.get(op))
    print(f"    only in the {side}: {label[0]} > {label[1]} {label[2]} {label[3]} ({calls} calls)")
