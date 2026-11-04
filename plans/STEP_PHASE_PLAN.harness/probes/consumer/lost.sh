#!/usr/bin/env bash
# The reference consumer (BreakfastProvider at 640c190, a scratch clone) on 5.28.2 and on the final patch, lane by lane, in memory: phases, diagrams, fingerprints.
set -u
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/31780689-26c4-4084-b1b5-c4efb9a6fd42/scratchpad
W=$S/on226; B=$S/bp; R=$B/repo; FEED=$B/feed; V=5.28.3-bp; OUT=$S/rr/results/consumer
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
OUT=$S/rr/results/consumer-lost
mkdir -p $OUT
lane=LightBDD
dir="$R/tests/BreakfastProvider.Tests.Component.$lane"
for i in 1 2 3 4 5 6; do
  out="$OUT/run$i"; rm -rf "$out"; mkdir -p "$out"
  pins 5.28.2
  (cd "$dir" && rm -rf bin obj && KRONIKOL_SUITE="probe-$lane" timeout 1500 dotnet test "$(basename "$dir").csproj" -p:WarningLevel=0 > "$out/test-output.txt" 2>&1; echo "run $i exit $?")
  reports="$dir/bin/Debug/net10.0/Reports"
  [ -d "$reports" ] || reports=$(find "$dir/bin" -type d -name Reports 2>/dev/null | head -1)
  cp "$reports/History.run.json" "$out/"
  zero=$(/c/Python314/python.exe -I - "$out/History.run.json" <<'PY'
import json,sys
d=json.load(open(sys.argv[1],encoding='utf-8')); ro=d['roster']; r=d['run']
print(sum(1 for c in r['calls'] if c==0), 'total', sum(r['calls']))
PY
)
  echo "  zero rows and total: $zero"
  case "$zero" in
    0\ *) ;;
    *)
      (cd "$reports" && {
        dotnet run --file query.cs -- summary . --max-bytes 0 > "$out/summary.txt" 2>&1
        dotnet run --file query.cs -- interactions . --method POST --grep /waffles --max-bytes 0 --limit 500 > "$out/post-waffles.txt" 2>&1
        dotnet run --file query.cs -- interactions . --status UnsupportedMediaType --max-bytes 0 --limit 200 > "$out/all-415.txt" 2>&1
        dotnet run --file query.cs -- interactions . --group-by status,caller,service --max-bytes 0 > "$out/by-status.txt" 2>&1
        dotnet run --file query.cs -- interactions . --group-by caller,service,cause,origin --max-bytes 0 > "$out/by-cause.txt" 2>&1
        ls -la > "$out/reports-listing.txt"; })
      echo "  kept queries in $out"
      rm -rf "$dir/bin" "$dir/obj"
      break ;;
  esac
  rm -rf "$dir/bin" "$dir/obj"
done
git -C $R checkout -q -- . 2>/dev/null
echo LOST_DONE
