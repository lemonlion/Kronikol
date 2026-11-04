#!/usr/bin/env bash
# run-lane.sh <xunit3|tunit> <label> <work-root> <kronikol-exe> <dll-dir or ""> : the R8 suite on another adapter, read as run.sh reads it.
set -u
lane=$1; label=$2; work=$3; tool=$4; dlls=${5:-}
here=$(cd "$(dirname "$0")" && pwd)
suite=$work/$label
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off GITHUB_STEP_SUMMARY=
export KRK_R8_OUT=$suite/hooks.txt
mkdir -p "$suite"; cp -r "$here/suite/." "$suite/"
cd "$suite" || exit 1
python -X utf8 -I "$here/lane8.py" "$lane"
dotnet build -c Release -v q -p:GeneratePackageOnBuild=false 2>&1 | grep -E " error |Build succeeded|Elapsed" | sort -u | head -5 | cut -c1-200
bin=bin/Release/net10.0
if [ -n "$dlls" ]; then cp "$dlls/Kronikol.dll" "$dlls/Kronikol.ReqNRoll.Core.dll" "$bin/"; fi
rm -f "$KRK_R8_OUT"
if [ "$lane" = tunit ]; then
  (cd "$bin" && dotnet R8.Suite.dll > "$suite/console.txt" 2>&1)
else
  dotnet test -c Release --no-build --logger "console;verbosity=normal" > console.txt 2>&1
fi
echo "=== $label: $lane dlls=${dlls:-published}"
grep -E "^(Passed!|Failed!)|Total tests:|Test Assembly Cleanup Failure|Unhandled|^(  )?(total|failed|succeeded):" console.txt | head -6 | cut -c1-140
rm -rf out; cp -r "$bin/Reports" out 2>/dev/null
"$tool" query scenarios out --limit 500 --max-bytes 2000000 2>&1 | grep -E '^s[0-9]+' | while read -r address rest; do
  echo "--- $address $rest" | cut -c1-120
  "$tool" query steps out "$address" --max-bytes 2000000 2>&1 | grep -v -E "^(stableId|open:|\s*$)" | cut -c1-130
done
echo; echo "=== what the suite's hooks saw"; cat "$KRK_R8_OUT" 2>/dev/null | cut -c1-220
