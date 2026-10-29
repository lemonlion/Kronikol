#!/bin/sh
# R3a's second adapter batch (plans/CLIENT_FLOORS_PLAN.md 5.2): xUnit2, whose test project is an xUnit v2 project taking
# xunit at the release probed (-p:XunitFloor), and TUnit, whose test project runs on xUnit v3 and takes TUnit.Core only
# through the adapter, so its pin moves it.
#   sh r3a-adapters2.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness
python -I "$H/floor/paired_floor.py" --worktree "$W" --project src/Kronikol.xUnit2/Kronikol.xUnit2.csproj \
  --tests tests/Kronikol.Tests.xUnit2 --pin xunit.extensibility.execution --series xunit,xunit.extensibility.execution \
  --property XunitFloor --major 2 --out "$H/floor/r3a-xunit2.txt" --logs "$LOGS/xUnit2"
python -I "$H/floor/floor_probe2.py" --worktree "$W" --project src/Kronikol.TUnit/Kronikol.TUnit.csproj \
  --package TUnit.Core --tests tests/Kronikol.Tests.TUnit --out "$H/floor/r3a-tunit.txt" --logs "$LOGS/TUnit" --search
echo ALLDONE > "$H/floor/r3a-adapters2.done"
