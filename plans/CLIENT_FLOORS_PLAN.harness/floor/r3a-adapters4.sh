#!/bin/sh
# R3a's fourth adapter batch (plans/CLIENT_FLOORS_PLAN.md 5.2): MSTest, xUnit3 and xUnit2 again, each probe now built
# from an empty bin/ and obj/ (F31): MSTest 4.1.0 and older had run on a newer probe's adapter, which found no test.
#   sh r3a-adapters4.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness
search() { # name pin series property major
  echo "# the run below, each probe built from an empty bin/ and obj/ (F31)" >> "$H/floor/r3a-$(echo $1 | tr 'A-Z' 'a-z').txt"
  python -I "$H/floor/paired_floor.py" --worktree "$W" --project "src/Kronikol.$1/Kronikol.$1.csproj" \
    --tests "tests/Kronikol.Tests.$1" --pin "$2" --series "$3" --property "$4" --major "$5" \
    --out "$H/floor/r3a-$(echo $1 | tr 'A-Z' 'a-z').txt" --logs "$LOGS/$1-clean"
}
search MSTest MSTest.TestFramework MSTest,MSTest.TestFramework MSTestFloor 4
search xUnit3 xunit.v3.extensibility.core xunit.v3,xunit.v3.extensibility.core XunitV3Floor 3
search xUnit2 xunit.extensibility.execution xunit,xunit.extensibility.execution XunitFloor 2
echo ALLDONE > "$H/floor/r3a-adapters4.done"
