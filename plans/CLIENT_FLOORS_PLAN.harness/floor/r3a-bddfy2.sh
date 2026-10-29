#!/bin/sh
# R3a's BDDfy search again (plans/CLIENT_FLOORS_PLAN.md 5.2), over the listed 8.x releases: the newest, 8.0.12, is
# unlisted (nuget.org dates it 1900-01-01), and the adapter does not build against it, as it made Scanners.Humanize
# static; the search stops at 8.0.11 (--to). A floating 8.* does take the unlisted 8.0.12, as the core suite's run on
# it after F32 found (r3a-bddfy-f32.txt); a fixed floor, the least a range allows, never does.
#   sh r3a-bddfy2.sh <worktree> <logs>
W=$1; LOGS=$2; F=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
echo "# the run below, over the listed releases (--to 8.0.11): 8.0.12 is unlisted" >> "$F/r3a-bddfy.txt"
python -I "$F/floor_probe2.py" --worktree "$W" --project src/Kronikol.BDDfy.xUnit3/Kronikol.BDDfy.xUnit3.csproj \
  --package TestStack.BDDfy --tests tests/Kronikol.Tests --filter "FullyQualifiedName~BDDfy" \
  --out "$F/r3a-bddfy.txt" --logs "$LOGS/BDDfy2" --search --to 8.0.11
echo ALLDONE > "$F/r3a-bddfy2.done"
