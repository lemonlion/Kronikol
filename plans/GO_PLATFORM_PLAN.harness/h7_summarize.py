"""Summarise a `go test -json` stream: the non-output events per test, and the output lines that matter
to a capturer. Reads stdin. Standard library only."""
import json, sys

KEEP = ("cached", "panic", "timed out", "TestMain", "cleanup:", "t.Name()", "H7_RUN_ID", "FAIL", "undefined",
        "want", "ATTR", "pid")
seen = {}
for raw in sys.stdin:
    raw = raw.strip()
    if not raw:
        continue
    try:
        e = json.loads(raw)
    except json.JSONDecodeError:
        print("  [not json] " + raw[:120])
        continue
    pkg = (e.get("Package") or e.get("ImportPath") or "").split("/")[-1]
    act, test = e.get("Action"), e.get("Test")
    if act == "output":
        line = e.get("Output", "").rstrip("\n")
        if any(k in line for k in KEEP):
            print(f"  {pkg:>7} {test or '-':<34} output  {line.strip()[:110]}")
        continue
    extra = ""
    if "Elapsed" in e and test is None:
        extra = f" elapsed={e['Elapsed']}"
    if e.get("FailedBuild"):
        extra += f" FailedBuild={e['FailedBuild']}"
    if act in ("attr",):
        extra += f" {e.get('Key')}={e.get('Value')}"
    print(f"  {pkg:>7} {test or '-':<34} {act}{extra}")
