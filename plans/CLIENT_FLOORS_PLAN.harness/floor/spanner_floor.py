# Spanner's three references move together (plans/CLIENT_FLOORS_PLAN.md R2a): Google.Cloud.Spanner.Data X brings
# Google.Cloud.Spanner.V1 X, and through GAX a Grpc.Core.Api it needs at least. For each Spanner release this finds that
# Grpc.Core.Api from the NU1605 a lower pin draws, then runs floor_probe2.py with the family pinned together, binary
# searching the 5.x releases for the oldest that passes.
#   python -I spanner_floor.py <worktree> <out> <logs>
import json, os, re, subprocess, sys, urllib.request

W, OUT, LOGS = sys.argv[1:4]
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = "src/Kronikol.Extensions.Spanner/Kronikol.Extensions.Spanner.csproj"
os.makedirs(LOGS, exist_ok=True)

def key(v):
    return [int(x) for x in v.split(".")]

with urllib.request.urlopen("https://api.nuget.org/v3-flatcontainer/google.cloud.spanner.data/index.json", timeout=60) as r:
    versions = sorted((v for v in json.load(r)["versions"] if "-" not in v and v.startswith("5.")), key=key)

def grpc_core_api_floor(x):
    """The Grpc.Core.Api Spanner.Data x needs at least: pin it to the first 2.x and read the downgrade NuGet reports."""
    path = os.path.join(W, PROJECT)
    original = open(path, encoding="utf-8", newline="").read()
    text = re.sub(r'(Include="Google.Cloud.Spanner.Data" Version=")[^"]*', r"\g<1>" + x, original)
    text = re.sub(r'(Include="Google.Cloud.Spanner.V1" Version=")[^"]*', r"\g<1>" + x, text)
    text = re.sub(r'(Include="Grpc.Core.Api" Version=")[^"]*', r"\g<1>2.23.0", text)
    try:
        open(path, "w", encoding="utf-8", newline="").write(text)
        p = subprocess.run(["dotnet", "restore", path, "-nologo"], capture_output=True, text=True)
        m = re.search(r"Detected package downgrade: Grpc.Core.Api from ([0-9.]+) to 2.23.0", p.stdout + p.stderr)
        return m.group(1) if m else "2.23.0"
    finally:
        open(path, "w", encoding="utf-8", newline="").write(original)

def probe(x):
    api = grpc_core_api_floor(x)
    cmd = [sys.executable, "-I", "-u", os.path.join(HERE, "floor_probe2.py"), "--worktree", W, "--project", PROJECT,
           "--package", "Google.Cloud.Spanner.Data", "--also", f"Google.Cloud.Spanner.V1={x}", "--also", f"Grpc.Core.Api={api}",
           "--tests", "tests/Kronikol.Tests.Spanner", "--out", OUT, "--logs", LOGS, x]
    out = subprocess.run(cmd, capture_output=True, text=True).stdout
    print(out.strip(), flush=True)
    return f"{x} | ok |" in out

with open(OUT, "a", encoding="utf-8") as f:
    f.write(f"# Google.Cloud.Spanner.Data with Spanner.V1 at the same version and Grpc.Core.Api at the least it needs:"
            f" {len(versions)} 5.x releases, {versions[0]} to {versions[-1]}; binary search\n")
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
    f.write(f"# oldest passing: {versions[lo]} (Grpc.Core.Api {grpc_core_api_floor(versions[lo])})\n")
print("oldest passing:", versions[lo])
