"""Compares the marker walk's top-level step with the stamp's, per record, from the TSV the probe writes."""
import collections
import pathlib
import sys

for path in sys.argv[1:]:
    rows = set()
    for line in pathlib.Path(path).read_text(encoding="utf-8").splitlines():
        parts = line.split("\t")
        if len(parts) >= 7:
            rows.add(tuple(parts[:7]))
    outcome = collections.Counter()
    samples = collections.defaultdict(list)
    deep = 0
    for test, kind, call, walk, stamp, method, uri in rows:
        if stamp in ("unstamped", "unmapped"):
            key = f"{stamp}, walk {'has a step' if walk != '-' else 'has none'}"
        elif stamp == "none":
            key = "both say no step" if walk == "-" else "stamp says no step, walk says the bar above"
        else:
            top = stamp.split(".")[0]
            if "." in stamp:
                deep += 1
            key = "same top-level step" if top == walk else ("walk has none, stamp has one" if walk == "-" else "DIFFERENT")
        outcome[key] += 1
        if key not in ("same top-level step", "unstamped, walk has none", "both say no step") and len(samples[key]) < 6:
            samples[key].append(f"{kind} walk={walk} stamp={stamp} {uri[:70]}")
    print(f"== {pathlib.Path(path).stem}: {len(rows)} records, {deep} on a sub-step")
    for key, count in outcome.most_common():
        print(f"   {count:6}  {key}")
        for sample in samples[key]:
            print(f"           {sample}")
