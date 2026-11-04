"""For a case: what the tests file and capture hold, in time order, and the rows and calls the build reads. To judge a difference by hand against what really ran.

Usage: explain.py <case dir> <tool dll> <reports dir> [scenarios]
"""
import json
import os
import pathlib
import subprocess
import sys

os.environ["KRONIKOL_HISTORY"] = "off"
os.environ["KRONIKOL_KEEP_RUNS"] = "off"
sys.stdout.reconfigure(encoding="utf-8")
case, tool, out = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
scenarios = int(sys.argv[4]) if len(sys.argv) > 4 else 1

print(f"### {case.name}")
for line in (case / "tests.ndjson").read_text(encoding="utf-8").splitlines():
    r = json.loads(line)
    if r.get("event") in ("step", "assertion"):
        print(f"  tests {r.get('testId')}: {r['event']} kw={r.get('keyword')} text={(r.get('text') or '')[:44]!r} t={(r.get('timestamp') or '-')[17:23]} bg={r.get('background')} level={r.get('level')} attempt={r.get('attempt')} status={r.get('status')}")
for f in (case / "captures").iterdir():
    for line in f.read_text(encoding="utf-8").splitlines():
        r = json.loads(line)
        if r.get("markerEnd") or r.get("type") == "Response":
            continue
        what = r.get("uri", "").rsplit("/", 1)[-1] if r.get("kind") not in ("marker", "step") else f"BAR {r.get('kind')} {(r.get('plantUml') or r.get('text') or '')[-40:]!r}"
        print(f"  capture {r.get('testId')}: {what} t={(r.get('timestamp') or '-')[17:23]}")
for i in range(scenarios):
    done = subprocess.run(["dotnet", tool, "query", "steps", out, f"s{i}"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    for line in done.stdout.splitlines():
        if line.startswith(("stableId", "open:", "at ")) or not line.strip():
            continue
        print("  " + line[:92])
