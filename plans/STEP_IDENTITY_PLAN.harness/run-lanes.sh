#!/usr/bin/env bash
# The triggers on Reqnroll's other lanes. usage: run-lanes.sh <scratch-dir> <prototype-worktree> <kronikol-version>
# Results: results/lane-<lane>-<build>.txt, read through each run's own query.cs. TUnit runs by dotnet run (dotnet test refuses it on this SDK).
set -u
SCRATCH="$1"; PROTO="$2"; VERSION="$3"
HERE="$(cd "$(dirname "$0")" && pwd)"
run_one() {
  local lane="$1" build="$2" extra="${3:-}"
  local dir="$SCRATCH/lane-$lane-$build"
  python "$HERE/make-lane.py" "$lane" "$build" "$dir" "$VERSION" "$PROTO" >/dev/null
  echo "=== $lane $build"
  if [ "$lane" = tunit ]; then
    (cd "$dir" && timeout 900 dotnet run --project . -c Debug $extra 2>&1 | grep -E " error |Build FAILED|failed: |succeeded: |total: |Test run summary" | cut -c1-120 | sort -u | head -8)
  else
    (cd "$dir" && timeout 900 dotnet test --nologo -v q $extra 2>&1 | grep -E " error |Build FAILED|Failed!|Passed!" | cut -c1-150 | sort -u | head -6)
  fi
  local reports
  reports="$(find "$dir/bin" -type d -name Reports | head -1)"
  if [ -z "$reports" ]; then echo "   no Reports folder"; return; fi
  bash "$HERE/read-report.sh" "$reports" "$HERE/results/lane-$lane-$build.txt" | cut -d/ -f1-1
}
if [ -n "${ONLY_PROTO:-}" ]; then
  run_one xunit2 proto
  run_one xunit3 proto
  run_one tunit proto
  run_one floor proto "-p:FloorRun=true"
  echo "=== done"; df -h /c | tail -1
  exit 0
fi
run_one xunit3 pub
run_one xunit3 proto
run_one tunit pub
run_one tunit proto
[ -n "${WITH_FLOOR:-}" ] && { run_one floor pub; run_one floor proto "-p:FloorRun=true"; }
echo "=== done"; df -h /c | tail -1
