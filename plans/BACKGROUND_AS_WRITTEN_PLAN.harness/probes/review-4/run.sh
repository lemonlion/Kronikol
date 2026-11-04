#!/usr/bin/env bash
# run.sh <suite-dir> <label: old|new|other> <dll-dir or ""> [filter] : runs the suite and keeps the reports folder under <suite-dir>/out-<label>
set -u
suite=$1; label=$2; dlls=${3:-}; filter=${4:-}
cd "$suite" || exit 1
bin=bin/Release/net10.0
if [ ! -d "$suite/published-dlls" ]; then
  mkdir -p "$suite/published-dlls"
  cp "$bin/Kronikol.dll" "$bin/Kronikol.ReqNRoll.Core.dll" "$suite/published-dlls/"
fi
if [ -n "$dlls" ]; then src=$dlls; else src=$suite/published-dlls; fi
cp "$src/Kronikol.dll" "$src/Kronikol.ReqNRoll.Core.dll" "$bin/"
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
rm -rf "$bin/Reports" "out-$label"
if [ -n "$filter" ]; then
  dotnet test -c Release --no-build --filter "$filter" --logger "console;verbosity=normal" > "console-$label.txt" 2>&1
else
  dotnet test -c Release --no-build --logger "console;verbosity=normal" > "console-$label.txt" 2>&1
fi
echo "exit $?"
grep -E "^(Passed!|Failed!)|Total tests:|Test Assembly Cleanup Failure|Unhandled" "console-$label.txt" | head -5 | cut -c1-140
cp -r "$bin/Reports" "out-$label"
ls "out-$label" | head -20 | tr '\n' ' '; echo
