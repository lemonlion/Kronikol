#!/usr/bin/env bash
# The reference consumer (BreakfastProvider at 640c190, a scratch clone, never the owner's) on a published version, lane by lane. Usage: consumer-published.sh <version>.
set -u
S=${KRONIKOL_SCRATCH:?set KRONIKOL_SCRATCH to a scratch folder holding bp/repo, a clone of BreakfastProvider at 640c190}
B=$S/bp; R=$B/repo; V=$1; OUT=$S/impl/consumer
mkdir -p "$OUT"
if [ ! -d "$R/.git" ]; then echo "no scratch clone at $R"; exit 1; fi
git -C "$R" checkout -q -- . 2>/dev/null
git -C "$R" log --oneline -1 | cut -c1-70
cat > "$R/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
XML
python -I - "$R" "$V" <<'PY'
import pathlib, re, sys
root = pathlib.Path(sys.argv[1]); v = sys.argv[2]
for p in list(root.rglob('*.csproj')) + list(root.rglob('*.props')):
    if any(x in p.parts for x in ('bin', 'obj')):
        continue
    t = p.read_bytes().decode('utf-8')
    n = re.sub(r'Include="(Kronikol[A-Za-z.0-9]*)" Version="[^"]+"', lambda m: f'Include="{m[1]}" Version="{v}"', t)
    if n != t:
        p.write_bytes(n.encode('utf-8'))
PY
for lane in ReqNRoll BDDfy xUnit LightBDD; do
    dir="$R/tests/BreakfastProvider.Tests.Component.$lane"; out="$OUT/$V/$lane"
    rm -rf "$out"; mkdir -p "$out"
    (cd "$dir" && rm -rf bin obj && KRONIKOL_SUITE="probe-$lane" timeout 1500 dotnet test "$(basename "$dir").csproj" -p:WarningLevel=0 > "$out/test-out.txt" 2>&1)
    grep -E '^(Passed|Failed)!' "$out/test-out.txt" | cut -c1-110
    grep -E 'NU1603|error NU' "$out/test-out.txt" | head -2 | cut -c1-140
    reports="$dir/bin/Debug/net10.0/Reports"
    if [ ! -d "$reports" ]; then reports=$(find "$dir/bin" -type d -name Reports 2>/dev/null | head -1); fi
    if [ -z "$reports" ] || [ ! -d "$reports" ]; then echo "$lane: no Reports folder"; else
        (cd "$reports" && dotnet run --file query.cs -- interactions . --group-by phase 2>&1 | cut -c1-90 > "$out/phase-totals.txt")
        (cd "$reports" && dotnet run --file query.cs -- summary . 2>&1 | cut -c1-120 | head -12 > "$out/summary.txt")
        echo "== $lane"; grep -iE 'setup|action|unknown' "$out/phase-totals.txt" | head -4 | cut -c1-70
    fi
    rm -rf "$dir/bin" "$dir/obj"
done
git -C "$R" checkout -q -- . 2>/dev/null
rm -f "$R/nuget.config"
echo BP_DONE
