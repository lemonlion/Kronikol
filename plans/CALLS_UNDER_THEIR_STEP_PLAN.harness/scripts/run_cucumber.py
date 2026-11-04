"""The checked-in Cucumber Messages runs, with a capture of one call per test step (kronikol-fe's calls.py), through two builds: each call's step, with and without --include-hooks.

Usage: run_cucumber.py <test data dir> <captures dir, holding one folder per run> <work dir> <label=command> <label=command>
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

data, captures, work = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]).resolve(), pathlib.Path(sys.argv[3]).resolve()
builds = [a.split("=", 1) for a in sys.argv[4:]]


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


runs = [
    ("main", data / "playwright-bdd-9.2-messages.ndjson", None, captures / "cap-main", None),
    ("att", data / "playwright-bdd-9.2-attachments" / "messages.ndjson", data / "playwright-bdd-9.2-attachments" / "tests.ndjson", captures / "cap-att", data / "playwright-bdd-9.2-attachments"),
]
same = differ = 0
for name, messages, tests, capture, base in runs:
    for hooks in (False, True):
        results = {}
        for label, command in builds:
            tool = shlex.split(command, posix=False)
            out = work / label / f"{name}-{'hooks' if hooks else 'nohooks'}"
            out.mkdir(parents=True, exist_ok=True)
            args = [*tool, "ingest", str(capture), "--cucumber-messages", str(messages), "-o", str(out), "--no-component-diagram", "--render", "browserjs"]
            if tests:
                args += ["--tests", str(tests)]
            if hooks:
                args += ["--include-hooks"]
            if base:
                args += ["--attachments-base", str(base)]
            done = run(args)
            service = out / "service.json"
            degraded = json.loads(service.read_text(encoding="utf-8"))["run"].get("degraded") if service.exists() else f"NO REPORT {done.returncode}: {done.stderr[:200]}"
            results[label] = (degraded, read(tool, out) if service.exists() else [])
        first = next(iter(results.values()))
        all_same = all(r == first for r in results.values())
        same += all_same
        differ += not all_same
        print(f"=== {name} {'--include-hooks' if hooks else ''}: {'the same on every build' if all_same else 'DIFFERS'}, flags {first[0] or '{}'}, {len(first[1])} scenarios, "
              f"{sum(line.count('=') for line in first[1])} calls, {sum(line.count('=-') for line in first[1])} on no step")
        if not all_same:
            for label, (degraded, lines) in results.items():
                print(f"  {label}: flags {degraded or '{}'}")
                for line in lines:
                    print(f"    {line}")
print(f"{same} the same, {differ} differ")
