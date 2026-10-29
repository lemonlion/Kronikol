#!/bin/sh
# R3a's third adapter batch (plans/CLIENT_FLOORS_PLAN.md 5.2), after the first two found three faults of the harness and
# the suites: xUnit2 again, now that TagMappingTests builds on xunit.assert 2.6.1 to 2.9.0; MSTest again, now that
# a probe which finds no test fails (F29) and the floor run takes the test SDK each MSTest release asks for; and TUnit
# from the pin down, as the adapter's HeadInAttribute does not build against the newest TUnit.Core.
#   sh r3a-adapters3.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness
echo "# the run below, after TagMappingTests' expected tags became arrays (the first stopped below 2.9.1 on CS0121)" >> "$H/floor/r3a-xunit2.txt"
python -I "$H/floor/paired_floor.py" --worktree "$W" --project src/Kronikol.xUnit2/Kronikol.xUnit2.csproj \
  --tests tests/Kronikol.Tests.xUnit2 --pin xunit.extensibility.execution --series xunit,xunit.extensibility.execution \
  --property XunitFloor --major 2 --out "$H/floor/r3a-xunit2.txt" --logs "$LOGS/xUnit2b"
echo "# the run below, after a probe that finds no test fails (F29) and the floor run takes MSTest's own test SDK" >> "$H/floor/r3a-mstest.txt"
python -I "$H/floor/paired_floor.py" --worktree "$W" --project src/Kronikol.MSTest/Kronikol.MSTest.csproj \
  --tests tests/Kronikol.Tests.MSTest --pin MSTest.TestFramework --series MSTest,MSTest.TestFramework \
  --property MSTestFloor --major 4 --out "$H/floor/r3a-mstest.txt" --logs "$LOGS/MSTestb"
echo "# the run below, from the pin down (--to 1.43.11): the adapter's HeadInAttribute does not build against 1.76.45" >> "$H/floor/r3a-tunit.txt"
python -I "$H/floor/floor_probe2.py" --worktree "$W" --project src/Kronikol.TUnit/Kronikol.TUnit.csproj \
  --package TUnit.Core --tests tests/Kronikol.Tests.TUnit --out "$H/floor/r3a-tunit.txt" --logs "$LOGS/TUnitb" \
  --search --to 1.43.11
echo ALLDONE > "$H/floor/r3a-adapters3.done"
