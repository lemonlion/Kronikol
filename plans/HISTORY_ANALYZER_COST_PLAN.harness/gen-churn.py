import json, random, hashlib, sys
# A ledger the CI one is not: the roster changes on most runs (scenarios added, removed, shuffled),
# ids repeat (slots 0..2), call sets and fingerprints change, some runs are partial, some fail.
random.seed(11); POOL = 400; RUNS = 70
pool = [hashlib.md5(str(i).encode()).hexdigest()[:16] for i in range(POOL)]
dump = lambda o: json.dumps(o, separators=(",", ":"))
calls = [f"Caller>Svc GET /thing{i:02d} 200" for i in range(40)]
out = [dump({"t": "header", "historyFormatVersion": 1, "generator": "3.22.1"}),
       dump({"t": "shapes", "hash": "bbbbbbbbbbbbbbbb", "calls": calls})]
written = set()
members = random.sample(pool, 300)
state = {}
for r in range(RUNS):
    if r % 3 != 0:
        gone = random.sample(members, random.randrange(0, 12))
        members = [m for m in members if m not in gone] + random.sample([p for p in pool if p not in members], random.randrange(0, 12))
        if r % 5 == 0: random.shuffle(members)
    partial = r % 17 == 9
    ids = random.sample(members, 40) if partial else list(members)
    ids = ids + random.sample(ids, 15) + random.sample(ids, 5)      # repeats: slots 1 and 2
    if r % 4 == 0: random.shuffle(ids)
    seen = {}; slots = []
    for i in ids:
        slots.append(seen.get(i, 0)); seen[i] = seen.get(i, 0) + 1
    h = hashlib.sha256(("Churn\n" + "|".join(ids)).encode()).hexdigest()[:16]
    if h not in written:
        written.add(h)
        out.append(dump({"t": "roster", "hash": h, "suite": "Churn", "ids": ids, "slots": slots,
                         "names": [f"Scenario {i}" for i in ids], "features": [f"Feature {i[0]}" for i in ids], "sources": [None] * len(ids)}))
    res, sets, fps, counts = "", [], [], []
    for i in ids:
        cs = state.get(i) or sorted(random.sample(range(40), 4))
        roll = random.random()
        if roll < 0.04: cs = sorted(random.sample(range(40), 4))
        elif roll < 0.08: cs = sorted(set(cs) ^ {random.randrange(40)}) or [0]
        state[i] = cs
        sets.append(cs); fps.append(hashlib.md5(str(cs).encode()).hexdigest()[:8]); counts.append(len(cs) + (1 if random.random() < 0.03 else 0))
        res += "F" if random.random() < 0.05 else ("S" if random.random() < 0.01 else "P")
    out.append(dump({"t": "run", "id": f"gh:{1000 + r}:1", "suite": "Churn", "partial": partial,
        "at": f"2026-0{1 + r // 28}-{1 + r % 28:02d}T10:00:00Z", "branch": "main", "commit": f"c{r:06d}",
        "provider": "GitHubActions", "url": None, "shards": 1, "roster": h,
        "results": res, "attempts": "".join("2" if random.random() < 0.01 else "-" for _ in ids),
        "durations": [random.randrange(5, 900) * (4 if random.random() < 0.02 else 1) for _ in ids],
        "calls": counts, "shapeSet": fps, "shapeOrdered": [f if random.random() > 0.02 else f[::-1] for f in fps], "shapeVersion": 3,
        "shapes": "bbbbbbbbbbbbbbbb", "callSets": sets,
        "errors": [("e1" if c == "F" else None) for c in res],
        "errorText": {"e1": "expected the cake to be vegan but found eggs"}, "deps": ["Caller>Svc"]}))
open(sys.argv[1], "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
print(len(written), "rosters")
random.seed(5)
olds = random.sample(pool, 60)
news = random.sample([p for p in pool if p not in olds], 60)
json.dump({"aliases": dict(zip(olds, news))}, open(sys.argv[2], "w"))
