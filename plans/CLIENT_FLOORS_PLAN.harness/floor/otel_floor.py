# OpenTelemetry's floor (plans/CLIENT_FLOORS_PLAN.md R2c). The package references OpenTelemetry and
# OpenTelemetry.Exporter.InMemory, released together, and its suite builds a tracer through
# OpenTelemetry.Extensions.Hosting (AddOpenTelemetry), which asks for the OpenTelemetry of its own release, so a floor
# run is only on the floor when the hosting extensions are of the same release. For each 1.x release X that all three
# share this pins the package's two references to X, builds the suite with -p:FloorRun=true -p:OpenTelemetryFloor=X
# (the test project's hosting extensions under the switch), runs it, and binary-searches for the oldest that passes.
#   python -I otel_floor.py <worktree> <out> <logs> [<release>...]   (releases given: probe each, no search)
import json, os, re, subprocess, sys, urllib.request

W, OUT, LOGS = sys.argv[1:4]
PROJECT = os.path.join(W, "src/Kronikol.Extensions.OpenTelemetry/Kronikol.Extensions.OpenTelemetry.csproj")
TESTS = os.path.join(W, "tests/Kronikol.Tests.OpenTelemetry")
PACKAGES = ("OpenTelemetry", "OpenTelemetry.Exporter.InMemory", "OpenTelemetry.Extensions.Hosting")
os.makedirs(LOGS, exist_ok=True)

def get(url):
    with urllib.request.urlopen(url, timeout=60) as r:
        return r.read().decode()

def key(v):
    return [int(x) for x in v.split(".")]

def stable(package):
    return {v for v in json.loads(get(f"https://api.nuget.org/v3-flatcontainer/{package.lower()}/index.json"))["versions"]
            if "-" not in v and v.startswith("1.")}

versions = sorted(set.intersection(*(stable(p) for p in PACKAGES)), key=key)
original = open(PROJECT, encoding="utf-8", newline="").read()

def run(cmd, log):
    with open(log, "w", encoding="utf-8") as f:
        return subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT).returncode, open(log, encoding="utf-8", errors="replace").read()

def probe(x):
    text = original
    for package in PACKAGES[:2]:
        text = re.sub(r'(Include="' + re.escape(package) + r'" Version=")[^"]*', r"\g<1>" + x, text)
    open(PROJECT, "w", encoding="utf-8", newline="").write(text)
    log = os.path.join(LOGS, f"build-otel-{x}.log")
    code, out = run(["dotnet", "build", TESTS, "-nologo", "-nodeReuse:false", "-p:FloorRun=true", f"-p:OpenTelemetryFloor={x}",
                     "-p:NuGetAuditMode=all"], log)
    # As paired_floor.py: the project leads some lines rather than closing them (plans/CLIENT_FLOORS_PLAN.md F28).
    audit = sorted(set(f"{m['pkg']} {m['ver']} {m['sev']} ({os.path.splitext(os.path.basename((m['proj'] or m['pre'].split(' : ')[0]).split('::')[0].strip()))[0]})"
                       for m in re.finditer(r"^(?P<pre>[^\r\n]*?)warning NU190[1-4]: Package '(?P<pkg>[^']+)' (?P<ver>[0-9][^ ]*) has a known (?P<sev>[a-z]+) severity[^\[\r\n]*(?:\[(?P<proj>[^\]]+)\])?", out, re.M)))
    try:
        target = next(iter(json.load(open(os.path.join(TESTS, "obj", "project.assets.json"), encoding="utf-8"))["targets"].values()))
        resolved = " ".join(k for k in target if k.split("/")[0] in PACKAGES)
    except OSError:
        resolved = "?"
    if code != 0:
        errs = " ".join(sorted(set(re.findall(r"error (?:CS|NU|MSB)[0-9]+: [^\[\r\n]{0,150}", out)))[:4])
        line = f"{x} | BUILD FAILED | {errs} | audit: {'; '.join(audit) or 'none'} | resolved: {resolved}"
    else:
        code, out = run(["dotnet", "test", TESTS, "--no-build", "-nologo"], log.replace("build-", "test-"))
        summary = " ".join(re.findall(r"(?:Passed|Failed)!\s+-\s+Failed:\s+\d+,\s+Passed:\s+\d+,\s+Skipped:\s+\d+,\s+Total:\s+\d+", out)) or "no summary"
        line = f"{x} | {'ok' if code == 0 else 'TESTS FAILED'} | {re.sub(r'\s+', ' ', summary)} | audit: {'; '.join(audit) or 'none'} | resolved: {resolved}"
    with open(OUT, "a", encoding="utf-8") as f:
        f.write(line + "\n")
    print(line, flush=True)
    return "| ok |" in line

if len(sys.argv) > 4:
    # Each release given, in turn, with no search: the releases between the oldest passing one and an advisory's end.
    try:
        with open(OUT, "a", encoding="utf-8") as f:
            f.write("# advisory floor: each release from the oldest passing to the first without an advisory" + chr(10))
        for x in sys.argv[4:]:
            probe(x)
    finally:
        open(PROJECT, "w", encoding="utf-8", newline="").write(original)
        subprocess.run(["dotnet", "restore", TESTS, "-nologo"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    raise SystemExit(0)

try:
    with open(OUT, "a", encoding="utf-8") as f:
        f.write(f"# OpenTelemetry and OpenTelemetry.Exporter.InMemory with OpenTelemetry.Extensions.Hosting of the same release:"
                f" {len(versions)} 1.x releases all three share, {versions[0]} to {versions[-1]}; binary search\n")
    lo, hi = 0, len(versions) - 1
    if not probe(versions[hi]):
        raise SystemExit("the newest release fails")
    while lo < hi:
        mid = (lo + hi) // 2
        if probe(versions[mid]):
            hi = mid
        else:
            lo = mid + 1
    with open(OUT, "a", encoding="utf-8") as f:
        f.write(f"# oldest passing: {versions[lo]}\n")
    print("oldest passing:", versions[lo])
finally:
    open(PROJECT, "w", encoding="utf-8", newline="").write(original)
    subprocess.run(["dotnet", "restore", TESTS, "-nologo"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
