#!/bin/sh
# Builds the behaviour card (card-behaviour.cs in the dive worktree, against 5.14.0) for every input, with R1's parties.
# Usage: sh cards-all.sh <audit/B dir> <scratchpad dir>
B=$1; S=$2; P=C:/Code/Kronikol-191-dive/plans/SERVICE_CARD_PLAN.harness/probes
O="$B/out/cards"; OB="$B/out/cards-bgsection"; mkdir -p "$O" "$OB"
cd "$P" || exit 1
dotnet build card-behaviour.cs -nologo -v q > "$O/build.log" 2>&1 || { echo build failed; exit 1; }
run() {
  echo "== $1"
  dotnet run --no-build --file card-behaviour.cs -- "$2" "$B/out/parties/$1.json" "$O/$1" 2>&1 | grep -v "warning CS"
  dotnet run --no-build --file card-behaviour.cs -- "$2" "$B/out/parties/$1.json" "$OB/$1" --with-background-section > "$OB/$1.txt" 2>&1
}
for l in xunit-in-memory reqnroll-in-memory xunit-external-sut reqnroll-in-docker; do run "A-$l" "$B/decoded/40290999167/$l.json"; done
for l in nunit-in-memory tunit-in-memory lightbdd-in-memory bddfy-in-memory; do run "A-$l" "$S/decoded/$l.json"; done
for f in "$B/decoded/40473765203"/*.json; do run "B-$(basename "$f" .json)" "$f"; done
for f in "$B/decoded/40491266394"/*.json; do run "C-$(basename "$f" .json)" "$f"; done
run "jest-ingest" "$S/decoded/jest-ingest.json"
run "example-ReqNRoll.xUnit3" "$S/decoded/example-reqnroll.json"
