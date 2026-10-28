#!/bin/sh
# Downloads the 18 lane report artifacts of one BreakfastProvider CI run, each into its own new empty directory.
# Usage: sh download.sh <run-id> <dest-root>
run=$1; dest=$2
for fw in xunit reqnroll nunit tunit lightbdd bddfy; do
  for mode in in-memory in-docker external-sut; do
    a="$fw-$mode-report"
    d="$dest/$run/$a"
    if [ -d "$d" ] && [ -n "$(ls -A "$d" 2>/dev/null)" ]; then echo "have $a"; continue; fi
    mkdir -p "$d"
    if gh run download "$run" -R lemonlion/BreakfastProvider -n "$a" -D "$d" >"$d.log" 2>&1; then echo "ok $a"; else echo "FAIL $a"; cat "$d.log"; fi
  done
done
