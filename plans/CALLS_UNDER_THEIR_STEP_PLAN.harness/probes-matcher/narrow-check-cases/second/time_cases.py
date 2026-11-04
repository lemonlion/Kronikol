"""Times `kronikol ingest` on each case of a folder, per build. Usage: time_cases.py <cases> <work> <label=command>..."""
import os, pathlib, shlex, subprocess, sys, time

os.environ["KRONIKOL_HISTORY"] = "off"
os.environ["KRONIKOL_KEEP_RUNS"] = "off"
cases, work, builds = pathlib.Path(sys.argv[1]).resolve(), pathlib.Path(sys.argv[2]).resolve(), [a.split("=", 1) for a in sys.argv[3:]]
for case in sorted(p for p in cases.iterdir() if p.is_dir()):
    line = [case.name.ljust(30)]
    for label, command in builds:
        tool = shlex.split(command, posix=False)
        out = work / label / case.name
        out.mkdir(parents=True, exist_ok=True)
        began = time.time()
        done = subprocess.run([*tool, "ingest", "captures", "--tests", "tests.ndjson", "-o", str(out), "--no-component-diagram", "--render", "browserjs"], cwd=case, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=500)
        line.append(f"{label} {time.time() - began:6.1f}s (exit {done.returncode})")
    print("  ".join(line), flush=True)
