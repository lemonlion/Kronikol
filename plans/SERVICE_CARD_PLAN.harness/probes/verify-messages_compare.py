"""Compares the revised card written with --corrected-broker-rule against the revision's card for the same report.

Prints counts only: per card, the messages rows under each rule and the brokers of the rows the corrected rule drops,
whether behaviour less messages, and run less behaviourHash, are equal, and the compact and gzipped sizes under each.
Usage: python -I -X utf8 verify-messages_compare.py <revision cards dir> <corrected cards dir>
"""
import gzip
import json
import os
import sys

old_dir, new_dir = sys.argv[1], sys.argv[2]
names = sorted(n[: -len(".service.json")] for n in os.listdir(new_dir)
               if n.endswith(".service.json") and not n.endswith(".indented.json"))


def load(path):
    with open(path, "rb") as f:
        raw = f.read()
    return raw, json.loads(raw)


def gz(raw):
    return len(gzip.compress(raw, compresslevel=9, mtime=0))


print("card | messages before | after | dropped rows by broker | behaviour less messages equal | run less behaviourHash equal"
      " | compact before | after | change | gzipped before | after")
totals = {}
for name in names:
    old_raw, old = load(os.path.join(old_dir, name + ".service.json"))
    new_raw, new = load(os.path.join(new_dir, name + ".service.json"))
    om, nm = old["behaviour"]["messages"], new["behaviour"]["messages"]
    new_rows = {json.dumps(m, sort_keys=True) for m in nm}
    dropped = {}
    for m in om:
        if json.dumps(m, sort_keys=True) not in new_rows:
            dropped[m["party"]] = dropped.get(m["party"], 0) + 1
    added = len(new_rows - {json.dumps(m, sort_keys=True) for m in om})
    ob = {k: v for k, v in old["behaviour"].items() if k != "messages"}
    nb = {k: v for k, v in new["behaviour"].items() if k != "messages"}
    orun = {k: v for k, v in old["run"].items() if k != "behaviourHash"}
    nrun = {k: v for k, v in new["run"].items() if k != "behaviourHash"}
    drop_text = ", ".join(f"{p} {c}" for p, c in sorted(dropped.items())) or "none"
    if added:
        drop_text += f"; rows added {added}"
    print(f"{name} | {len(om)} | {len(nm)} | {drop_text} | {ob == nb} | {orun == nrun}"
          f" | {len(old_raw)} | {len(new_raw)} | {len(new_raw) - len(old_raw)} | {gz(old_raw)} | {gz(new_raw)}")
