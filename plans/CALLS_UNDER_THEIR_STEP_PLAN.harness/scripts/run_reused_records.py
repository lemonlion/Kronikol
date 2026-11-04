"""probes-matcher/reused-records: an ingest over a host's records, twice over the same objects, on each build. Prints each ingest's calls.

Usage: run_reused_records.py <InProc.dll> <work dir> <reader Kronikol.Tool.dll> <label=folder holding Kronikol.dll> ...
The probe is built once, against any build's Kronikol.dll, and run against the folder it is given. Reports are read only through the query engine.
"""
import json
import os
import pathlib
import shutil
import subprocess
import sys

os.environ["KRONIKOL_HISTORY"] = "off"
os.environ["KRONIKOL_KEEP_RUNS"] = "off"
sys.stdout.reconfigure(encoding="utf-8")
probe, work, reader = sys.argv[1], pathlib.Path(sys.argv[2]).resolve(), sys.argv[3]


def run(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", **kw)


for pair in sys.argv[4:]:
    label, folder = pair.split("=", 1)
    root = work / label
    shutil.rmtree(root, ignore_errors=True)
    done = run(["dotnet", probe, folder, "reuse"], env={**os.environ, "REUSE_OUT": str(root)})
    print(f"=== {label}")
    if not root.exists():
        print("  no reports:", (done.stdout + done.stderr)[-300:])
        continue
    base = root / os.listdir(root)[0]
    for name in sorted(os.listdir(base)):
        rows = json.loads(run(["dotnet", reader, "query", "interactions", str(base / name), "s0", "--json", "--max-bytes", "0"]).stdout)["items"]
        print(f"  {name}: " + " ".join(f"/{r['uri'].rsplit('/', 1)[-1]}={r.get('stepPath') if r.get('stepPath') is not None else '-'}" for r in rows))
