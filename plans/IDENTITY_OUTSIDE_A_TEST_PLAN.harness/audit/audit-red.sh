#!/bin/bash
# The audit's new facts on a given worktree: run_red.sh <worktree> <out file>
WT="$1"; OUT="$2"; : > "$OUT"
cd "$WT" || exit 1
echo "worktree $(git describe --tags --always 2>/dev/null) ($(git rev-parse --short HEAD))" >> "$OUT"
run() {  # run <project> <filter>
  echo "== $1 ($2)" >> "$OUT"
  dotnet test "tests/$1" --filter "$2" 2>&1 | grep -E ' error |\[FAIL\]|Passed!|Failed!|^\s+(Expected|Actual|But was)|Assert\.' | sed -E 's/^\s+/  /' | cut -c1-200 >> "$OUT"
}
run Kronikol.Tests.xUnit2 "FullyQualifiedName~PhaseSetIn"
run Kronikol.Tests.NUnit4 "Name~StartAction_and_StartSetup"
run Kronikol.Tests.MSTest "Name~StartAction_and_StartSetup"
run Kronikol.Tests.TUnit "FullyQualifiedName~StartAction_and_StartSetup"
