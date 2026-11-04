"""One ingest case through one or more builds, with what run_cases.py leaves out: the step rows, and every record's step, responses included.

Usage: run_one_case.py <case dir> <work dir> <label=command> [<label=command>...]
A command is what comes before `ingest`, such as "dotnet C:/x/Kronikol.Tool.dll". A case's flags.txt is passed to the ingest.
Reports are read only through the query engine.
"""
import json
import os
import pathlib
import re
import shlex
import subprocess
import sys

os.environ["KRONIKOL_HISTORY"] = "off"
os.environ["KRONIKOL_KEEP_RUNS"] = "off"
sys.stdout.reconfigure(encoding="utf-8")

case, work = pathlib.Path(sys.argv[1]).resolve(), pathlib.Path(sys.argv[2]).resolve()
builds = [a.split("=", 1) for a in sys.argv[3:]]
flags = (case / "flags.txt").read_text(encoding="utf-8").split() if (case / "flags.txt").exists() else []


def run(cmd, cwd=None):
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=cwd)


print(f"=== {case.name}{' ' + ' '.join(flags) if flags else ''}")
for label, command in builds:
    tool = shlex.split(command, posix=False)
    out = work / label / case.name
    out.mkdir(parents=True, exist_ok=True)
    run([*tool, "ingest", "captures", "--tests", "tests.ndjson", "-o", str(out), "--no-component-diagram", "--render", "browserjs", *flags], cwd=case)
    print(f"  {label}:")
    for line in run([*tool, "query", "steps", str(out), "s0"]).stdout.splitlines():
        if re.match(r"\s+(~|✗)?\s*b?\d", line):
            print("    " + line.strip()[:90])
    rows = json.loads(run([*tool, "query", "interactions", str(out), "s0", "--json", "--max-bytes", "0"]).stdout)["items"]
    # A request and its response are one row here, at the request's address. The record after it is the response.
    for row in rows:
        address = row.get("address") or row.get("id")
        number = int(address.rsplit("/i", 1)[1])
        for at in (number, number + 1):
            text = run([*tool, "query", "http", str(out), f"s0/i{at}"]).stdout
            kind = next((part for part in ("Request", "Response") if part in text.split("\n", 1)[0]), "?")
            step = next((line[len("in step "):].strip() for line in text.splitlines() if line.startswith("in step ")), "-")
            print(f"    i{at} {kind:8} {row['uri'].rsplit('/', 1)[-1]:8} step {step}")
