#!/bin/sh
# R3a's TUnit search a third time (plans/CLIENT_FLOORS_PLAN.md F35): TUnit's source generator writes the adapter's hook
# registrations into Kronikol.TUnit at its build, bound to that TUnit.Core, so the adapter built on 1.0.0 fails to load
# on 1.53.0 and later ("Field not found: 'TUnit.Core.Sources.AfterTestHooks'"), which the second search, each probe
# building and running on one release, could not see. Here each probe builds the adapter on the release probed and runs
# the suites as CI's normal run does (--no-floor-run): the TUnit suite on the newest 1.x, and the LightBDD TUnit suite on
# the TUnit that LightBDD.TUnit 3.12.1 asks for (1.53.0) or the release probed, whichever is newer. The second search
# already ran each release on itself. Each probe from an empty bin/ and obj/; the package facts are left out (F23).
#   sh r3a-tunit3.sh <worktree> <logs>
W=$1; LOGS=$2; F=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
python -I "$F/floor_probe2.py" --worktree "$W" --project src/Kronikol.TUnit/Kronikol.TUnit.csproj --package TUnit.Core \
  --tests tests/Kronikol.Tests.TUnit --tests tests/Kronikol.Tests.LightBDD.TUnit --no-floor-run \
  --filter "FullyQualifiedName!~PackageTests" --out "$F/r3a-tunit3.txt" --logs "$LOGS/TUnit3" --search
echo ALLDONE > "$F/r3a-tunit3.done"
