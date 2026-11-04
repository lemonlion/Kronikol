#!/bin/bash
# The core suite on R2 alone and on the final cut, after R2's exact compare.
set -u
export MSBUILDDISABLENODEREUSE=1
H=/c/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness
r="$H/results"
core() { # worktree header out
  { echo "# $2"; cd "$1" && timeout 1500 dotnet test tests/Kronikol.Tests/Kronikol.Tests.csproj -c Release -p:WarningLevel=0 2>&1 | grep -E "^(Passed!|Failed!)|^\s+Failed [A-Z]| error " | cut -c1-180 | sort -u | head -30; } > "$3" 2>&1
}
core /c/Code/k225-m "The core suite on R2 alone, on 9d994a4b: prototype/r2-alone.patch" "$r/r2-core.txt" &
core /c/Code/k225-w "The core suite on the final cut: #223's 638f275d with this plan's layer and R2, prototype/on-223-638f275d.patch" "$r/core-638f275d.txt" &
wait
echo "== core done $(date +%H:%M:%S)"
tail -1 "$r/r2-core.txt" | cut -c1-110
tail -1 "$r/core-638f275d.txt" | cut -c1-110
df -B1M /c | tail -1 | cut -c20-
