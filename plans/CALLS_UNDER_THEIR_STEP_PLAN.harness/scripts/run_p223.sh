#!/bin/sh
# The review's wrong-row probes on #223's prototype with the whole path switched on.
R="$1"; OUT="$2"
TOOL="$R/proto/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
[ -f "$TOOL" ] || TOOL=$(ls "$R"/proto/src/Kronikol.Tool/bin/Release/net*/Kronikol.Tool.dll | head -1)
(cd "$R/p223" && dotnet build src/Kronikol.StepTracking -c Debug --nologo -v q 2>&1 | grep -E " error |Build succeeded" | cut -c1-120 | head -3)
: > "$OUT"
echo "The review's wrong-row probes on #223's prototype ($(git -C "$R/p223" rev-parse --short=8 HEAD)), with its map changed to give the whole path (scripts/p223_deep.py). Read by this plan's query tool." >> "$OUT"
for suite in rq lb bddfy; do
  d="$R/probes/$suite-p223"
  rm -rf "$d"; mkdir -p "$d"
  (cd "$R/probes/$suite-proto" && tar --exclude=bin --exclude=obj --exclude=run.log --exclude=TestResults -cf - .) | tar -xf - -C "$d"
  for f in "$d"/*.csproj; do sed -i "s#proto</KRoot>#p223</KRoot>#" "$f"; done
  grep -h "<KRoot>" "$d"/*.csproj | cut -c1-90
  echo "=================== $suite on #223's pairing" >> "$OUT"
  (cd "$d" && timeout 500 dotnet test -c Release --nologo > run.log 2>&1)
  grep -E "^(Passed|Failed)!| error " "$d/run.log" | head -3 | cut -c1-140 >> "$OUT"
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
done
echo FINISHED >> "$OUT"
