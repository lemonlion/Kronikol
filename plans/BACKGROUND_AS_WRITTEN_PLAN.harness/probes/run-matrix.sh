#!/usr/bin/env bash
# Runs the shape matrix on one build of the adapter, in every mode, and writes one table per mode.
#
#   run-matrix.sh <label> <work-dir> <kronikol-exe> [dir-with-replacement-dlls] [extra dotnet build args...]
#
# <label> names the results (shapes-<label>-<mode>.txt). With a dll folder, its Kronikol*.dll replace the ones the published 5.27.13
# packages put in the suite's output, so a run differs from the published one by the fix alone and the suite is the same build.
# History and run rotation are off: each mode's report is copied out whole.
set -u
label=$1; work=$2; kronikol=$3; dlls=${4:-}; shift $(( $# < 4 ? $# : 4 )); extra=("$@")
harness=$(cd "$(dirname "$0")/.." && pwd)
suite=$work/$label
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off

rm -rf "$suite"; mkdir -p "$suite"; cp -r "$harness/probes/shapes-suite/." "$suite/"
cd "$suite" || exit 1
dotnet build -c Release "${extra[@]}" 2>&1 | grep -E "error|Build succeeded" | head -5 | cut -c1-200
if [ -n "$dlls" ]; then
  for name in Kronikol.dll Kronikol.ReqNRoll.Core.dll; do [ -f "$dlls/$name" ] && cp "$dlls/$name" bin/Release/net10.0/; done
fi

for mode in pass fail-and fail-when fail-bg fail-bg2; do
  KRK_MODE=$mode dotnet test -c Release --no-build "${extra[@]}" 2>&1 | tail -1 | cut -c1-100
  rm -rf "$work/$label-$mode"; cp -r bin/Release/net10.0/Reports "$work/$label-$mode"
  python -I "$harness/probes/shapes-table.py" "$kronikol" "$work/$label-$mode" > "$harness/results/shapes-$label-$mode.txt" 2>&1
done
# A file attached inside a step the scenarios share (shape 18): does the run still end in a report?
rm -rf bin/Release/net10.0/Reports
KRK_MODE=pass KRK_ATTACH=1 dotnet test -c Release --no-build --logger "console;verbosity=normal" "${extra[@]}" > "$work/$label-attach.console.txt" 2>&1
code=$?
{
  echo "dotnet test exit code: $code"
  grep -E "Total tests:|Passed:|Failed:|Failed!|Passed!|Test Assembly Cleanup Failure|KeyNotFoundException :" "$work/$label-attach.console.txt" | cut -c1-200 | head -8
  echo "Report files: $(ls bin/Release/net10.0/Reports | grep -c -E '^TestRunReport\.(html|krkl|json)$') of TestRunReport.html and .krkl"
  ls bin/Release/net10.0/Reports/attachments 2>/dev/null | sed 's/^/attachment copied: /'
  if ls bin/Release/net10.0/Reports/TestRunReport.krkl > /dev/null 2>&1; then
    for name in "18 one" "18 two"; do
      address=$("$kronikol" query scenarios bin/Release/net10.0/Reports | grep "$name" | cut -d' ' -f1)
      echo "--- $name"; "$kronikol" query attachments bin/Release/net10.0/Reports "$address" 2>&1 | cut -c1-120 | head -4
    done
  fi
} > "$harness/results/shapes-$label-attach.txt" 2>&1
cat "$harness/results/shapes-$label-attach.txt" | head -12

for verdict in MISSED INVENTED BORROWED; do
  echo "$label $verdict: $(grep -c " $verdict" "$harness/results/shapes-$label-pass.txt")"
done
