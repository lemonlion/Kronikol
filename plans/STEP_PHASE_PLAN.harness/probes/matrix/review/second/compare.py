import pathlib, re, sys
root = pathlib.Path(sys.argv[1])
def read(path):
    tests, cur = {}, None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            cur = line[3:].rsplit(".", 1)[-1]
            tests[cur] = []
        elif cur is not None and line.strip():
            tests[cur].append(line.rstrip())
    return tests
for name in ("plain", "lightbdd"):
    a, b = read(root / "5.27.13" / f"{name}.txt"), read(root / "5.27.14-r3" / f"{name}.txt")
    for t in a:
        print("=" * 8, t)
        la, lb = a[t], b.get(t, [])
        print("--- 5.27.13"); print("\n".join(l[:64] for l in la if not l.startswith("call")))
        print("--- r3"); print("\n".join(l[:64] for l in lb if not l.startswith("call")))
