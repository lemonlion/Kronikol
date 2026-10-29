# A floor search whose suite needs partners of the same release (plans/CLIENT_FLOORS_PLAN.md R2c): otel_floor.py made
# general. For each release X of --series (the releases every package named there shares) this pins the project's
# --pin references to X, builds the test project with -p:FloorRun=true -p:<--property>=X (the test project takes its
# partners at X under the switch), runs the suite, and binary-searches for the oldest that passes; with releases given,
# it probes each in turn instead.
#   python -I paired_floor.py --worktree W --project src/X/X.csproj --tests tests/T --pin Id[,Id] --series Id[,Id]
#       --property Name --major N --out out.txt --logs dir [--env NAME=value]... [release...]
import argparse, json, os, re, shutil, subprocess, urllib.request

ap = argparse.ArgumentParser()
for a in ("--worktree", "--project", "--tests", "--pin", "--series", "--property", "--major", "--out", "--logs"):
    ap.add_argument(a, required=True)
ap.add_argument("--env", action="append", default=[])
ap.add_argument("--filter", help="a dotnet test --filter: a package fact holds the nuspec to the ledger,"
                " so it fails every probe of another release (F23) and a search leaves it out")
ap.add_argument("releases", nargs="*")
o = ap.parse_args()
PROJECT = os.path.join(o.worktree, o.project)
TESTS = os.path.join(o.worktree, o.tests)
PINS = o.pin.split(",")
SERIES = o.series.split(",")
ENV = dict(os.environ, **dict(e.split("=", 1) for e in o.env))
os.makedirs(o.logs, exist_ok=True)

def get(url):
    with urllib.request.urlopen(url, timeout=60) as r:
        return r.read().decode()

def key(v):
    return [int(x) for x in v.split(".")]

def stable(package):
    return {v for v in json.loads(get(f"https://api.nuget.org/v3-flatcontainer/{package.lower()}/index.json"))["versions"]
            if "-" not in v and v.split(".")[0] == o.major}

original = open(PROJECT, encoding="utf-8", newline="").read()

def run(cmd, log):
    with open(log, "w", encoding="utf-8") as f:
        code = subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT, env=ENV).returncode
    return code, open(log, encoding="utf-8", errors="replace").read()

def probe(x):
    text = original
    for package in PINS:
        text = re.sub(r'(Include="' + re.escape(package) + r'" Version=")[^"]*', r"\g<1>" + x, text)
    open(PROJECT, "w", encoding="utf-8", newline="").write(text)
    log = os.path.join(o.logs, f"build-{x}.log")
    # From an empty bin/ and obj/: MSTest.TestAdapter reaches the output as content copied only when newer, so a probe
    # built in place after a newer one kept that one's adapter, which refused the older framework and found no test
    # (plans/CLIENT_FLOORS_PLAN.md F31).
    for output in ("bin", "obj"):
        shutil.rmtree(os.path.join(TESTS, output), ignore_errors=True)
    code, out = run(["dotnet", "build", TESTS, "-nologo", "-nodeReuse:false", "-p:FloorRun=true", f"-p:{o.property}={x}",
                     "-p:NuGetAuditMode=all"], log)
    # The project is in a [suffix] on some lines and leads others ("path.csproj : warning NU1903: ..."), which the first
    # form of this pattern missed, so it reported no advisory where the build had three (plans/CLIENT_FLOORS_PLAN.md F28).
    audit = sorted(set(f"{m['pkg']} {m['ver']} {m['sev']} ({os.path.splitext(os.path.basename((m['proj'] or m['pre'].split(' : ')[0]).split('::')[0].strip()))[0]})"
                       for m in re.finditer(r"^(?P<pre>[^\r\n]*?)warning NU190[1-4]: Package '(?P<pkg>[^']+)' (?P<ver>[0-9][^ ]*) has a known (?P<sev>[a-z]+) severity[^\[\r\n]*(?:\[(?P<proj>[^\]]+)\])?", out, re.M)))
    try:
        target = next(iter(json.load(open(os.path.join(TESTS, "obj", "project.assets.json"), encoding="utf-8"))["targets"].values()))
        resolved = " ".join(k for k in target if k.split("/")[0] in set(PINS) | set(SERIES))
        # A series package on another release means the probe did not run on this one: the LightBDD.xUnit2 example
        # asks for LightBDD.Framework 3.10.0 or later through LightBDD.TabularAttributes, so 3.5.0 ran on 3.10.0.
        elsewhere = [k for k in target if k.split("/")[0] in SERIES and k.split("/")[1] != x]
    except OSError:
        resolved, elsewhere = "?", []
    if code != 0:
        errs = " ".join(sorted(set(re.findall(r"error (?:CS|NU|MSB)[0-9]+: [^\[\r\n]{0,150}", out)))[:4])
        line = f"{x} | BUILD FAILED | {errs} | audit: {'; '.join(audit) or 'none'} | resolved: {resolved}"
    elif elsewhere:
        line = f"{x} | NOT ON {x} | resolved {' '.join(elsewhere)} | audit: {'; '.join(audit) or 'none'} | resolved: {resolved}"
    else:
        # VSTest exits 0 when it finds no test at all, so a run that found none counted as a pass, as MSTest 4.1.0 and
        # older did on another probe's adapter (plans/CLIENT_FLOORS_PLAN.md F29, F31): it has to fail.
        test = ["dotnet", "test", TESTS, "--no-build", "-nologo"] + (["--filter", o.filter] if o.filter else [])
        code, out = run(test + ["--", "RunConfiguration.TreatNoTestsAsError=true"], log.replace("build-", "test-"))
        summary = " ".join(re.findall(r"(?:Passed|Failed)!\s+-\s+Failed:\s+\d+,\s+Passed:\s+\d+,\s+Skipped:\s+\d+,\s+Total:\s+\d+", out)) or "no summary"
        line = f"{x} | {'ok' if code == 0 and summary != 'no summary' else 'TESTS FAILED'} | {re.sub(r'\s+', ' ', summary)} | audit: {'; '.join(audit) or 'none'} | resolved: {resolved}"
    with open(o.out, "a", encoding="utf-8") as f:
        f.write(line + chr(10))
    print(line, flush=True)
    return "| ok |" in line

try:
    if o.releases:
        with open(o.out, "a", encoding="utf-8") as f:
            f.write(f"# {', '.join(PINS)} with {', '.join(SERIES)} of the same release: each release given" + chr(10))
        for x in o.releases:
            probe(x)
    else:
        versions = sorted(set.intersection(*(stable(p) for p in SERIES)), key=key)
        with open(o.out, "a", encoding="utf-8") as f:
            f.write(f"# {', '.join(PINS)} with {', '.join(SERIES)} of the same release: {len(versions)} {o.major}.x releases all"
                    f" share, {versions[0]} to {versions[-1]}; binary search" + chr(10))
        lo, hi = 0, len(versions) - 1
        if not probe(versions[hi]):
            raise SystemExit("the newest release fails")
        while lo < hi:
            mid = (lo + hi) // 2
            if probe(versions[mid]):
                hi = mid
            else:
                lo = mid + 1
        with open(o.out, "a", encoding="utf-8") as f:
            f.write(f"# oldest passing: {versions[lo]}" + chr(10))
        print("oldest passing:", versions[lo])
finally:
    open(PROJECT, "w", encoding="utf-8", newline="").write(original)
    subprocess.run(["dotnet", "restore", TESTS, "-nologo"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=ENV)
