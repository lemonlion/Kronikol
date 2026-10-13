#!/bin/sh
# usage: run_windows.sh <worktree> <mutant dir> <test project> <filter> <log dir>
# Copies each mutant over its file, runs the filtered facts, then puts the original back (from a backup, never git).
WT=$1; MUT=$2; PROJ=$3; FILTER=$4; LOGS=$5
mkdir -p "$LOGS"
cd "$WT" || exit 1
for dir in "$MUT"/*/; do
  id=$(basename "$dir")
  file=$(cd "$dir" && find . -name '*.cs' | sed 's|^\./||')
  cp "$file" "$LOGS/original-$id.cs"
  cp "$dir/$file" "$file"
  dotnet test "$PROJ" --filter "$FILTER" --logger "console;verbosity=normal" > "$LOGS/mut-$id.log" 2>&1
  echo "exit $?" >> "$LOGS/mut-$id.log"
  cp "$LOGS/original-$id.cs" "$file"
  touch "$file"
  failed=$(grep -E '^\s+Failed Kronikol' "$LOGS/mut-$id.log" | sed -E 's/^\s+Failed Kronikol\.Tests\.[A-Za-z]+\.[A-Za-z]+\.//; s/ \[.*$//' | cut -c1-90 | tr '\n' ';')
  built=$(grep -c 'error CS' "$LOGS/mut-$id.log")
  echo "$id: build errors $built; failed: ${failed:-none}" >> "$LOGS/summary.txt"
done
echo done >> "$LOGS/summary.txt"
