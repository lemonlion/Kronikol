#!/bin/sh
# R2c's dependency injection floors (plans/CLIENT_FLOORS_PLAN.md 5.2): MediatR's and DispatchProxy's
# Microsoft.Extensions.DependencyInjection.Abstractions, each probed below and at the least the Kronikol core's graph
# allows, with the test project's container at 10.0.7 under -p:FloorRun=true. Appends to floor/r2c-di.txt.
#   sh r2c-di.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
for p in MediatR DispatchProxy; do
  echo "# Kronikol.Extensions.$p: Microsoft.Extensions.DependencyInjection.Abstractions below, at and above 10.0.7" >> "$H/r2c-di.txt"
  python -I "$H/floor_probe2.py" --worktree "$W" --project "src/Kronikol.Extensions.$p/Kronikol.Extensions.$p.csproj" \
    --package Microsoft.Extensions.DependencyInjection.Abstractions --tests "tests/Kronikol.Tests.$p" \
    --out "$H/r2c-di.txt" --logs "$LOGS/di-$p" 10.0.0 10.0.6 10.0.7 10.0.14
done
echo ALLDONE > "$H/r2c-di.done"
