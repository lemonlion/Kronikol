#!/bin/sh
# R3a's adapter searches (plans/CLIENT_FLOORS_PLAN.md 5.2): each test framework adapter's pin, with its test project, a
# project of that framework, taking the framework at the release probed (-p:<Framework>Floor), so the adapter runs on
# the framework it asks for. One after another, as they share the worktree's builds.
#   sh r3a-adapters.sh <worktree> <logs> ["<adapter> ...", by default xUnit3 NUnit4 MSTest]
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness
search() { # name pin series property major
  python -I "$H/floor/paired_floor.py" --worktree "$W" --project "src/Kronikol.$1/Kronikol.$1.csproj" \
    --tests "tests/Kronikol.Tests.$1" --pin "$2" --series "$3" --property "$4" --major "$5" \
    --out "$H/floor/r3a-$(echo $1 | tr 'A-Z' 'a-z').txt" --logs "$LOGS/$1"
}
for a in ${3:-xUnit3 NUnit4 MSTest}; do
  case $a in
    xUnit3) search xUnit3 xunit.v3.extensibility.core xunit.v3,xunit.v3.extensibility.core XunitV3Floor 3 ;;
    NUnit4) search NUnit4 NUnit NUnit NUnitFloor 4 ;;
    MSTest) search MSTest MSTest.TestFramework MSTest,MSTest.TestFramework MSTestFloor 4 ;;
  esac
done
echo ALLDONE > "$H/floor/r3a-adapters.done"
