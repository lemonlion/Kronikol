#!/bin/sh
# R3a's dependency injection floors again (plans/CLIENT_FLOORS_PLAN.md 5.2): with the core's Microsoft.AspNetCore.Mvc.Testing
# at 10.0.0, the Microsoft.Extensions.DependencyInjection.Abstractions that ServiceBus, MediatR and DispatchProxy ask for
# no longer rests on 10.0.7. Each suite takes the container (and MediatR's the logging) at the release probed
# (-p:DiFloor), as the container asks for the abstractions of its own release; each probe from an empty bin/ and obj/.
# The package facts are left out: each holds the nuspec to the ledger, so it fails every probe of another release
# (F23), and the first run stopped on them at 10.0.14.
#   sh r3a-di.sh <worktree> <logs>
W=$1; LOGS=$2; F=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
for p in ServiceBus MediatR DispatchProxy; do
  series=Microsoft.Extensions.DependencyInjection.Abstractions,Microsoft.Extensions.DependencyInjection
  [ "$p" = MediatR ] && series=$series,Microsoft.Extensions.Logging
  python -I "$F/paired_floor.py" --worktree "$W" --project "src/Kronikol.Extensions.$p/Kronikol.Extensions.$p.csproj" \
    --tests "tests/Kronikol.Tests.$p" --pin Microsoft.Extensions.DependencyInjection.Abstractions --series "$series" \
    --property DiFloor --major 10 --filter "FullyQualifiedName!~PackageTests" \
    --out "$F/r3a-di-$(echo $p | tr 'A-Z' 'a-z').txt" --logs "$LOGS/di-$p"
done
echo ALLDONE > "$F/r3a-di.done"
