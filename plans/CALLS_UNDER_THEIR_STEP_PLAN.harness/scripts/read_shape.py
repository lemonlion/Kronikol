import json, subprocess, sys
tool = sys.argv[2:]; R = sys.argv[1]
def q(*a):
    return json.loads(subprocess.run([*tool, "query", *a, "--json", "--max-bytes", "0"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout)
rows = q("interactions", R, "s0")["items"]
seen, out = set(), []
for r in rows:
    k = (r["uri"], r.get("stepPath"))
    if k in seen: continue
    seen.add(k); out.append(f"/{r['uri'].rsplit('/', 1)[-1]}={r.get('stepPath') if r.get('stepPath') is not None else '-'}")
print(" ".join(out))
