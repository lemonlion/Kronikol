"""Each case of a folder through one or more builds of `kronikol ingest`: every call's step, and what the run flagged. Prints one line per scenario per build.

Usage: run_cases.py <cases dir> <work dir> <label=command> [<label=command>...] [--only=A,B]
A command is what comes before `ingest`, such as "dotnet C:/x/Kronikol.Tool.dll".
A case's flags.txt is passed to the ingest.
"""
import json
import os
import pathlib
import shlex
import subprocess
import sys

os.environ["KRONIKOL_HISTORY"] = "off"
os.environ["KRONIKOL_KEEP_RUNS"] = "off"
sys.stdout.reconfigure(encoding="utf-8")

args = [a for a in sys.argv[1:] if not a.startswith("--only")]
only = next((a.split("=", 1)[1].split(",") for a in sys.argv[1:] if a.startswith("--only=")), None)
cases, work, builds = pathlib.Path(args[0]).resolve(), pathlib.Path(args[1]).resolve(), [a.split("=", 1) for a in args[2:]]


def run(cmd, cwd=None):
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=cwd)


def read(tool, out):
    def q(*a):
        return json.loads(run([*tool, "query", *a, "--json", "--max-bytes", "0"]).stdout)

    lines = []
    for s in q("scenarios", str(out))["items"]:
        address = s.get("address") or s.get("id")
        rows = q("interactions", str(out), address)["items"]
        seen, calls = set(), []
        for r in rows:
            key = (r["uri"], r.get("stepPath"))
            if key in seen:
                continue
            seen.add(key)
            calls.append(f"/{r['uri'].rsplit('/', 1)[-1]}={r.get('stepPath') if r.get('stepPath') is not None else '-'}")
        lines.append(f"{address} " + " ".join(calls))
    return lines


for case in sorted(p for p in cases.iterdir() if p.is_dir()):
    if only and not any(case.name.startswith(o + "-") or case.name == o for o in only):
        continue
    print(f"=== {case.name}")
    results = {}
    for label, command in builds:
        tool = shlex.split(command, posix=False)
        out = work / label / case.name
        out.mkdir(parents=True, exist_ok=True)
        flags = (case / "flags.txt").read_text(encoding="utf-8").split() if (case / "flags.txt").exists() else []
        done = run([*tool, "ingest", "captures", "--tests", "tests.ndjson", "-o", str(out), "--no-component-diagram", "--render", "browserjs", *flags], cwd=case)
        service = out / "service.json"
        degraded = json.loads(service.read_text(encoding="utf-8"))["run"].get("degraded") if service.exists() else f"NO REPORT {done.returncode}"
        results[label] = (read(tool, out) if service.exists() else [], degraded)
    first = next(iter(results.values()))
    same = all(r == first for r in results.values())
    for label, (lines, degraded) in results.items():
        print(f"  {label}: flags {degraded or '{}'}" + ("  (all builds the same)" if same else ""))
        for line in lines:
            print(f"    {line}")
        if same:
            break
