"""Same case, several runs, published tool: does the mismatch reach service.json's degraded and the data file's diagnostics each time?"""
import json, os, pathlib, subprocess, sys
os.environ["KRONIKOL_HISTORY"] = "off"; os.environ["KRONIKOL_KEEP_RUNS"] = "off"
PUB = ["dotnet", "C:/Users/cex/.nuget/packages/kronikol.tool/5.28.2/tools/net10.0/any/Kronikol.Tool.dll"]
case = pathlib.Path("C:/Code/k225-rv/tmpA")
for i in range(8):
    out = pathlib.Path(f"C:/Code/k225-rv/out/race/{i}")
    out.mkdir(parents=True, exist_ok=True)
    done = subprocess.run([*PUB, "ingest", "captures", "--tests", "tests.ndjson", "-o", str(out), "--no-component-diagram", "--render", "browserjs"], cwd=case, capture_output=True, text=True, encoding="utf-8", errors="replace")
    stdout = sum("StepAttributionMismatch" in l for l in done.stdout.splitlines())
    deg = json.loads((out / "service.json").read_text(encoding="utf-8"))["run"]["degraded"]
    print(i, "stdout lines", stdout, "degraded", deg)
