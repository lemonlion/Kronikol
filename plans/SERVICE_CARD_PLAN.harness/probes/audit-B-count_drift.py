"""Per lane pair: how many operations' call counts and status counts differ between the two runs (the `run` part's
counts), next to whether the behaviour hash is equal. Usage: python -I -X utf8 count_drift.py <dir> <x> <y> <lane>..."""
import json, sys
d, x, y = sys.argv[1], sys.argv[2], sys.argv[3]
for lane in sys.argv[4:]:
    a = json.load(open(f"{d}/{x}-{lane}.detail.json", encoding="utf-8"))["ops"]
    b = json.load(open(f"{d}/{y}-{lane}.detail.json", encoding="utf-8"))["ops"]
    ha = open(f"{d}/{x}-{lane}.hash.txt").read().split()[0]; hb = open(f"{d}/{y}-{lane}.hash.txt").read().split()[0]
    common = set(a) & set(b)
    calls = sum(a[o]["calls"] != b[o]["calls"] for o in common)
    stat = sum(a[o]["statuses"] != b[o]["statuses"] for o in common)
    ta = sum(v["calls"] for v in a.values()); tb = sum(v["calls"] for v in b.values())
    print(f"  {x}-vs-{y} {lane:20} requests {ta} vs {tb}; ops {len(common)} in both; call count differs on {calls}, status counts on {stat}; behaviour equal {ha == hb}")
