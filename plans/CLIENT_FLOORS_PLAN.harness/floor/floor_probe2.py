# The floor probe for R2 and R3 (plans/CLIENT_FLOORS_PLAN.md 5.2): floor_probe.sh generalised to take the package id,
# the packable project that references it and the test projects that run it.
#
#   python -I floor_probe2.py --worktree C:/Code/Kronikol-205 --project src/X/X.csproj --package Id \
#       --tests tests/Kronikol.Tests.X [--tests ...] [--also Id2=version ...] --out results.txt --logs <dir>  v1 v2 ...
#   python -I floor_probe2.py ... --search [--from 2.0.0] [--to 2.5.0]   # binary search for the oldest passing release of the major
#
# For each version: pin the package to it in the project's csproj, build each test project with -p:FloorRun=true (so a
# test project's own newest reference drops out and it runs on what the project asks for) and -p:NuGetAuditMode=all
# (so restore reports an advisory anywhere in the graph, not only on a direct reference), note what NuGet resolved and
# every NU1901-NU1904, run the suite, and append one line. The csproj is put back afterwards, whatever happens.
# --also pins other references of the same project for the run (a family that must move together, as Spanner's).
# Lines: "<version> | ok|BUILD FAILED|TESTS FAILED | <test summary or errors> | audit: ... | resolved: ..."
import argparse, json, os, re, shutil, subprocess, sys, urllib.request

ap = argparse.ArgumentParser()
ap.add_argument("--worktree", required=True)
ap.add_argument("--project", required=True)
ap.add_argument("--package", required=True)
ap.add_argument("--tests", action="append", required=True)
ap.add_argument("--also", action="append", default=[])
ap.add_argument("--out", required=True)
ap.add_argument("--logs", required=True, help="a folder for the build and test logs")
ap.add_argument("--search", action="store_true")
ap.add_argument("--from", dest="start")
ap.add_argument("--to", dest="end", help="the newest release to take; the search starts there, so a newer one the suite's"
                " own code cannot build against (TUnit.Core's, R3a) is left out")
ap.add_argument("--env", action="append", default=[], help="NAME=value for the test runs")
ap.add_argument("--filter", help="a dotnet test --filter for the suite")
ap.add_argument("--setup", help="a bash command run after the build and before the suite, with {test} and {version}"
                " filled in: Playwright's browsers for the release built (R3a)")
ap.add_argument("--no-floor-run", action="store_true",
                help="build the test projects without -p:FloorRun=true, so each keeps its own newest reference and the"
                " suite runs on the newest against the package built on the release probed: TUnit's source generator"
                " writes hook code into the adapter that binds to the TUnit.Core of its build (F35)")
ap.add_argument("versions", nargs="*")
a = ap.parse_args()

os.makedirs(a.logs, exist_ok=True)
os.chdir(a.worktree)
csproj = a.project
backup = open(csproj, encoding="utf-8", newline="").read()
env = dict(os.environ, **dict(e.split("=", 1) for e in a.env))

def key(v):
    return [int(x) if x.isdigit() else x for x in re.split(r"[.]", v)]

def pin(text, package, version):
    pattern = re.compile(r'(<PackageReference\s+Include="' + re.escape(package) + r'"\s+Version=")[^"]*(")')
    out, n = pattern.subn(lambda m: m.group(1) + version + m.group(2), text)
    if n != 1:
        raise SystemExit(f"{csproj}: {n} references to {package}, not one")
    return out

def resolved(test):
    try:
        d = json.load(open(os.path.join(test, "obj", "project.assets.json"), encoding="utf-8"))
    except OSError:
        return "?"
    names = {a.package.lower()} | {x.split("=")[0].lower() for x in a.also}
    target = next(iter(d["targets"].values()))
    return " ".join(k for k in target if k.split("/")[0].lower() in names)

def run(cmd, log):
    with open(log, "w", encoding="utf-8") as f:
        p = subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT, env=env)
    return p.returncode, open(log, encoding="utf-8", errors="replace").read()

