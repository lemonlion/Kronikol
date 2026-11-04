#!/usr/bin/env bash
# The shape matrix on another Reqnroll adapter or generator setting, in every mode: run-lane.sh <xunit3|tunit|norowtests> <label> <work-dir> <kronikol-exe> [dir-with-replacement-dlls]
# Writes results/shapes-<label>-lane-<lane>-<mode>.txt. The first cut of these lanes was one green run each; a failing step takes a different path through the hooks.
set -u
lane=$1; label=$2; work=$3; kronikol=$4; dlls=${5:-}
harness=$(cd "$(dirname "$0")/.." && pwd)
suite=$work/lane-$lane-$label
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off

rm -rf "$suite"; mkdir -p "$suite"; cp -r "$harness/probes/shapes-suite/." "$suite/"
cd "$suite" || exit 1
python -I "$harness/probes/lane-switch.py" "$lane"
dotnet build -c Release 2>&1 | grep -E " error |Build succeeded" | sort -u | head -6 | cut -c1-200
if [ -n "$dlls" ]; then
  for name in Kronikol.dll Kronikol.ReqNRoll.Core.dll; do cp "$dlls/$name" bin/Release/net10.0/; done
fi

for mode in pass fail-and fail-when fail-bg fail-bg2; do
  rm -rf bin/Release/net10.0/Reports
  if [ "$lane" = tunit ]; then
    (cd bin/Release/net10.0 && KRK_MODE=$mode dotnet Shapes.Suite.dll 2>&1 | grep -E "^(  )?(total|failed|succeeded):|summary" | tr -s ' ' | tr '\n' ' ' | cut -c1-110); echo
  else
    KRK_MODE=$mode dotnet test -c Release --no-build 2>&1 | tail -1 | cut -c1-100
  fi
  table=$harness/results/shapes-$label-lane-$lane-$mode.txt
  python -I "$harness/probes/shapes-table.py" "$kronikol" bin/Release/net10.0/Reports > "$table" 2>&1
  printf '%s %s: scenarios=%s ' "$lane" "$mode" "$(grep -c -E '^[0-9]{2} .*(MISSED|INVENTED|BORROWED|ok|right)' "$table")"
  for verdict in MISSED INVENTED BORROWED; do printf '%s=%s ' "$verdict" "$(grep -c "$verdict" "$table")"; done
  echo
done
echo FINISHED
