import os, re, sys
for path in sys.argv[1:]:
    t = open(path, encoding="utf-8", errors="replace").read()
    parts = re.split(r"^### ", t, flags=re.M)
    ok = bad = 0
    badlist = []
    for i, p in enumerate(parts):
        if not re.match(r"s\d+ calls", p):
            continue
        steps = parts[i - 1]
        m = re.search(r"P (P0\d) (T\d)( \d)?", steps)
        if not m:
            continue
        who = m.group(1)
        rows = [l.split() for l in p.splitlines() if re.match(r"^[0-9-]+\s+POST", l)]
        got = {r[0]: r[2] for r in rows}
        want = {"0": f"/{who}/given", "1": f"/{who}/when", "2": f"/{who}/then"}
        if got == want:
            ok += 1
        else:
            bad += 1
            badlist.append((m.group(0), got))
    print(os.path.basename(path), "ok", ok, "bad", bad, badlist[:3])
