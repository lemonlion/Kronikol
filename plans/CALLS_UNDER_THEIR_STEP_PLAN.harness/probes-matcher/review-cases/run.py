"""Each case through published and the change: every call's step (the request half, as `query interactions` lists it; with HALVES=1 also every record through `query http`), and the run's degraded flags.
Usage: run.py <cases dir> [name prefix ...]"""
import json, os, pathlib, subprocess, sys
os.environ["KRONIKOL_HISTORY"] = "off"; os.environ["KRONIKOL_KEEP_RUNS"] = "off"
sys.stdout.reconfigure(encoding="utf-8")
PUB = ["dotnet", "C:/Users/cex/.nuget/packages/kronikol.tool/5.28.2/tools/net10.0/any/Kronikol.Tool.dll"]
NEW = ["dotnet", "C:/Code/k225-m/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"]
cases = pathlib.Path(sys.argv[1]).resolve(); only = sys.argv[2:]
work = pathlib.Path("C:/Code/k225-rv/out").resolve()
HALVES = os.environ.get("HALVES") == "1"
def run(cmd, cwd=None):
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=cwd)
def read(tool, out):
    def q(*a):
        r = run([*tool, "query", *a, "--json", "--max-bytes", "0"])
        try: return json.loads(r.stdout)
        except Exception: return {"items": [], "raw": r.stdout[:300], "err": r.stderr[:300]}
    res = []
    for s in q("scenarios", str(out))["items"]:
        addr = s.get("address") or s.get("id")
        calls = []
        for r in q("interactions", str(out), addr)["items"]:
            sp = r.get("stepPath")
            calls.append(f"/{r['uri'].rsplit('/',1)[-1]}={sp if sp is not None else '-'}")
        res.append(f"{addr} " + " ".join(calls))
        if HALVES:
            # every record's own step: the response halves are only reachable by address
            halves = []
            for i in range(0, 40):
                r = run([*tool, "query", "http", str(out), f"{addr}/i{i}", "--max-bytes", "0"])
                if r.returncode != 0: break
                head = r.stdout.splitlines()
                step = [l for l in head if l.lower().strip().startswith("step")]
                kind = "Res" if any(l.strip().lower().startswith(("status", "response")) for l in head[:3]) else "?"
                halves.append(f"i{i}:{(head[0][:40] if head else '')} | {step[0].strip()[:40] if step else 'no step'}")
            res.append("   halves: " + " ;; ".join(halves))
    return res
for case in sorted(p for p in cases.iterdir() if p.is_dir()):
    if only and not any(case.name.startswith(o) for o in only): continue
    print(f"=== {case.name}")
    flags = (case/"flags.txt").read_text().split() if (case/"flags.txt").exists() else []
    results = {}
    for label, tool in (("pub", PUB), ("new", NEW)):
        out = work / label / case.name
        out.mkdir(parents=True, exist_ok=True)
        done = run([*tool, "ingest", "captures", "--tests", "tests.ndjson", "-o", str(out), "--no-component-diagram", "--render", "browserjs", *flags], cwd=case)
        service = out / "service.json"
        if done.returncode != 0 or not service.exists():
            print(f"  {label}: NO REPORT rc={done.returncode} {done.stdout[-300:]} {done.stderr[:400]}")
            results[label] = ([], "none"); continue
        results[label] = (read(tool, out), {"mismatch": sum("StepAttributionMismatch" in l for l in done.stdout.splitlines()), "degraded": json.loads(service.read_text(encoding="utf-8"))["run"].get("degraded") or {}})
    same = results["pub"] == results["new"]
    for label in ("pub", "new"):
        lines, d = results[label]
        print(f"  {label}{' (same)' if same else ''}: flags {d}")
        for l in lines: print(f"    {l}")
        if same: break
