#!/usr/bin/env python3
"""diagdiff.py <control-reports> <change-reports>: write each scenario's diagrams through the query tool, match them by
scenario name, normalise ids and times, and say which differ. Prints only names and short first differences, never a diagram."""
import json, os, re, subprocess, sys, tempfile, difflib, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
TOOL = ["dotnet", "C:/Code/Kronikol-225z/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll", "query"]

def run(*a):
    return subprocess.run(TOOL + list(a), capture_output=True, text=True, encoding="utf-8", errors="replace").stdout

def norm(text):
    text = re.sub(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "GUID", text)
    text = re.sub(r"\b\d+(\.\d+)?\s?ms\b", "N ms", text)
    text = re.sub(r"\b\d+(\.\d+)?\s?s\b", "N s", text)
    text = re.sub(r"\d{4}-\d\d-\d\dT[0-9:.]+Z?", "TIME", text)
    text = re.sub(r"traceparent=[0-9a-f-]+", "traceparent=T", text)
    return text

def scen(reports):
    items = json.loads(run("scenarios", reports, "--json", "--max-bytes", "0"))["items"]
    return {(i["feature"], i["scenario"]): i["address"] for i in items}

def diagrams(reports, addr, d):
    os.makedirs(d, exist_ok=True)
    run("diagram", reports, addr, "--out", d)
    out = {}
    for f in sorted(os.listdir(d)):
        if f.startswith(addr + "-") and f.endswith(".puml"):
            out[f[len(addr) + 1:]] = norm(open(os.path.join(d, f), encoding="utf-8", errors="replace").read())
    return out

def main(a, b):
    sa, sb = scen(a), scen(b)
    tmp = tempfile.mkdtemp(dir=os.environ.get("SCRATCH", None))
    same = diff = 0
    for key in sorted(set(sa) & set(sb), key=str):
        da = diagrams(a, sa[key], os.path.join(tmp, "a"))
        db = diagrams(b, sb[key], os.path.join(tmp, "b"))
        if da == db:
            same += 1
            continue
        diff += 1
        print("DIFFERS", key, "diagrams", len(da), len(db))
        for name in sorted(set(da) | set(db)):
            la = da.get(name, "").splitlines(); lb = db.get(name, "").splitlines()
            shown = 0
            for line in difflib.unified_diff(la, lb, lineterm="", n=0):
                if line.startswith(("---", "+++", "@@")):
                    continue
                print("   ", line[:150])
                shown += 1
                if shown >= 8:
                    print("    ...")
                    break
    print("same", same, "differ", diff, "only in one:", len(set(sa) ^ set(sb)))

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
