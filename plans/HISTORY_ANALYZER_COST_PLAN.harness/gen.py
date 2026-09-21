import json, random, hashlib, sys
# The issue's generator, with the scenario count as an argument.
N = int(sys.argv[1]); RUNS = 60
random.seed(3)
ids = [hashlib.md5(str(i).encode()).hexdigest()[:16] for i in range(N)]
dump = lambda o: json.dumps(o, separators=(",", ":"))
out = [dump({"t": "header", "historyFormatVersion": 1, "generator": "3.21.0"}),
       dump({"t": "roster", "hash": "aaaaaaaaaaaaaaaa", "suite": "Big", "ids": ids, "slots": [0] * N,
             "names": [f"Scenario number {i} does the expected thing" for i in range(N)],
             "features": [f"Feature {i // 25}" for i in range(N)], "sources": [None] * N}),
       dump({"t": "shapes", "hash": "bbbbbbbbbbbbbbbb", "calls": [f"Caller>Svc GET /thing{i} 200" for i in range(40)]})]
shape = [f"{random.getrandbits(32):08x}" for _ in range(N)]
call_sets = [[random.randrange(40) for _ in range(5)] for _ in range(N)]
for r in range(RUNS):
    res = "".join("F" if random.random() < 0.004 else "P" for _ in range(N))
    out.append(dump({"t": "run", "id": f"gh:{1000 + r}:1", "suite": "Big", "partial": False,
        "at": f"2026-0{1 + r // 60}-{1 + r % 28:02d}T10:00:00Z", "branch": "main", "commit": "abc",
        "provider": "GitHubActions", "url": None, "shards": 1, "roster": "aaaaaaaaaaaaaaaa",
        "results": res, "attempts": "-" * N, "durations": [random.randrange(5, 900) for _ in range(N)],
        "calls": [5] * N, "shapeSet": shape, "shapeOrdered": shape, "shapeVersion": 3,
        "shapes": "bbbbbbbbbbbbbbbb", "callSets": call_sets,
        "errors": [("e1" if c == "F" else None) for c in res],
        "errorText": {"e1": "expected the cake to be vegan but found eggs"}, "deps": ["Caller>Svc"]}))
open(f"big{N}.jsonl", "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
