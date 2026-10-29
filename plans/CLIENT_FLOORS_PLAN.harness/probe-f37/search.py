# F37: the oldest Microsoft.Playwright 1.x whose driver starts on Linux, binary-searched with probe/ in the .NET 10 SDK
# container of the podman VM (as root; the container's name and folders are this probe's own). Each release runs from
# an empty bin/ and obj/, with a NuGet cache of the probe's own. Prints a line per release probed, then the answer.
#   python -I search.py [versions to probe instead of the search]
import json
import os
import subprocess
import sys
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
VM = ["wsl", "-d", "podman-machine-default", "-u", "root", "--"]
ROOT = "/root/f37-pw"


def wsl_path(path):
    drive, rest = os.path.splitdrive(path)
    return "/mnt/" + drive[0].lower() + rest.replace("\\", "/")


def vm(*cmd, timeout=1800):
    return subprocess.run(VM + list(cmd), capture_output=True, text=True, timeout=timeout)


def probe(version):
    vm("podman", "rm", "-f", "f37-pwprobe")
    run = vm("podman", "run", "--rm", "--name", "f37-pwprobe", "-v", f"{ROOT}/probe:/probe",
             "-v", f"{ROOT}/nuget:/root/.nuget/packages", "-w", "/probe", "mcr.microsoft.com/dotnet/sdk:10.0",
             "sh", "-c", f"rm -rf bin obj && dotnet run -p:PwVersion={version} 2>&1 | tail -5")
    lines = [line for line in (run.stdout + run.stderr).splitlines() if line.strip()]
    verdict = next((line for line in lines if line.startswith(("driver started", "driver failed"))), None)
    return verdict is not None and verdict.startswith("driver started"), verdict or " | ".join(lines[-3:])


vm("sh", "-c", f"mkdir -p {ROOT}/probe {ROOT}/nuget && rm -rf {ROOT}/probe/* && cp {wsl_path(HERE)}/probe/pwprobe.csproj "
               f"{wsl_path(HERE)}/probe/Program.cs {ROOT}/probe/")
if sys.argv[1:]:
    for version in sys.argv[1:]:
        ok, what = probe(version)
        print(f"{version} | {'ok' if ok else 'FAIL'} | {what}", flush=True)
    raise SystemExit(0)

with urllib.request.urlopen("https://api.nuget.org/v3-flatcontainer/microsoft.playwright/index.json", timeout=60) as r:
    versions = [v for v in json.load(r)["versions"] if "-" not in v and v.startswith("1.")]
print(f"# Microsoft.Playwright: {len(versions)} stable 1.x releases, {versions[0]} to {versions[-1]}; binary search on Linux"
      f" (mcr.microsoft.com/dotnet/sdk:10.0)", flush=True)
lo, hi = 0, len(versions) - 1
ok, what = probe(versions[hi])
print(f"{versions[hi]} | {'ok' if ok else 'FAIL'} | {what}", flush=True)
if not ok:
    raise SystemExit("the newest release's driver does not start")
while lo < hi:
    mid = (lo + hi) // 2
    ok, what = probe(versions[mid])
    print(f"{versions[mid]} | {'ok' if ok else 'FAIL'} | {what}", flush=True)
    if ok:
        hi = mid
    else:
        lo = mid + 1
print(f"# oldest whose driver starts on Linux: {versions[lo]}", flush=True)
vm("podman", "rm", "-f", "f37-pwprobe")
