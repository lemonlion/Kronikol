#!/bin/sh
# Reruns the reviewer's probes on its copies. Usage: run_review_probes.sh <review dir> <out file> <suite>...
REVIEW="$1"; OUT="$2"; shift 2
TOOL="$REVIEW/proto/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
[ -f "$TOOL" ] || TOOL=$(ls "$REVIEW"/proto/src/Kronikol.Tool/bin/Release/net*/Kronikol.Tool.dll | head -1)
: > "$OUT"
for suite in "$@"; do
  for copy in base proto; do
    d="$REVIEW/probes/$suite-$copy"
    [ -d "$d" ] || continue
    echo "=================== $suite $copy" >> "$OUT"
    if [ -z "$NO_RUN" ]; then
      if grep -q "TUnit" "$d"/*.csproj 2>/dev/null; then
        (cd "$d" && timeout 280 dotnet run -c Release > run.log 2>&1)
      else
        (cd "$d" && timeout 280 dotnet test -c Release --nologo > run.log 2>&1)
      fi
    fi
    grep -E "^(Passed|Failed)!| error |succeeded:|failed:" "$d/run.log" | head -3 | cut -c1-110 >> "$OUT"
    # Release only: a Debug folder from an earlier run would be read as this run's (it was, once).
    reports="$d/bin/Release/net10.0/Reports"
    [ -d "$reports" ] || reports=$(find "$d/bin/Release" -type d -name Reports | head -1)
    [ -n "$reports" ] || { echo "no reports" >> "$OUT"; continue; }
    n=$(dotnet "$TOOL" query scenarios "$reports" 2>&1 | grep -cE "^s[0-9]+ ")
    i=0
    while [ "$i" -lt "$n" ]; do
      dotnet "$TOOL" query steps "$reports" "s$i" 2>&1 | grep -v "^stableId\|^at \|^open:\|^$\|^[0-9]* calls ·" | cut -c1-105 >> "$OUT"
      dotnet "$TOOL" query interactions "$reports" "s$i" --group-by step,path 2>&1 | grep -v "buckets\|^step " | cut -c1-42 >> "$OUT"
      i=$((i+1))
    done
    dotnet "$TOOL" query summary "$reports" 2>&1 | grep -E "StepAttributionMismatch" | cut -c1-120 >> "$OUT"
  done
done
echo FINISHED >> "$OUT"
