#!/bin/sh
# Writes the whole 4.3-shaped service.json (card-behaviour.cs, plan's reading) for every lane and prints its sizes.
# Usage: sh sizes-all.sh <audit/B dir> <scratchpad dir>
B=$1; S=$2; P=C:/Code/Kronikol-191-dive/plans/SERVICE_CARD_PLAN.harness/probes; O="$B/out/sizes"; mkdir -p "$O"
cd "$P" || exit 1
run() { echo "== $1"; dotnet run --no-build --file card-behaviour.cs -- "$2" "$B/out/parties/$1.json" "$O/$1" 2>&1 | grep -E "^  (scenario|no scenario)|^parties|^subject"; }
for f in "$B/decoded/40491266394"/*.json; do run "C-$(basename "$f" .json)" "$f"; done
for f in "$B/decoded/40473765203"/*.json; do run "B-$(basename "$f" .json)" "$f"; done
for l in xunit-in-memory reqnroll-in-memory xunit-external-sut reqnroll-in-docker; do run "A-$l" "$B/decoded/40290999167/$l.json"; done
run "jest-ingest" "$S/decoded/jest-ingest.json"
run "example-ReqNRoll.xUnit3" "$S/decoded/example-reqnroll.json"
