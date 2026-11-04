#!/usr/bin/env bash
# What a run on the fixed build says about the runs before it: history's verdicts, `query diff`, and a merge.
#
#   upgrade-across-the-fix.sh <work-dir> <kronikol-exe> <dir-with-fixed-dlls>
#
# Seven green runs on the published 5.27.13 packages write one ledger, then one run on the fixed assemblies appends to it. The suite is
# the shape matrix, whose steps move between b0.. and 0.. across the fix, so anything that reads a step's address would show it here.
set -u
work=$1; kronikol=$2; dlls=$3
harness=$(cd "$(dirname "$0")/.." && pwd)
suite=$work/upgrade
rm -rf "$suite"; mkdir -p "$suite"; cp -r "$harness/probes/shapes-suite/." "$suite/"
cd "$suite" || exit 1
export KRONIKOL_KEEP_RUNS=off KRK_MERGEABLE=1
mkdir -p .kronikol  # the ledger lives above the report
dotnet build -c Release 2>&1 | grep -E " error |Build succeeded" | head -3 | cut -c1-160
reports=bin/Release/net10.0/Reports

for i in 1 2 3 4 5 6 7; do
  KRK_MODE=pass dotnet test -c Release --no-build 2>&1 | tail -1 | cut -c1-80
done
rm -rf "$work/upgrade-before"; cp -r "$reports" "$work/upgrade-before"
echo "--- history after the seventh run on 5.27.13"
"$kronikol" query history "$reports" 2>&1 | head -6 | cut -c1-150

cp "$dlls/Kronikol.dll" "$dlls/Kronikol.ReqNRoll.Core.dll" bin/Release/net10.0/
KRK_MODE=pass dotnet test -c Release --no-build 2>&1 | tail -1 | cut -c1-80
rm -rf "$work/upgrade-after"; cp -r "$reports" "$work/upgrade-after"
echo "--- history after one run on the fixed build"
"$kronikol" query history "$reports" 2>&1 | head -12 | cut -c1-150
echo "--- the fixed run's console line about history, if any, and its summary"
"$kronikol" query summary "$reports" 2>&1 | grep -i -E "history|scenarios|passed|failed" | head -8 | cut -c1-150

echo "--- query diff, 5.27.13 against the fixed build"
"$kronikol" query diff "$work/upgrade-before" "$work/upgrade-after" 2>&1 | head -16 | cut -c1-150

echo "--- merge of the fixed run"
rm -f "$work"/upgrade-merged*
"$kronikol" merge "$work/upgrade-after" -o "$work/upgrade-merged.html" 2>&1 | tail -2 | cut -c1-150
address=$("$kronikol" query scenarios "$work/upgrade-merged.krkl" 2>&1 | grep "01 one" | cut -d' ' -f1)
"$kronikol" query flow "$work/upgrade-merged.krkl" "$address" 2>&1 | head -9 | cut -c1-120
