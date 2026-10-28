#!/bin/sh
# Decodes every lane report of the given runs with the 5.14.0 tool built in the dive worktree.
# Usage: sh decode-all.sh <audit/B dir> <scratchpad dir>
B=$1; S=$2
K="dotnet C:/Code/Kronikol-191-dive/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
for run in 40473765203 40491266394; do
  mkdir -p "$B/decoded/$run"
  for d in "$B/downloads/$run"/*-report; do
    lane=$(basename "$d" -report)
    out="$B/decoded/$run/$lane.json"
    [ -s "$out" ] && continue
    $K decode "$d" -o "$out" > "$out.log" 2>&1 || echo "FAIL $run $lane"
  done
done
mkdir -p "$B/decoded/40290999167"
for d in "$S/bp/40290999167"/*-report; do
  lane=$(basename "$d" -report)
  out="$B/decoded/40290999167/$lane.json"
  [ -s "$out" ] && continue
  $K decode "$d" -o "$out" > "$out.log" 2>&1 || echo "FAIL 40290999167 $lane"
done
echo done
