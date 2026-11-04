#!/usr/bin/env bash
# BackForwardRestoreTests.R16 on pristine main and on the patch, by turns, so both see the same load.
# Usage: run.sh <pristine tree> <patched tree> <rounds> [filter], with each failure's first line of message.
pristine=$1; patched=$2; rounds=$3; filter=${4:-FullyQualifiedName~R16_Back_lands}
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
for tree in "$pristine" "$patched"; do
  ( cd "$tree" && dotnet build tests/Kronikol.Tests.EndToEnd -c Release -p:GeneratePackageOnBuild=false -v q 2>&1 | grep -E " error |Build succeeded" | head -2 | cut -c1-120 )
done
for round in $(seq 1 "$rounds"); do
  for label in pristine patched; do
    if [ "$label" = pristine ]; then tree=$pristine; else tree=$patched; fi
    out=$( cd "$tree" && dotnet test tests/Kronikol.Tests.EndToEnd -c Release --no-build --filter "$filter" 2>&1 )
    summary=$(echo "$out" | grep -E "^(Passed|Failed)!" | sed -E 's/, Skipped.*Duration: / in /; s/ - Kronikol.*//' | cut -c1-70)
    failed=$(echo "$out" | grep -E "^\s+Failed " | sed -E 's/.*engine: "([a-z]+)".*/\1/' | tr '\n' ' ')
    message=$(echo "$out" | grep -A1 -E "Error Message:" | tail -1 | sed -E 's/^ +//' | cut -c1-140)
    echo "$round $label: $summary ${failed:+failed on: $failed | $message}"
  done
done
echo FINISHED
