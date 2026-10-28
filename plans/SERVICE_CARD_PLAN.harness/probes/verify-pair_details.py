"""The details behind plan 2.4 and 4.3 that results/revision-pairs.txt does not print, from the revised cards of the pairs
(revision-pairs-revised.sh's output) and the decoded data files. Prints counts, party names, operations' methods and
templated targets, and scenario titles; never a body or a header's value other than the one header counted.
Usage: python -I -X utf8 verify-pair_details.py <pair cards dir> <decoded dir of run C> <decoded dir of the local runs>
"""
import json
import os
import re
import sys
from collections import Counter

cards_dir, decoded_c, decoded_local = sys.argv[1], sys.argv[2], sys.argv[3]


def card(name):
    with open(os.path.join(cards_dir, name + ".service.json"), encoding="utf-8") as f:
        return json.load(f)


def ops_of(c):
    out = {}
    for e in c["behaviour"]["edges"]:
        for o in e["operations"]:
            out[o["op"]] = (e["from"], e["to"], o["method"], o["target"], o["graphqlOperation"], o["effectivePhases"])
    return out


print("== Root background block: requests per card (the probe's 'background-section requests')")
counts = Counter()
for name in sorted(n[:-4] for n in os.listdir(cards_dir) if n.endswith(".txt")):
    with open(os.path.join(cards_dir, name + ".txt"), encoding="utf-8") as f:
        m = re.search(r"background-section requests (\d+)", f.read())
    counts[int(m.group(1))] += 1
    if int(m.group(1)):
        print(f"  {name}: {m.group(1)}")
print(f"  cards {sum(counts.values())}: " + ", ".join(f"{n} with {k}" for k, n in sorted(counts.items())))

print("== Titles across 5.4.1 (run B) and 5.12.1 (run C), same sid, the Reqnroll lanes")
for lane in ("reqnroll-in-memory", "reqnroll-in-docker", "reqnroll-external-sut"):
    b, c = card("B-" + lane), card("C-" + lane)
    bs = {s["sid"]: s for s in b["behaviour"]["scenarios"]}
    rows = [(bs[s["sid"]]["feature"], s["feature"]) for s in c["behaviour"]["scenarios"]
            if s["sid"] in bs and bs[s["sid"]]["feature"] != s["feature"]]
    other = sum(1 for s in c["behaviour"]["scenarios"] if s["sid"] in bs
                and (bs[s["sid"]]["scenario"], bs[s["sid"]]["tags"], bs[s["sid"]]["source"]) != (s["scenario"], s["tags"], s["source"]))
    print(f"  {lane}: rows whose feature title differs {len(rows)}, distinct titles {len(set(rows))};"
          f" rows whose scenario name, tags or source differ {other}")
    if lane == "reqnroll-in-memory":
        for old, new in sorted(set(rows)):
            print(f"    {old!r} -> {new!r}")

print("== The menu cache: scenarios of the test's own GET /menu, and which of them reached Supplier Service")
for name in ("A-reqnroll-in-memory", "B-reqnroll-in-memory", "L1-reqnroll-in-memory", "L2-reqnroll-in-memory"):
    c = card(name)
    ops = ops_of(c)
    menu = {op for op, v in ops.items() if v[0] == "Caller" and v[1] == "Breakfast Provider" and v[2] == "GET" and v[3] == "/menu"}
    supplier = {op for op, v in ops.items() if v[0] == "Breakfast Provider" and v[1] == "Supplier Service"}
    run_rows = {s["sid"]: set(s["ops"]) for s in c["run"]["scenarios"]}
    menu_rows = [s["sid"] for s in c["behaviour"]["scenarios"] if menu & set(s["ops"])]
    misses = [sid for sid in menu_rows if run_rows.get(sid, set()) & supplier]
    print(f"  {name}: scenarios calling GET /menu {len(menu_rows)}, of which reached Supplier Service {len(misses)}")

print("== The recorded phase across 5.4.1 (run B) and 5.12.1 (run C), the LightBDD lanes: operations whose effective phases differ")
for lane in ("lightbdd-in-memory", "lightbdd-in-docker", "lightbdd-external-sut"):
    b, c = ops_of(card("B-" + lane)), ops_of(card("C-" + lane))
    diff = [(c[op], b[op][5]) for op in sorted(set(b) & set(c)) if b[op][5] != c[op][5]]
    print(f"  {lane}: {len(diff)}")
    for (frm, to, method, target, gql, phases), before in diff:
        print(f"    {frm} > {to} {method} {target[:60]}{' ' + gql if gql else ''}: {before} -> {phases}")


def platform(path):
    with open(path, encoding="utf-8") as f:
        root = json.load(f)
    calls, scenarios, header = 0, set(), 0
    for feature in root.get("features") or []:
        for s in feature.get("scenarios") or []:
            for i in s.get("httpInteractions") or []:
                if (i.get("type") == "Request" and "Cosmos" in (i.get("serviceName") or "") and i.get("method") == "Other"
                        and (i.get("uri") or "").rstrip("/").endswith("/colls/orders")
                        and str(i.get("content") or "").lstrip().startswith('{"query"')):
                    calls += 1
                    scenarios.add(s.get("stableId"))
                    for h in i.get("headers") or []:
                        key = h.get("key") if isinstance(h, dict) else (h[0] if isinstance(h, list) and h else None)
                        value = h.get("value") if isinstance(h, dict) else (h[1] if isinstance(h, list) and len(h) > 1 else None)
                        if str(key).lower() == "x-ms-cosmos-is-query-plan-request" and str(value) == "True":
                            header += 1
    return calls, len(scenarios), header


print("== One commit, two platforms (68a8760, xunit-in-memory): Breakfast Provider > CosmosDB Other /colls/orders with a {\"query\":...} body")
for label, path in (("run C, Linux", os.path.join(decoded_c, "xunit-in-memory.json")),
                    ("L1, Windows", os.path.join(decoded_local, "xunit-in-memory-run1.json")),
                    ("L2, Windows", os.path.join(decoded_local, "xunit-in-memory-run2.json"))):
    calls, scenarios, header = platform(path)
    print(f"  {label}: calls {calls} in {scenarios} scenarios; carrying x-ms-cosmos-is-query-plan-request: True {header}")
