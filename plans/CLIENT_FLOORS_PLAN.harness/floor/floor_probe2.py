# The floor probe for R2 and R3 (plans/CLIENT_FLOORS_PLAN.md 5.2): floor_probe.sh generalised to take the package id,
# the packable project that references it and the test projects that run it.
#
#   python -I floor_probe2.py --worktree C:/Code/Kronikol-205 --project src/X/X.csproj --package Id \
#       --tests tests/Kronikol.Tests.X [--tests ...] [--also Id2=version ...] --out results.txt --logs <dir>  v1 v2 ...
#   python -I floor_probe2.py ... --search [--from 2.0.0]   # binary search for the oldest passing release of the major
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
ap.add_argument("--env", action="append", default=[], help="NAME=value for the test runs")
ap.add_argument("--filter", help="a dotnet test --filter for the suite")
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
        code, out = run(["dotnet", "build", test, "-nologo", "-p:FloorRun=true", "-p:NuGetAuditMode=all"], log)
        # Each advisory with the project whose graph holds it: one in the packable project's graph is the floor's
        # concern, one only in a test project's (Testcontainers, say) is not.
        for m in re.finditer(r"warning (NU190[1-4]): Package '([^']+)' ([0-9][^ ]*) has a known ([a-z]+) severity[^\[\r\n]*\[([^\]]+)\]", out):
            where = os.path.splitext(os.path.basename(m.group(5).split("::")[0]))[0]
            audit.add(f"{m.group(2)} {m.group(3)} {m.group(4)} ({where})")
        res.append(resolved(test))
        if code != 0:
            errs = sorted(set(re.findall(r"error (?:CS|NU|MSB)[0-9]+: [^\[\r\n]{0,150}", out)))[:4]
            status = "BUILD FAILED"
            notes.append(f"{os.path.basename(test)}: " + " ".join(errs))
            continue
        tlog = log.replace("build-", "test-")
        cmd = ["dotnet", "test", test, "--no-build", "-nologo"]
        if a.filter:
            cmd += ["--filter", a.filter]
        code, out = run(cmd, tlog)
        summary = " ".join(re.findall(r"(?:Passed|Failed)!\s+-\s+Failed:\s+\d+,\s+Passed:\s+\d+,\s+Skipped:\s+\d+,\s+Total:\s+\d+", out)) or \
            " ".join(re.findall(r"(?:total|failed|succeeded|skipped): \d+", out)) or "no summary"
        if code != 0 and status == "ok":
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
