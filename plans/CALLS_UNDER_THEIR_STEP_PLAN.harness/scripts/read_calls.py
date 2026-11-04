"""Prints a report's step rows and each call's stepPath, through the query engine only.

Usage: read_calls.py <reports dir> <tool command...>
"""
import json
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8")
reports = sys.argv[1]
tool = sys.argv[2:]


def run(*args):
    return subprocess.run([*tool, "query", *args, "--max-bytes", "0"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout


scenarios = json.loads(run("scenarios", reports, "--json"))["items"]
for scenario in sorted(scenarios, key=lambda s: s.get("scenario") or ""):
    address = scenario["address"]
    print(f"--- {scenario.get('scenario')} [{scenario.get('result')}]")
    for line in run("steps", reports, address).splitlines():
        stripped = line.strip()
        if not stripped or stripped[0] in "!#" or stripped.startswith(("stableId", "open:", address + " ")) or " calls · " in line or stripped.startswith("calls"):
            continue
        print("    " + line.rstrip()[:96])
    calls = json.loads(run("interactions", reports, address, "--json"))["items"]
    print("    calls: " + "  ".join(f"{c['uri'].rstrip('/').rsplit('/', 1)[-1]}={c['stepPath']}" for c in calls))
