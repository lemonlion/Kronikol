#!/usr/bin/env bash
# Build BreakfastProvider once, then run lanes several times, copying each run's TestRunReport.json out.
# Usage: runlanes.sh <out-dir> <lane:count> [<lane:count> ...]   lanes: ReqNRoll, xUnit, NUnit, TUnit, LightBDD
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
BP="${BP:-$HERE/bp}"   # S7: BP=<scratch clone> runs a clone elsewhere
OUT="$1"; shift
mkdir -p "$OUT"
export KRONIKOL_HISTORY=off
export DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$BP"
echo "[$(date +%T)] build" | tee -a "$OUT/log.txt"
dotnet build BreakfastProvider.sln -c Debug -v q -nologo > "$OUT/build.txt" 2>&1 || { echo "build failed" | tee -a "$OUT/log.txt"; exit 1; }
for spec in "$@"; do
  lane="${spec%%:*}"; count="${spec##*:}"
  proj="tests/BreakfastProvider.Tests.Component.$lane"
  for i in $(seq 1 "$count"); do
    echo "[$(date +%T)] $lane run $i" | tee -a "$OUT/log.txt"
    # drop the previous run's report so we copy this run's
    find "$proj/bin" -name TestRunReport.json -path '*Reports*' -delete 2>/dev/null
    start=$(date +%s)
    dotnet test --project "$proj" --no-build > "$OUT/$lane-$i.test.txt" 2>&1
    rc=$?
    end=$(date +%s)
    rep=$(find "$proj/bin" -name TestRunReport.json -path '*Reports*' -newermt "@$start" 2>/dev/null | head -1)
    mkdir -p "$OUT/$lane-$i"
    if [ -n "$rep" ]; then cp "$rep" "$OUT/$lane-$i/TestRunReport.json"; fi
    # The pages too, for html_marks.py (4.9.1)
    if [ -n "$rep" ]; then for page in TestRunReport.html Specifications.html; do [ -f "$(dirname "$rep")/$page" ] && cp "$(dirname "$rep")/$page" "$OUT/$lane-$i/"; done; fi
    echo "[$(date +%T)] $lane run $i rc=$rc $((end-start))s report=${rep:-none}" | tee -a "$OUT/log.txt"
    git checkout -q -- docs 2>/dev/null
  done
done
echo "[$(date +%T)] done" | tee -a "$OUT/log.txt"
