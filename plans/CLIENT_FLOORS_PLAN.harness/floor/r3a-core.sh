#!/bin/sh
# R3a's first search (plans/CLIENT_FLOORS_PLAN.md 5.2): the core package's Microsoft.AspNetCore.Mvc.Testing, which every
# consumer takes, by binary search over its 10.x releases, the core suite on each. The suite takes Mvc.Testing only
# through the core, so the pin moves it. Run at idle priority: each probe is the whole core suite. FloorLedgerTests is
# left out: it holds the core's csproj to the ledger's 10.0.7, so it fails on every other release the search pins.
#   sh r3a-core.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness
python -I "$H/floor/floor_probe2.py" --worktree "$W" --project src/Kronikol/Kronikol.csproj \
  --package Microsoft.AspNetCore.Mvc.Testing --tests tests/Kronikol.Tests \
  --filter "FullyQualifiedName!~Kronikol.Tests.Packaging.FloorLedgerTests" \
  --out "$H/floor/r3a-core.txt" --logs "$LOGS/core" --search
echo ALLDONE >> "$H/floor/r3a-core.txt"
