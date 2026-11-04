"""Reads the probe's TSV (scripts/probe_final_path.py): published's walk against the path a record ends with, split by whose flow answered.

Usage: differential_final.py <tsv>...
"""
import collections
import pathlib
import sys

sys.stdout.reconfigure(encoding="utf-8")

for path in sys.argv[1:]:
    rows = set()
    for line in pathlib.Path(path).read_text(encoding="utf-8").splitlines():
        parts = line.split("\t")
        if len(parts) >= 10:
            rows.add(tuple(parts[:10]))
    outcome = collections.Counter()
    samples = collections.defaultdict(list)
    deep = own = other = requests = 0
    for test, kind, call, walk, stamp, method, uri, final, caller, own_flow in rows:
        if kind == "Request":
            requests += 1
        if own_flow == "True":
            own += 1
        elif stamp not in ("unstamped", "unmapped"):
            other += 1
        if "." in final:
            deep += 1
        top = final.split(".")[0]
        if stamp in ("unstamped", "unmapped"):
            key = f"{stamp}: walk {'has a step' if walk != '-' else 'has none'}, final {'has one' if final != '-' else 'has none'}"
        elif top == walk:
            key = "same top-level step"
        elif walk == "-":
            key = "walk has none, final has one"
        elif final == "-":
            key = "walk has one, final has none"
        else:
            key = "DIFFERENT"
        outcome[key] += 1
        if key not in ("same top-level step",) and not key.endswith("walk has none, final has none") and len(samples[key]) < 8:
            samples[key].append(f"{kind} walk={walk} stamp={stamp} final={final} caller={caller} own={own_flow} {uri[:70]}")
    print(f"== {pathlib.Path(path).stem}: {len(rows)} records ({requests} requests), {own} answered by their own flow, {other} read from what was open, {deep} on a sub-step")
    for key, count in outcome.most_common():
        print(f"   {count:6}  {key}")
        for sample in samples[key]:
            print(f"           {sample}")
