#!/bin/bash
# The lanes around step tracking, then the core suite, then the example suites, on a prototype's worktree. Each lane's summary line, as the runner printed it.
# Usage: run_lanes.sh <worktree> <core: yes or no>
set -u
export MSBUILDDISABLENODEREUSE=1
W=$1; CORE=${2:-yes}
cd "$W" || exit 1
summary='^(Passed!|Failed!)|Test Run (Passed|Failed)|Test run summary|^ *(total|failed|succeeded|skipped): |^\s+Failed [A-Z]|\[FAIL\]| error '
run() { # label project
  echo "=== $1 $(date +%H:%M:%S)"
  # The summary line first: a tail of the sorted lines lost it to the [FAIL] lines of a lane with a dozen failures.
  timeout 1500 dotnet test "$2" -c Release -p:WarningLevel=0 2>&1 | grep -E "$summary" | cut -c1-180 | sort -u > "$W/.lane-lines.txt"
  grep -E '^(Passed!|Failed!)|Test Run (Passed|Failed)' "$W/.lane-lines.txt"
  grep -vE '^(Passed!|Failed!)|Test Run (Passed|Failed)' "$W/.lane-lines.txt" | tail -40
  rm -f "$W/.lane-lines.txt"
}
for lane in StepTracking AssertionTracking xUnit2 xUnit3 NUnit4 MSTest TUnit LightBDD.xUnit3 LightBDD.xUnit2 LightBDD.TUnit; do
  run "$lane" "tests/Kronikol.Tests.$lane/Kronikol.Tests.$lane.csproj"
done
if [ "$CORE" = yes ]; then
  run core tests/Kronikol.Tests/Kronikol.Tests.csproj
fi
for suite in BDDfy.xUnit3 LightBDD.xUnit3 LightBDD.xUnit2 LightBDD.TUnit ReqNRoll.xUnit2 ReqNRoll.xUnit3 ReqNRoll.TUnit xUnit3 xUnit2 NUnit4 TUnit; do
  project="examples/Example.Api/tests/Example.Api.Tests.Component.$suite/Example.Api.Tests.Component.$suite.csproj"
  case "$suite" in
    *TUnit)
      # dotnet test does not start a TUnit suite here (Microsoft.Testing.Platform).
      echo "=== example $suite $(date +%H:%M:%S)"
      timeout 900 dotnet run --project "$project" -c Release -p:WarningLevel=0 2>&1 | grep -E "$summary|Test run summary|Passed|Failed" | cut -c1-180 | sort -u | tail -6
      ;;
    *) run "example $suite" "$project" ;;
  esac
done
echo "== done $(date +%H:%M:%S)"
