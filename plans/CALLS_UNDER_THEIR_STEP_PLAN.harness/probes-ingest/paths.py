import json,subprocess,sys
tool=sys.argv[2:]; R=sys.argv[1]
def q(*a):
    return json.loads(subprocess.run([*tool,"query",*a,"--json","--max-bytes","0"],capture_output=True,text=True,encoding="utf-8").stdout)
items=q("scenarios",R)["items"]
for s in items:
    a=s.get("address") or s.get("id")
    rows=q("interactions",R,a)["items"]
    print(a, (s.get("name") or s.get("title") or "")[:44].ljust(44), " ".join(f"{r['uri'].rsplit('/',1)[-1]}={r['stepPath']}" for r in rows))
