"""Does WarmUpCorpusTests see Q5 (b)? Over its two lanes it cannot, as warmup-spec-cost.py counts. Here it gets a third lane holding test-made publishes
(the file warmup-spec-cost.py writes), three ways: no option and the marks of warmup.py as it stands; the option and the same marks; the option and
the marks of warmup.py with MessageTable's rows ported. In a worktree holding R1 and R2. Everything it touches is put back, and it builds there, so
nothing else may build in that worktree while it runs.

usage: python probes/warmup-parity-lane.py <worktree> <folder warmup-spec-cost.py wrote the lane to> <folder for the runs' output>
"""
import json
import os
import re
import shutil
import subprocess
import sys

worktree, lane_folder, out = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(out, exist_ok=True)
patch = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "prototype", "r2-option-warm-up-judges-publishes.patch")
project = "tests/Kronikol.Tests/Kronikol.Tests.csproj"
stored = "tests/Kronikol.Tests/TestData/WarmUp/expected-marks.json"
lane_file = "tests/Kronikol.Tests/TestData/WarmUp/publish.calls.json.gz"
fact = "tests/Kronikol.Tests/Reports/WarmUpCorpusTests.cs"
option_files = ["src/Kronikol/Reports/WarmUpCalls.cs", "tests/Kronikol.Tests/Reports/WarmUpCallsTests.cs", "plans/WARM_UP_PLAN.harness/warmup.py"]
saved = {path: open(os.path.join(worktree, path), "rb").read() for path in [stored, fact, *option_files]}


def restore():
    for path, content in saved.items():
        with open(os.path.join(worktree, path), "wb") as fh:
            fh.write(content)
    for path in (os.path.join(worktree, lane_file), os.path.join(worktree, "tests/Kronikol.Tests/bin/Debug/net10.0/TestData/WarmUp/publish.calls.json.gz")):
        if os.path.exists(path):
            os.remove(path)


def add_lane(which):
    shutil.copyfile(os.path.join(lane_folder, "publish.calls.json.gz"), os.path.join(worktree, lane_file))
    marks = json.loads(saved[stored].decode("utf-8"))
    marks["publish"] = json.load(open(os.path.join(lane_folder, f"publish.expected-{which}.json"), encoding="utf-8"))["publish"]
    with open(os.path.join(worktree, stored), "w", encoding="utf-8") as fh:
        json.dump(marks, fh, indent=1, sort_keys=True)
    text = saved[fact].decode("utf-8")
    row = '    [InlineData("reqnroll")]'
    assert text.count(row) == 1
    newline = "\r\n" if "\r\n" in text else "\n"
    with open(os.path.join(worktree, fact), "wb") as fh:
        fh.write(text.replace(row, row + newline + '    [InlineData("publish")]').encode("utf-8"))


def run(label):
    build = subprocess.run(["dotnet", "build", project, "-c", "Debug", "-nologo", "-v:q", "-clp:ErrorsOnly"], cwd=worktree, capture_output=True, text=True, errors="replace")
    if build.returncode != 0:
        raise SystemExit(f"{label}: the build failed\n{build.stdout[-2000:]}")
    test = subprocess.run(["dotnet", "test", project, "-c", "Debug", "--no-build", "-nologo", "--filter", "FullyQualifiedName~WarmUpCorpusTests"], cwd=worktree, capture_output=True, text=True, errors="replace")
    with open(os.path.join(out, f"{label}.txt"), "w", encoding="utf-8") as fh:
        fh.write(test.stdout)
    summary = [re.sub(r"\s+-\s+Kronikol\.Tests.*$", "", line.strip()) for line in test.stdout.splitlines() if re.match(r"\s*(Passed!|Failed!)", line)]
    print(f"{label}: {summary[-1] if summary else 'no summary line'}")
    for failed in re.findall(r"WarmUpCorpusTests\.(\S+\(.*?\)) \[FAIL\]", test.stdout):
        print(f"    failed: {failed}")


def apply_option():
    subprocess.run(["git", "apply", os.path.abspath(patch)], cwd=worktree, check=True)


try:
    restore()
    add_lane("unported")
    run("no option, the script's marks as it stands")
    restore()
    apply_option()
    add_lane("unported")
    run("the option, the script's marks as it stands")
    restore()
    apply_option()
    add_lane("ported")
    run("the option, the script's marks with the rows ported")
finally:
    restore()
