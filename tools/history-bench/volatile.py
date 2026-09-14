"""Which scenario identities are VOLATILE by construction? CROSS_RUN_HISTORY_PLAN §2.9.

`stableId` hashes feature::[outline::]name::sorted(k=v). Any example value that changes between runs
mints a new id every run, so the scenario can never accrue history and the roster grows without bound.
This finds example values that LOOK run-varying, statically, from reports already on disk.

Usage: python volatile.py <TestRunReport.json> [...]
"""
import json, re, sys, collections

PATTERNS = [
    ("guid",      re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")),
    ("hex32",     re.compile(r"^[0-9a-fA-F]{32}$")),
    ("iso-time",  re.compile(r"^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}")),
    ("epoch-ms",  re.compile(r"^1[6-9]\d{11}$")),
    ("long-digits", re.compile(r"^\d{10,}$")),
]

tally, examples, scenarios, parameterised = collections.Counter(), {}, 0, 0
for path in sys.argv[1:]:
    d = json.load(open(path, encoding="utf-8-sig"))
    for f in d.get("features", []):
        for s in f.get("scenarios", []):
            scenarios += 1
            ev = s.get("exampleValues") or {}
            if ev: parameterised += 1
            for k, v in ev.items():
                for name, rx in PATTERNS:
                    if isinstance(v, str) and rx.match(v.strip()):
                        tally[name] += 1
                        examples.setdefault(name, (s.get("displayName"), k, v))

print("%d scenarios, %d parameterised (%.1f%%)" % (scenarios, parameterised, 100.0*parameterised/max(scenarios,1)))
if not tally:
    print("\nNo run-varying example values found: every parameterised id is stable by construction.")
else:
    for name, n in tally.most_common():
        who, k, v = examples[name]
        print("  %-12s %4d   e.g. %s  [%s = %s]" % (name, n, who, k, v))
