#!/usr/bin/env bash
# The reference consumer (BreakfastProvider at 640c190, a scratch clone) on 5.28.2 and on the final patch, lane by lane, in memory: phases, diagrams, fingerprints.
set -u
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/31780689-26c4-4084-b1b5-c4efb9a6fd42/scratchpad
W=$S/cutw; B=$S/bp; R=$B/repo; FEED=$B/feed; V=5.28.3-bp; OUT=$S/rr/results/consumer
mkdir -p $FEED $OUT
git -C $W checkout -q -- . ; git -C $W clean -fdq src tests
git -C $W apply --whitespace=nowarn $S/rr/prototype/r1-r2-r3.patch || { echo APPLY_FAILED; exit 1; }
for project in Kronikol Kronikol.StepTracking Kronikol.ReqNRoll.Core Kronikol.ReqNRoll.xUnit3 Kronikol.LightBDD.Core Kronikol.LightBDD.xUnit3 Kronikol.xUnit3 Kronikol.BDDfy.xUnit3 Kronikol.NUnit4 Kronikol.TUnit; do
    (cd $W/src/$project && dotnet build -c Release -p:Version=$V -p:PackageVersion=$V -p:GeneratePackageOnBuild=true -p:PackageOutputPath=$FEED --nologo -v q 2>&1 | grep -E " error |Build succeeded" | cut -c1-140 | head -2 | sed "s/^/$project: /")
done
ls $FEED | grep -c "$V" | sed "s/^/packages: /"
git -C $W checkout -q -- . ; git -C $W clean -fdq src tests
for cached in "$USERPROFILE"/.nuget/packages/kronikol*/$V; do rm -rf "$cached" 2>/dev/null; done
cat > $R/nuget.config <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /><add key="prototype" value="$(cygpath -m $FEED)" /></packageSources></configuration>
XML
pins() { # version for the patched packages
  /c/Python314/python.exe -I - "$R" "$1" <<'PY'
import pathlib,re,sys
root=pathlib.Path(sys.argv[1]); v=sys.argv[2]
patched={'Kronikol','Kronikol.StepTracking','Kronikol.ReqNRoll.xUnit3','Kronikol.LightBDD.xUnit3','Kronikol.xUnit3','Kronikol.BDDfy.xUnit3','Kronikol.NUnit4','Kronikol.TUnit'}
for p in list(root.rglob('*.csproj'))+list(root.rglob('*.props')):
    if any(x in p.parts for x in ('bin','obj')): continue
    t=p.read_bytes().decode('utf-8'); 
    n=re.sub(r'Include="(Kronikol[A-Za-z.0-9]*)" Version="[^"]+"',lambda m:f'Include="{m[1]}" Version="{v if m[1] in patched else "5.28.2"}"',t)
    if n!=t: p.write_bytes(n.encode('utf-8'))
PY
}
lane() { # label version lane
  local label="$1" version="$2" lane="$3"
  local dir="$R/tests/BreakfastProvider.Tests.Component.$lane" out="$OUT/$label/$lane"
  rm -rf "$out"; mkdir -p "$out/diagrams"
  pins "$version"
  (cd "$dir" && rm -rf bin obj && KRONIKOL_SUITE="probe-$lane" timeout 1500 dotnet test "$(basename "$dir").csproj" -p:WarningLevel=0 > "$out/test-output.txt" 2>&1; echo "$lane $label exit $?"; grep -E "Test run summary|total:|failed:|succeeded:|^(Passed|Failed)!" "$out/test-output.txt" | cut -c1-100 | head -5)
  local reports="$dir/bin/Debug/net10.0/Reports"
  if [ ! -d "$reports" ]; then reports=$(find "$dir/bin" -type d -name Reports 2>/dev/null | head -1); fi
  if [ -z "$reports" ] || [ ! -d "$reports" ]; then echo "$lane $label: no Reports folder"; else
    cp "$reports/History.run.json" "$out/" 2>/dev/null
    (cd "$reports" && dotnet run --file query.cs -- interactions . --group-by phase,caller,service 2>&1 | cut -c1-110 > "$out/phases.txt")
    (cd "$reports" && dotnet run --file query.cs -- interactions . --group-by phase 2>&1 | cut -c1-90 > "$out/phase-totals.txt")
    (cd "$reports" && dotnet run --file query.cs -- diagram . --out "$out/diagrams" > "$out/diagram-list.txt" 2>&1)
  fi
  rm -rf "$dir/bin" "$dir/obj"
}
for l in ReqNRoll BDDfy xUnit LightBDD; do
  lane final $V $l
done
# The LightBDD lane again, as a run of 5.28.2 lost calls in some runs: three more on the patch.
for i in 2 3 4; do lane final$i $V LightBDD; done
git -C $R checkout -q -- . 2>/dev/null
echo BP_DONE
