#!/bin/sh
# R3a's BDD batch (plans/CLIENT_FLOORS_PLAN.md 5.2), one search after another in the adapters' worktree:
# - LightBDD, three families, each pinning LightBDD.Framework in Kronikol.LightBDD.Core and the runner's package in its
#   adapter: xUnit3 (tests/Kronikol.Tests.LightBDD.xUnit3, which takes LightBDD.XUnit3 at -p:LightBddFloor), TUnit
#   (tests/Kronikol.Tests.LightBDD.TUnit, which takes LightBDD.TUnit only through the adapter) and xUnit2 (the example
#   component project, the only LightBDD.xUnit2 suite, whose own LightBDD.XUnit2 moves with the pin). LightBDD.Core's
#   floor is the least of the three.
# - xunit under Kronikol.LightBDD.xUnit2, with Kronikol.xUnit2's xunit.extensibility.execution, which xunit asks for
#   at its own release, on the same example.
# - TestStack.BDDfy and Reqnroll, through the core suite's facts for each, the suite taking both only through the
#   adapters.
#   sh r3a-bdd.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness; F=$H/floor
EX=examples/Example.Api/tests/Example.Api.Tests.Component.LightBDD.xUnit2
python -I "$F/family_floor.py" --worktree "$W" \
  --project src/Kronikol.LightBDD.Core/Kronikol.LightBDD.Core.csproj --project src/Kronikol.LightBDD.xUnit3/Kronikol.LightBDD.xUnit3.csproj \
  --tests tests/Kronikol.Tests.LightBDD.xUnit3 --pin LightBDD.Framework,LightBDD.XUnit3 --series LightBDD.Framework,LightBDD.XUnit3 \
  --property LightBddFloor --major 3 --out "$F/r3a-lightbdd-xunit3.txt" --logs "$LOGS/LightBDD-xUnit3"
python -I "$F/family_floor.py" --worktree "$W" \
  --project src/Kronikol.LightBDD.Core/Kronikol.LightBDD.Core.csproj --project src/Kronikol.LightBDD.TUnit/Kronikol.LightBDD.TUnit.csproj \
  --tests tests/Kronikol.Tests.LightBDD.TUnit --pin LightBDD.Framework,LightBDD.TUnit --series LightBDD.Framework,LightBDD.TUnit \
  --property LightBddFloor --major 3 --out "$F/r3a-lightbdd-tunit.txt" --logs "$LOGS/LightBDD-TUnit"
python -I "$F/family_floor.py" --worktree "$W" \
  --project src/Kronikol.LightBDD.Core/Kronikol.LightBDD.Core.csproj --project src/Kronikol.LightBDD.xUnit2/Kronikol.LightBDD.xUnit2.csproj \
  --project "$EX/Example.Api.Tests.Component.LightBDD.xUnit2.csproj" \
  --tests "$EX" --pin LightBDD.Framework,LightBDD.XUnit2 --series LightBDD.Framework,LightBDD.XUnit2 \
  --property LightBddFloor --major 3 --out "$F/r3a-lightbdd-xunit2.txt" --logs "$LOGS/LightBDD-xUnit2"
python -I "$F/family_floor.py" --worktree "$W" \
  --project src/Kronikol.LightBDD.xUnit2/Kronikol.LightBDD.xUnit2.csproj --project src/Kronikol.xUnit2/Kronikol.xUnit2.csproj \
  --project "$EX/Example.Api.Tests.Component.LightBDD.xUnit2.csproj" \
  --tests "$EX" --pin xunit,xunit.extensibility.execution --series xunit,xunit.extensibility.execution \
  --property XunitFloor --major 2 --out "$F/r3a-lightbdd-xunit2-xunit.txt" --logs "$LOGS/LightBDD-xUnit2-xunit"
python -I "$F/floor_probe2.py" --worktree "$W" --project src/Kronikol.BDDfy.xUnit3/Kronikol.BDDfy.xUnit3.csproj \
  --package TestStack.BDDfy --tests tests/Kronikol.Tests --filter "FullyQualifiedName~BDDfy" \
  --out "$F/r3a-bddfy.txt" --logs "$LOGS/BDDfy" --search
python -I "$F/floor_probe2.py" --worktree "$W" --project src/Kronikol.ReqNRoll.Core/Kronikol.ReqNRoll.Core.csproj \
  --package Reqnroll --tests tests/Kronikol.Tests --filter "FullyQualifiedName~ReqNRoll|FullyQualifiedName~Reqnroll" \
  --out "$F/r3a-reqnroll.txt" --logs "$LOGS/Reqnroll" --search
echo ALLDONE > "$F/r3a-bdd.done"
