"""Every test project of a worktree, one at a time, in Release: what `Passed!`/`Failed!` said, and the names of what failed.

Usage: full_suite.py <worktree> <out file> [--only-examples | --only-e2e | --skip=<substring>...]
Leaves out the Playwright project and the example projects unless asked: each of those is announced to other sessions first.
"""
import pathlib
import re
import subprocess
import sys
import time

WT = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])
flags = sys.argv[3:]
skips = [f.split("=", 1)[1] for f in flags if f.startswith("--skip=")]


def tracked():
    out = subprocess.run(["git", "ls-files", "*.csproj"], cwd=WT, capture_output=True, text=True).stdout.splitlines()
    return [p for p in out if re.search(r"(^|/)tests/", p) and "Test" in p.rsplit("/", 1)[-1]]


projects = tracked()
if "--only-examples" in flags:
    projects = [p for p in projects if p.startswith("examples/")]
elif "--only-e2e" in flags:
    projects = [p for p in projects if "EndToEnd" in p]
else:
    projects = [p for p in projects if not p.startswith("examples/") and "EndToEnd" not in p]
projects = [p for p in projects if not any(s in p for s in skips)]

failures = 0
with OUT.open("a", encoding="utf-8", newline="\n") as out:
    out.write(f"# {len(projects)} projects in {WT}\n")
    out.flush()
    for project in projects:
        started = time.time()
        try:
            run = subprocess.run(["dotnet", "test", project, "-c", "Release", "-p:WarningLevel=0", "-nologo", "-v", "q"], cwd=WT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=3600)
            text = run.stdout + run.stderr
        except subprocess.TimeoutExpired as e:
            text, run = f"TIMED OUT {e}", None
        lines = re.findall(r"^(?:Passed!|Failed!).*$", text, flags=re.M)
        failed = re.findall(r"^\s+Failed (\S+)", text, flags=re.M)
        errors = sorted({l.strip()[:200] for l in text.splitlines() if " error " in l})[:3]
        failures += len(failed) + (1 if run is None or (run.returncode != 0 and not failed) else 0)
        out.write(f"{project} ({int(time.time() - started)} s) exit {run.returncode if run else 'timeout'}\n")
        for line in lines:
            out.write(f"  {re.sub(r', Duration:.*', '', line)[:110]}\n")
        for name in failed[:40]:
            out.write(f"  FAILED {name[:170]}\n")
        for error in errors:
            out.write(f"  ERROR {error}\n")
        out.flush()
    out.write(f"# done, {failures} failures\n")
print("done", failures)
