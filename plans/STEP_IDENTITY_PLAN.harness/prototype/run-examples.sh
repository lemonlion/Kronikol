#!/usr/bin/env bash
# The examples' own suites on one build, read through their own query.cs, to compare the base with the prototype: none holds a hook step, so nothing should move.
# usage: run-examples.sh <label> <worktree>. Results: results/examples-<label>-<lane>.txt
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
LABEL="$1"; W="$2"
HERE="$(cd "$(dirname "$0")/.." && pwd)"
cd "$W/examples/Example.Api/tests" || exit 1
for lane in ReqNRoll.xUnit3 BDDfy.xUnit3 LightBDD.xUnit3 xUnit3; do
  P="Example.Api.Tests.Component.$lane"
  if [ ! -d "$P" ]; then echo "--- $lane: no such project"; continue; fi
  echo "--- $lane"
  (cd "$P" && timeout 560 dotnet test --nologo -v q -p:GeneratePackageOnBuild=false 2>&1 | grep -a -E "^\s*(Passed|Failed)!| error " | cut -c1-120 | sort -u | head -3)
  R="$(find "$P" -type f -name query.cs -not -path '*/obj/*' -printf '%T@ %h\n' 2>/dev/null | sort -n | tail -1 | cut -d' ' -f2-)"
  if [ -z "$R" ]; then echo "   no report found"; continue; fi
  bash "$HERE/read-lane.sh" "$R" "$HERE/results/examples-$LABEL-$lane.txt" | cut -d/ -f1 | head -1
done
df -h /c | tail -1
