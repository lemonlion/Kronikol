# gRPC's floors (plans/CLIENT_FLOORS_PLAN.md R2a). The suite calls a real server, Grpc.AspNetCore, which brings
# Grpc.Net.Client and a Google.Protobuf of its own, so a floor run is only on the floor when the server is of the same
# release. For each Grpc.Net.Client release X this pins the extension to X and to the Google.Protobuf that
# Grpc.AspNetCore X asks for, builds the suite with -p:FloorRun=true -p:GrpcServerFloor=X (the test project's server
# under the switch), runs it, and binary-searches the 2.x releases for the oldest that passes.
#   python -I grpc_floor.py <worktree> <out> <logs>
import json, os, re, subprocess, sys, urllib.request

W, OUT, LOGS = sys.argv[1:4]
PROJECT = os.path.join(W, "src/Kronikol.Extensions.Grpc/Kronikol.Extensions.Grpc.csproj")
TESTS = os.path.join(W, "tests/Kronikol.Tests.Grpc")
os.makedirs(LOGS, exist_ok=True)

def get(url):
    with urllib.request.urlopen(url, timeout=60) as r:
        return r.read().decode()

def key(v):
    return [int(x) for x in v.split(".")]

versions = sorted((v for v in json.loads(get("https://api.nuget.org/v3-flatcontainer/grpc.net.client/index.json"))["versions"]
                   if "-" not in v and v.startswith("2.")), key=key)
servers = set(json.loads(get("https://api.nuget.org/v3-flatcontainer/grpc.aspnetcore/index.json"))["versions"])
versions = [v for v in versions if v in servers]

def protobuf_for(x):
    nuspec = get(f"https://api.nuget.org/v3-flatcontainer/grpc.aspnetcore/{x}/grpc.aspnetcore.nuspec")
    return re.search(r'<dependency id="Google.Protobuf" version="\[?([0-9.]+)', nuspec).group(1)

original = open(PROJECT, encoding="utf-8", newline="").read()

def run(cmd, log):
    with open(log, "w", encoding="utf-8") as f:
        return subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT).returncode, open(log, encoding="utf-8", errors="replace").read()

def probe(x):
    p = protobuf_for(x)
    text = re.sub(r'(Include="Grpc.Net.Client" Version=")[^"]*', r"\g<1>" + x, original)
    text = re.sub(r'(Include="Google.Protobuf" Version=")[^"]*', r"\g<1>" + p, text)
    open(PROJECT, "w", encoding="utf-8", newline="").write(text)
    log = os.path.join(LOGS, f"build-grpc-{x}.log")
    code, out = run(["dotnet", "build", TESTS, "-nologo", "-p:FloorRun=true", f"-p:GrpcServerFloor={x}", "-p:NuGetAuditMode=all"], log)
    audit = sorted(set(f"{m.group(1)} {m.group(2)} {m.group(3)} ({os.path.splitext(os.path.basename(m.group(4).split('::')[0]))[0]})"
                       for m in re.finditer(r"warning NU190[1-4]: Package '([^']+)' ([0-9][^ ]*) has a known ([a-z]+) severity[^\[\r\n]*\[([^\]]+)\]", out)))
    try:
        target = next(iter(json.load(open(os.path.join(TESTS, "obj", "project.assets.json"), encoding="utf-8"))["targets"].values()))
        resolved = " ".join(k for k in target if k.split("/")[0] in ("Grpc.Net.Client", "Google.Protobuf", "Grpc.AspNetCore"))
    except OSError:
        resolved = "?"
    if code != 0:
        errs = " ".join(sorted(set(re.findall(r"error (?:CS|NU|MSB)[0-9]+: [^\[\r\n]{0,150}", out)))[:4])
        line = f"{x} | BUILD FAILED | Google.Protobuf {p}: {errs} | audit: {'; '.join(audit) or 'none'} | resolved: {resolved}"
    else:
        code, out = run(["dotnet", "test", TESTS, "--no-build", "-nologo"], log.replace("build-", "test-"))
        summary = " ".join(re.findall(r"(?:Passed|Failed)!\s+-\s+Failed:\s+\d+,\s+Passed:\s+\d+,\s+Skipped:\s+\d+,\s+Total:\s+\d+", out)) or "no summary"
        line = f"{x} | {'ok' if code == 0 else 'TESTS FAILED'} | Google.Protobuf {p}: {re.sub(r'\s+', ' ', summary)} | audit: {'; '.join(audit) or 'none'} | resolved: {resolved}"
    with open(OUT, "a", encoding="utf-8") as f:
        f.write(line + "\n")
    print(line, flush=True)
    return "| ok |" in line

try:
    with open(OUT, "a", encoding="utf-8") as f:
        f.write(f"# Grpc.Net.Client with Google.Protobuf at what Grpc.AspNetCore of the same release asks for, the suite's server"
                f" of that release: {len(versions)} 2.x releases, {versions[0]} to {versions[-1]}; binary search\n")
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
        f.write(f"# oldest passing: {versions[lo]} (Google.Protobuf {protobuf_for(versions[lo])})\n")
    print("oldest passing:", versions[lo])
finally:
    open(PROJECT, "w", encoding="utf-8", newline="").write(original)
    subprocess.run(["dotnet", "restore", TESTS, "-nologo"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