def probe(version):
    text = pin(backup, a.package, version)
    for also in a.also:
        pkg, ver = also.split("=", 1)
        text = pin(text, pkg, ver)
    open(csproj, "w", encoding="utf-8", newline="").write(text)
    status, notes, audit, res = "ok", [], set(), []
    for test in a.tests:
        log = os.path.join(a.logs, f"build-{os.path.basename(test)}-{version}.log")
        # From an empty bin/ and obj/: MSTest.TestAdapter reaches the output as content copied only when newer, so a probe
        # built in place after a newer one kept that one's adapter, which refused the older framework and found no test
        # (plans/CLIENT_FLOORS_PLAN.md F31).
        for output in ("bin", "obj"):
            shutil.rmtree(os.path.join(a.worktree, test, output), ignore_errors=True)
        build = ["dotnet", "build", test, "-nologo", "-p:NuGetAuditMode=all"] + ([] if a.no_floor_run else ["-p:FloorRun=true"])
        code, out = run(build, log)
        # Each advisory with the project whose graph holds it: one in the packable project's graph is the floor's
        # concern, one only in a test project's (Testcontainers, say) is not.
        # The project closes some lines in brackets and leads others ("path.csproj : warning NU1903: ..."), which the first
        # form of this pattern missed (plans/CLIENT_FLOORS_PLAN.md F28).
        for m in re.finditer(r"^(?P<pre>[^\r\n]*?)warning NU190[1-4]: Package '(?P<pkg>[^']+)' (?P<ver>[0-9][^ ]*) has a known (?P<sev>[a-z]+) severity[^\[\r\n]*(?:\[(?P<proj>[^\]]+)\])?", out, re.M):
            where = os.path.splitext(os.path.basename((m["proj"] or m["pre"].split(" : ")[0]).split("::")[0].strip()))[0]
            audit.add(f"{m['pkg']} {m['ver']} {m['sev']} ({where})")
        res.append(resolved(test) + (f" (built on: {resolved(os.path.dirname(csproj))})" if a.no_floor_run else ""))
        if code != 0:
            errs = sorted(set(re.findall(r"error (?:CS|NU|MSB)[0-9]+: [^\[\r\n]{0,150}", out)))[:4]
            status = "BUILD FAILED"
            notes.append(f"{os.path.basename(test)}: " + " ".join(errs))
            continue
        tlog = log.replace("build-", "test-")
        if a.setup:
            # sh, not bash: CreateProcess looks in System32 before PATH, and System32's bash.exe is WSL's, which ran the
            # first Playwright probe's setup in the default WSL distro, where there is no pwsh (R3a); there is no sh.exe there.
            code, out = run(["sh", "-c", a.setup.format(test=test, version=version)], log.replace("build-", "setup-"))
            if code != 0:
                status = "SETUP FAILED"
                notes.append(f"{os.path.basename(test)}: " + " ".join(out.strip().splitlines()[-2:])[:200])
                continue
        cmd = ["dotnet", "test", test, "--no-build", "-nologo"]
        if a.filter:
            cmd += ["--filter", a.filter]
        # VSTest exits 0 when it finds no test at all, so a run that found none counted as a pass, as MSTest 4.1.0 and
        # older did on another probe's adapter (plans/CLIENT_FLOORS_PLAN.md F29, F31): it has to fail.
        cmd += ["--", "RunConfiguration.TreatNoTestsAsError=true"]
        code, out = run(cmd, tlog)
        summary = " ".join(re.findall(r"(?:Passed|Failed)!\s+-\s+Failed:\s+\d+,\s+Passed:\s+\d+,\s+Skipped:\s+\d+,\s+Total:\s+\d+", out)) or \
            " ".join(re.findall(r"(?:total|failed|succeeded|skipped): \d+", out)) or "no summary"
        if (code != 0 or summary == "no summary") and status == "ok":
            status = "TESTS FAILED"
        notes.append(f"{os.path.basename(test)}: {re.sub(r'\s+', ' ', summary)}")
    line = f"{version} | {status} | {'; '.join(notes)} | audit: {'; '.join(sorted(audit)) or 'none'} | resolved: {' / '.join(res)}"
    with open(a.out, "a", encoding="utf-8") as f:
        f.write(line + "\n")
    print(line, flush=True)
    return status == "ok"

try:
    if a.search:
        current = re.search(r'Include="' + re.escape(a.package) + r'"\s+Version="([^"]+)"', backup).group(1)
        major = current.split(".")[0]
        with urllib.request.urlopen(f"https://api.nuget.org/v3-flatcontainer/{a.package.lower()}/index.json", timeout=60) as r:
            versions = [v for v in json.load(r)["versions"] if "-" not in v and v.split(".")[0] == major]
        versions.sort(key=key)
        if a.start:
            versions = [v for v in versions if key(v) >= key(a.start)]
        if a.end:
            versions = [v for v in versions if key(v) <= key(a.end)]
        with open(a.out, "a", encoding="utf-8") as f:
            f.write(f"# {a.package}: {len(versions)} stable {major}.x releases, {versions[0]} to {versions[-1]}; binary search\n")
        lo, hi = 0, len(versions) - 1
        if not probe(versions[hi]):
            raise SystemExit("the newest release fails: no floor in this major")
        while lo < hi:
            mid = (lo + hi) // 2
            if probe(versions[mid]):
                hi = mid
            else:
                lo = mid + 1
        with open(a.out, "a", encoding="utf-8") as f:
            f.write(f"# oldest passing: {versions[lo]}\n")
        print("oldest passing:", versions[lo])
    for v in a.versions:
        probe(v)
finally:
    open(csproj, "w", encoding="utf-8", newline="").write(backup)
    for test in a.tests:
        subprocess.run(["dotnet", "restore", test, "-nologo"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
