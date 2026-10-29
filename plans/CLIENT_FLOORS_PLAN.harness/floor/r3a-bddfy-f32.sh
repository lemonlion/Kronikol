#!/bin/sh
# R3a's BDDfy search after F32 (plans/CLIENT_FLOORS_PLAN.md 5.2): the adapter reads BDDfy's humaniser by reflection,
# so it builds against 8.0.11, where Scanners.Humanize turned static; over the listed 8.x releases (8.0.12 is
# unlisted), the core suite's BDDfy facts on each, from an empty bin/ and obj/.
#   sh r3a-bddfy-f32.sh <worktree> <logs>
W=$1; LOGS=$2; F=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
python -I "$F/floor_probe2.py" --worktree "$W" --project src/Kronikol.BDDfy.xUnit3/Kronikol.BDDfy.xUnit3.csproj \
  --package TestStack.BDDfy --tests tests/Kronikol.Tests --filter "FullyQualifiedName~BDDfy" \
  --out "$F/r3a-bddfy-f32.txt" --logs "$LOGS/BDDfy-f32" --search --to 8.0.11
echo ALLDONE > "$F/r3a-bddfy-f32.done"
