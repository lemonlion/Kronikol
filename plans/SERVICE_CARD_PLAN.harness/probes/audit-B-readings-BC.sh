#!/bin/sh
# Readings P1 to P4 (see readings-10pairs.sh) for run B (40473765203: cf47045, Kronikol 5.4.1) against run C
# (40491266394: 68a8760, Kronikol 5.12.1) on all 18 lanes: what moves behaviourHash across an upgrade and a commit.
# Usage: sh readings-BC.sh <audit/B dir>
B=$1; P=C:/Code/Kronikol-191-dive/plans/SERVICE_CARD_PLAN.harness/probes
cd "$P" || exit 1
for v in "P1:--cause-attribution --phase-any-parent" "P2:--cause-attribution" "P3:--cause-attribution --scenario-ops-no-background" "P4:--cause-attribution --scenario-ops-test-only"; do
  name=${v%%:*}; flags=${v#*:}; O="$B/out/readings-BC/$name"; mkdir -p "$O"
  for f in "$B/decoded/40491266394"/*.json; do
    l=$(basename "$f" .json)
    dotnet run --no-build --file card-behaviour.cs -- "$B/decoded/40473765203/$l.json" "$B/out/parties/B-$l.json" "$O/B-$l" $flags > "$O/B-$l.txt" 2>&1
    dotnet run --no-build --file card-behaviour.cs -- "$f" "$B/out/parties/C-$l.json" "$O/C-$l" $flags > "$O/C-$l.txt" 2>&1
    python -I -X utf8 "$B/scripts/behaviour_diff.py" "$O/B-$l" "$O/C-$l" "$name B-vs-C $l" > "$O/diff-$l.txt" 2>&1
    grep -h "^SUMMARY" "$O/diff-$l.txt"
  done
done
