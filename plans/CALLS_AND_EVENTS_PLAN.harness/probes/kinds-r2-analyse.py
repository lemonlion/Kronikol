"""Reads kinds-by-tracker.tsv (5.27.13 at Detailed) beside kinds-by-tracker's output on the prototype at each verbosity (made with
on-prototype.sh, VERBOSITY=Raw and so on, named r2-<verbosity>.tsv in one folder), and holds each row to section 5.4 and to section 6.7 of
plans/CALLS_AND_EVENTS_PLAN.md. Prints plain text. Run with PYTHONIOENCODING=utf-8: the labels hold arrows.

usage: kinds-r2-analyse.py results/kinds-by-tracker.tsv <folder of r2-*.tsv>"""
import csv
import sys
from collections import Counter, defaultdict

BROKERS = {
    "Kronikol.Extensions.SQS": "SQS", "Kronikol.Extensions.SNS": "SNS", "Kronikol.Extensions.EventBridge": "EventBridge",
    "Kronikol.Extensions.StorageQueues": "StorageQueues", "Kronikol.Extensions.PubSub": "PubSub",
    "Kronikol.Extensions.EventHubs": "EventHubs", "Kronikol.Extensions.Kafka": "Kafka",
    "Kronikol.Extensions.MassTransit": "MassTransit", "Kronikol.Extensions.ServiceBus": "ServiceBus",
}


def rows(path):
    with open(path, encoding="utf-8", newline="") as f:
        return [r for r in csv.DictReader(f, delimiter="\t")]


def op_name(row):
    return row["operation"].split("[")[0]


today_path, r2_dir = sys.argv[1], sys.argv[2]
today = rows(today_path)
r2 = {v: rows(f"{r2_dir}/r2-{v}.tsv") for v in ["Detailed", "Raw", "Summarised"]}


def key(row):
    return (row["package"], row["route"], row["operation"])


print("== what moved, Detailed, request kind: 5.27.13 -> the prototype (operations, one row per route)")
today_by = {key(r): r for r in today}
moved = defaultdict(list)
for r in r2["Detailed"]:
    t = today_by.get(key(r))
    if t is None or r["package"] not in BROKERS:
        continue
    if t["requestKind"] != r["requestKind"]:
        moved[(BROKERS[r["package"]], t["requestKind"], r["requestKind"], t["tableVerdict"])].append(op_name(r))
to_event = to_default = 0
for k in sorted(moved):
    ops = sorted(set(moved[k]))
    print(f"{k[0]:<14} {k[1]:>7} -> {k[2]:<7} table says {k[3]:<9} {len(ops):>2}  {', '.join(ops)}")
    if k[2] == "Event":
        to_event += len(ops)
    else:
        to_default += len(ops)
print(f"to Event {to_event}, to Default {to_default}")
print("  to Default by verdict:", dict(Counter(k[3] for k in moved for _ in set(moved[k]) if k[2] == "Default")))

print()
print("== a response whose kind differs from its request's")
for v, table in r2.items():
    diff = [r for r in table if r["package"] in BROKERS and r["responseLogged"] == "true" and r["requestKind"] != r["responseKind"]]
    print(f"{v:<11} {len(diff)}")
diff = [r for r in today if r["package"] in BROKERS and r["responseLogged"] == "true" and r["requestKind"] != r["responseKind"]]
print(f"5.27.13 Detailed {len(diff)}")

print()
print("== the literal theory of 6.7 over each verbosity: Event exactly when the table says Publishes or Receives")
print("   (a row is a broker record the prototype wrote. 'Unknown' under the table = it cannot read the label or URI)")
for v, table in r2.items():
    disagree = defaultdict(list)
    total = Counter()
    for r in table:
        if r["package"] not in BROKERS or r["requestKind"] in ("NO-RECORD", "NOT-DRIVEN"):
            continue
        total[BROKERS[r["package"]]] += 1
        literal = "Event" if r["tableVerdict"] in ("Publishes", "Receives") else "Default"
        if literal != r["requestKind"]:
            disagree[(BROKERS[r["package"]], r["tableVerdict"], r["uriScheme"], r["requestKind"])].append((op_name(r), r["label"]))
    print(f"-- {v}: {sum(total.values())} rows, {sum(len(x) for x in disagree.values())} disagree")
    for k in sorted(disagree):
        ops = sorted({o for o, _ in disagree[k]})
        labels = sorted({l for _, l in disagree[k]})
        print(f"   {k[0]:<13} scheme {k[2]:<11} table {k[1]:<8} kind {k[3]:<7} {len(ops):>2} ops: {', '.join(ops)}   labels: {', '.join(labels[:4])}{' ...' if len(labels) > 4 else ''}")

print()
print("== one label under one scheme carrying both kinds (the path is not compared here: readers-of-kind measures the collapse key)")
for v, table in r2.items():
    by = defaultdict(set)
    for r in table:
        if r["package"] in BROKERS and r["requestKind"] not in ("NO-RECORD", "NOT-DRIVEN"):
            by[(BROKERS[r["package"]], r["label"], r["uriScheme"])].add(r["requestKind"])
    both = sorted(k for k, kinds in by.items() if len(kinds) > 1)
    print(f"{v:<11} {len(both)} {both}")

print()
print("== the theory by the operation: each operation's expected kind comes from 5.27.13's Detailed row (the table says Publishes or")
print("   Receives, or it is a delivery, Consume), and is held at every verbosity, where the label may be one the table cannot read")
expected = {}
for r in today:
    if r["package"] in BROKERS and r["requestKind"] not in ("NO-RECORD", "NOT-DRIVEN"):
        moves = r["tableVerdict"] in ("Publishes", "Receives") or op_name(r) == "Consume"
        expected[(r["package"], op_name(r))] = "Event" if moves else "Default"
for v, table in r2.items():
    wrong = [(BROKERS[r["package"]], op_name(r), r["requestKind"]) for r in table
             if r["package"] in BROKERS and r["requestKind"] not in ("NO-RECORD", "NOT-DRIVEN")
             and expected.get((r["package"], op_name(r)), r["requestKind"]) != r["requestKind"]]
    print(f"{v:<11} {len(wrong)} of {sum(1 for r in table if r['package'] in BROKERS and r['requestKind'] not in ('NO-RECORD', 'NOT-DRIVEN'))} disagree {wrong}")

print()
print("== Detailed: every row against 5.27.13, on every column but the kinds and the span kind (the ones history and the card read)")
others = ["label", "category", "statementShaped", "uriScheme", "responseLogged", "tableVerdict", "otlpDbSystem"]
differ = [(key(r), c) for r in r2["Detailed"] if key(r) in today_by for c in others if today_by[key(r)][c] != r[c]]
print(f"{len(r2['Detailed'])} rows, {len(differ)} differ {differ}")
print(f"rows only in one: {sorted(set(map(key, today)) ^ set(map(key, r2['Detailed'])))}")

print()
print("== OTLP span kind by verdict, Detailed: 5.27.13 -> the prototype (broker rows)")
trans = Counter()
for r in r2["Detailed"]:
    t = today_by.get(key(r))
    if t is None or r["package"] not in BROKERS or t["otlpSpanKind"] == r["otlpSpanKind"]:
        continue
    trans[(BROKERS[r["package"]], t["otlpSpanKind"], r["otlpSpanKind"], t["tableVerdict"])] += 1
for k, n in sorted(trans.items()):
    print(f"{k[0]:<14} {k[1]:>8} -> {k[2]:<8} table says {k[3]:<9} {n}")
