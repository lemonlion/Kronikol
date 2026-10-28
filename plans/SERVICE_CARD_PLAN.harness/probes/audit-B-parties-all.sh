#!/bin/sh
# Runs `kronikol query parties --json` (5.14.0, the dive worktree's build) over every input and saves each envelope.
# Usage: sh parties-all.sh <audit/B dir> <scratchpad dir>
B=$1; S=$2
K="dotnet C:/Code/Kronikol-191-dive/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
O="$B/out/parties"; mkdir -p "$O"
q() { $K query parties "$2" --json --max-bytes 0 > "$O/$1.json" 2> "$O/$1.err" || echo "FAIL $1"; }
for d in "$S/bp/40290999167"/*-report; do q "A-$(basename "$d" -report)" "$d"; done
for l in nunit-in-memory tunit-in-memory lightbdd-in-memory bddfy-in-memory; do q "A-$l" "$S/decoded/$l.json"; done
for d in "$B/downloads/40473765203"/*-report; do q "B-$(basename "$d" -report)" "$d"; done
for d in "$B/downloads/40491266394"/*-report; do q "C-$(basename "$d" -report)" "$d"; done
q "jest-ingest" "$B/inputs/jest-ingest"
for e in LightBDD.xUnit3 ReqNRoll.xUnit3 xUnit3; do q "example-$e" "$B/inputs/examples-5.8.0/$e"; done
echo done
