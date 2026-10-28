#!/bin/sh
# Every reading of the card over the ten same-commit pairs: eight CI lanes (runs A 40290999167 and B 40473765203, both
# cf47045, Kronikol 5.4.1, Linux) and two local lanes (two runs each of 68a8760, Kronikol 5.12.1, Windows).
#   P0 nesting  cause nesting first, phase from any parent, every call in a scenario's ops (the 2.3 prototype's reading)
#   P1 plan     cause attribution first (#192), phase from any parent, every call in a scenario's ops (4.2 as written)
#   P2          P1, and a call whose cause is background keeps its recorded phase
#   P3          P2, and background calls left out of a scenario's ops (Q6)
#   P4          P2, and a scenario's ops only the operations the test itself called
# Usage: sh readings-10pairs.sh <audit/B dir> <scratchpad dir>
B=$1; S=$2; P=C:/Code/Kronikol-191-dive/plans/SERVICE_CARD_PLAN.harness/probes
cd "$P" || exit 1
input() { case "$1" in A-nunit-in-memory|A-tunit-in-memory|A-lightbdd-in-memory|A-bddfy-in-memory) echo "$S/decoded/${1#A-}.json";;
  A-*) echo "$B/decoded/40290999167/${1#A-}.json";; B-*) echo "$B/decoded/40473765203/${1#B-}.json";;
  L1-*) echo "$B/decoded/local/${1#L1-}-run1.json";; L2-*) echo "$B/decoded/local/${1#L2-}-run2.json";; esac; }
pairs="A:B:xunit-in-memory A:B:reqnroll-in-memory A:B:reqnroll-in-docker A:B:xunit-external-sut A:B:nunit-in-memory A:B:tunit-in-memory A:B:lightbdd-in-memory A:B:bddfy-in-memory L1:L2:xunit-in-memory L1:L2:reqnroll-in-memory"
for v in "P0:" "P1:--cause-attribution --phase-any-parent" "P2:--cause-attribution" "P3:--cause-attribution --scenario-ops-no-background" "P4:--cause-attribution --scenario-ops-test-only"; do
  name=${v%%:*}; flags=${v#*:}; O="$B/out/readings/$name"; mkdir -p "$O"
  for t in $pairs; do
    x=${t%%:*}; rest=${t#*:}; y=${rest%%:*}; l=${rest#*:}
    for r in $x $y; do dotnet run --no-build --file card-behaviour.cs -- "$(input $r-$l)" "$B/out/parties/$r-$l.json" "$O/$r-$l" $flags > "$O/$r-$l.txt" 2>&1; done
    python -I -X utf8 "$B/scripts/behaviour_diff.py" "$O/$x-$l" "$O/$y-$l" "$name $x-vs-$y $l" > "$O/diff-$x-$y-$l.txt" 2>&1
    grep -h "^SUMMARY" "$O/diff-$x-$y-$l.txt"
  done
done
