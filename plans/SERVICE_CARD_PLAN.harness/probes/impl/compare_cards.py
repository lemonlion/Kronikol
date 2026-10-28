"""Compare the implementation's service.json with the revision probe's card, lane by lane.

Usage: python -X utf8 compare_cards.py <impl-card.json> <probe-card.json>
Prints one summary line and, per member, what differs (operations keyed by from/to/method/target, ignoring op ids).
"""
import json
import sys


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


impl, probe = load(sys.argv[1]), load(sys.argv[2])
bi, bp = impl["behaviour"], probe["behaviour"]


def ops(card):
    out = {}
    for e in card["behaviour"]["edges"]:
        for o in e["operations"]:
            key = (e["from"], e["to"], e["protocol"], o["method"], o["target"], o.get("graphqlOperation"))
            out[key] = {k: o[k] for k in ("statuses", "origins", "causes", "effectivePhases")}
    return out


def by_op(card):
    """op id -> (from, to, method, target, graphql)"""
    out = {}
    for e in card["behaviour"]["edges"]:
        for o in e["operations"]:
            out[o["op"]] = (e["from"], e["to"], o["method"], o["target"], o.get("graphqlOperation"))
    return out


diffs = []
if bi["subject"] != bp["subject"]:
    diffs.append(f"subject {bi['subject']} vs {bp['subject']}")
pi = {p["name"]: p for p in bi["parties"]}
pp = {p["name"]: p for p in bp["parties"]}
for name in sorted(set(pi) | set(pp)):
    if pi.get(name) != pp.get(name):
        diffs.append(f"party {name}: {pi.get(name)} vs {pp.get(name)}")
ei = {(e["from"], e["to"], e["protocol"]) for e in bi["edges"]}
ep = {(e["from"], e["to"], e["protocol"]) for e in bp["edges"]}
if ei != ep:
    diffs.append(f"edges only impl {sorted(ei - ep)} only probe {sorted(ep - ei)}")
oi, op_ = ops(impl), ops(probe)
graphql = lambda k: k[4].startswith("/graphql")
only_i = [k for k in oi if k not in op_ and not graphql(k)]
only_p = [k for k in op_ if k not in oi and not graphql(k)]
if only_i or only_p:
    diffs.append(f"ops only impl {only_i[:6]} (+{max(0, len(only_i) - 6)}) only probe {only_p[:6]} (+{max(0, len(only_p) - 6)})")
for k in sorted(set(oi) & set(op_)):
    if oi[k] != op_[k]:
        diffs.append(f"op {k[0]}>{k[1]} {k[3]} {k[4][:60]}: {oi[k]} vs {op_[k]}")
mi_ops, mp_ops = by_op(impl), by_op(probe)
mi = sorted((mi_ops[m["op"]][:4], m["direction"], m["party"], m["topic"]) for m in bi["messages"])
mp = sorted((mp_ops[m["op"]][:4], m["direction"], m["party"], m["topic"]) for m in bp["messages"])
if mi != mp:
    diffs.append(f"messages only impl {[m for m in mi if m not in mp]} only probe {[m for m in mp if m not in mi]}")
xi = sorted(mi_ops[o][:4] for o in bi["exposes"]["ops"] if not mi_ops[o][3].startswith("/graphql"))
xp = sorted(mp_ops[o][:4] for o in bp["exposes"]["ops"] if not mp_ops[o][3].startswith("/graphql"))
if xi != xp:
    diffs.append(f"exposes only impl {[x for x in xi if x not in xp]} only probe {[x for x in xp if x not in xi]}")
if bi["capture"]["calls"] != bp["capture"]["calls"] or bi["capture"]["subjectCalls"] != bp["capture"]["subjectCalls"]:
    diffs.append(f"capture {bi['capture']} vs {bp['capture']}")
ce_i = sorted(mi_ops[o][:4] for o in bi["capture"]["emptyTargets"])
ce_p = sorted(mp_ops[o][:4] for o in bp["capture"]["emptyTargets"])
if ce_i != ce_p:
    diffs.append(f"emptyTargets {ce_i} vs {ce_p}")
if bi["unresolved"] != bp["unresolved"]:
    diffs.append(f"unresolved {bi['unresolved']} vs {bp['unresolved']}")
si = {s["sid"]: (s["feature"], s["scenario"], s["tags"], s["source"], sorted(mi_ops[o][:4] for o in s["ops"] if not mi_ops[o][3].startswith("/graphql"))) for s in bi["scenarios"]}
sp = {s["sid"]: (s["feature"], s["scenario"], s["tags"], s["source"], sorted(mp_ops[o][:4] for o in s["ops"] if not mp_ops[o][3].startswith("/graphql"))) for s in bp["scenarios"]}
bad = [sid for sid in set(si) | set(sp) if si.get(sid) != sp.get(sid)]
if bad:
    sid = sorted(bad)[0]
    diffs.append(f"scenarios differ on {len(bad)} rows, e.g. {sid}: {si.get(sid)} vs {sp.get(sid)}")
ri, rp = impl["run"], probe["run"]
for key in ("results", "environment", "suite", "degraded"):
    if ri.get(key) != rp.get(key):
        diffs.append(f"run.{key} {ri.get(key)} vs {rp.get(key)}")
hi = {p["name"]: (p["hosts"], p["doubleCalls"]) for p in ri["parties"]}
hp = {p["name"]: (p["hosts"], p["doubleCalls"]) for p in rp["parties"]}
if hi != hp:
    diffs.append(f"run.parties {[(n, hi.get(n), hp.get(n)) for n in sorted(set(hi) | set(hp)) if hi.get(n) != hp.get(n)]}")

print(f"parties {len(bi['parties'])}/{len(bp['parties'])} edges {len(ei)}/{len(ep)} ops {len(oi)}/{len(op_)} "
      f"messages {len(bi['messages'])}/{len(bp['messages'])} exposes {len(bi['exposes']['ops'])}/{len(bp['exposes']['ops'])} "
      f"scenarios {len(bi['scenarios'])}/{len(bp['scenarios'])} diffs {len(diffs)}")
for d in diffs:
    print("   ", d[:400])
