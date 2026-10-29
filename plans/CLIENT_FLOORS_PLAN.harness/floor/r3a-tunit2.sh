#!/bin/sh
# R3a's TUnit search again (plans/CLIENT_FLOORS_PLAN.md 5.2), on 5.27.8's adapter (F30: [HeadIn] takes the interface
# through TUnit's base class, so the newest release builds) and with HeadInAttributeTests setting ReflectionInfo, which
# TUnit.Core asks for before 1.19.0; each probe from an empty bin/ and obj/.
#   sh r3a-tunit2.sh <worktree> <logs>
W=$1; LOGS=$2; F=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
echo "# the run below, on 5.27.8's adapter (F30) with the test helper building on every 1.x" >> "$F/r3a-tunit.txt"
python -I "$F/floor_probe2.py" --worktree "$W" --project src/Kronikol.TUnit/Kronikol.TUnit.csproj \
  --package TUnit.Core --tests tests/Kronikol.Tests.TUnit --out "$F/r3a-tunit.txt" --logs "$LOGS/TUnit-f30" --search
echo ALLDONE > "$F/r3a-tunit2.done"
